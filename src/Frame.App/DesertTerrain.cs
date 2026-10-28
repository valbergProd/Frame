using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

// What the Desert Drift artwork says about its own desert, measured once (lazily, on the first visible frame) from the
// embedded desert-drift.png; no extra assets. Maps are in image-relative coordinates (0..1) on a 320x180 grid and are
// placed exactly like the backdrop's centred UniformToFill brush (ImageRect), so effects stay on the painted dunes at
// any window size:
// - AirLight: wide-blurred luminance = how much warm light the air holds there (airborne dust is only seen where lit).
// - Glow: lightly blurred luminance = the glowing crests and lit slopes (surface grains, spindrift, saltation).
// - Flow: unit direction along the dune contours (perpendicular to the luminance gradient, oriented downwind, limited
//   to +-45 degrees; the prevailing wind direction where the art has no contour), so creeping sand follows the dunes.
// - Spawn: cumulative weights for surface grains (lit slopes and crests).
// - CrestFar/CrestNear: the crest light (bright parts of the art in pale gold), split by depth with complementary soft
//   weights (sum = 1, no seam) so the highlight flows of far and near dunes can move independently.
// - DriftMask: tileable streaks of blowing sand in soft gust patches: the moving opacity mask of those highlights.
static class DesertTerrain
{
    public const int W = 320, H = 180;
    static float[] airLight = [], glow = [], flowX = [], flowY = [], spawn = [];
    static double aspect = 1672d / 941;
    public static BitmapSource? CrestFar { get; private set; }
    public static BitmapSource? CrestNear { get; private set; }
    public static BitmapSource? DriftMask { get; private set; }
    public static BitmapSource? AirMask { get; private set; } // AirLight as alpha (floor 7 %): where airborne dust is seen
    public static bool Ready { get; private set; }
    public static double Milliseconds { get; private set; } // one-time analysis cost (diagnostics)

    // Same placement as ImageBrush Stretch=UniformToFill (centred) inside a w x h box.
    public static Rect ImageRect(double w, double h)
    {
        double s = Math.Max(w / aspect, h);
        return new Rect((w - s * aspect) / 2, (h - s) / 2, s * aspect, s);
    }

    public static void Ensure()
    {
        if (Ready) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var image = new BitmapImage();
        image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/Assets/desert-drift.png"); image.DecodePixelWidth = 836; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int iw = bgra.PixelWidth, ih = bgra.PixelHeight; var px = new byte[iw * ih * 4]; bgra.CopyPixels(px, iw * 4, 0);
        aspect = iw / (double)ih;
        var lum = new float[iw * ih];
        for (int i = 0; i < lum.Length; i++) lum[i] = (.0722f * px[i * 4] + .7152f * px[i * 4 + 1] + .2126f * px[i * 4 + 2]) / 255f;
        CrestFar = Crest(lum, iw, ih, far: true); CrestNear = Crest(lum, iw, ih, far: false);
        // Analysis grid: box average of the decoded image.
        var grid = new float[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int x0 = x * iw / W, x1 = Math.Max(x0 + 1, (x + 1) * iw / W), y0 = y * ih / H, y1 = Math.Max(y0 + 1, (y + 1) * ih / H);
                float sum = 0; for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) sum += lum[yy * iw + xx];
                grid[y * W + x] = sum / ((x1 - x0) * (y1 - y0));
            }
        var near = Blur(grid, 1, 2); var wide = Blur(grid, 6, 3); var contour = Blur(grid, 3, 3);
        airLight = new float[W * H]; glow = new float[W * H];
        for (int i = 0; i < grid.Length; i++) { airLight[i] = (float)Smooth(.075, .36, wide[i]); glow[i] = (float)Smooth(.11, .5, near[i]); }
        // Contour directions; the strength threshold adapts to the art (60th..92nd percentile of the gradient).
        flowX = new float[W * H]; flowY = new float[W * H]; var magnitude = new float[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float gx = contour[y * W + Math.Min(W - 1, x + 1)] - contour[y * W + Math.Max(0, x - 1)];
                float gy = contour[Math.Min(H - 1, y + 1) * W + x] - contour[Math.Max(0, y - 1) * W + x];
                magnitude[y * W + x] = MathF.Sqrt(gx * gx + gy * gy);
                float tx = -gy, ty = gx; if (tx < 0) { tx = -tx; ty = -ty; }
                float n = MathF.Sqrt(tx * tx + ty * ty); if (n > 1e-6f) { tx /= n; ty /= n; } else { tx = 1; ty = 0; }
                if (MathF.Abs(ty) > tx) { ty = MathF.Sign(ty) * .7071f; tx = .7071f; } // never steeper than 45 degrees
                flowX[y * W + x] = tx; flowY[y * W + x] = ty;
            }
        var sorted = (float[])magnitude.Clone(); Array.Sort(sorted);
        float lo = sorted[(int)(sorted.Length * .6)], hi = Math.Max(lo + 1e-5f, sorted[(int)(sorted.Length * .92)]);
        spawn = new float[W * H]; double total = 0;
        for (int i = 0; i < grid.Length; i++)
        {
            float w = (float)Smooth(lo, hi, magnitude[i]);
            float fx = w * flowX[i] + (1 - w) * .998f, fy = w * flowY[i] + (1 - w) * .05f, n = MathF.Sqrt(fx * fx + fy * fy);
            flowX[i] = fx / n; flowY[i] = fy / n;
            double crest = Smooth(.16, .55, near[i]); total += crest * crest * (.35 + .65 * w); spawn[i] = (float)total;
        }
        if (total <= 0) for (int i = 0; i < spawn.Length; i++) spawn[i] = i + 1;
        DriftMask = Mask();
        var lightPx = new byte[W * H * 4];
        for (int i = 0; i < airLight.Length; i++) { byte a = (byte)(255 * (.07 + .93 * Math.Pow(airLight[i], 1.15))); lightPx[i * 4] = lightPx[i * 4 + 1] = lightPx[i * 4 + 2] = lightPx[i * 4 + 3] = a; }
        AirMask = BitmapSource.Create(W, H, 96, 96, PixelFormats.Pbgra32, null, lightPx, W * 4); AirMask.Freeze();
        Ready = true; Milliseconds = clock.Elapsed.TotalMilliseconds;
    }

    static BitmapSource Crest(float[] lum, int w, int h, bool far)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            double depth = Smooth(.34, .62, y / (double)h); // 0 = far dunes (upper), 1 = near dunes (lower)
            double share = far ? 1 - depth : depth;
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x; double a = Smooth(.24, .74, lum[i]) * share;
                px[i * 4] = (byte)(150 * a); px[i * 4 + 1] = (byte)(210 * a); px[i * 4 + 2] = (byte)(255 * a); px[i * 4 + 3] = (byte)(255 * a); // pale gold, premultiplied
            }
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4); bitmap.Freeze(); return bitmap;
    }

    // Mask of the crest light (tileable 512x256, alpha only): fine horizontal streaks of blowing sand (6..34 px) whose
    // density and strength follow soft periodic patches, over a faint patch glow. Drifting downwind it turns the glowing
    // crests into granular, flowing highlights instead of a uniform brightening.
    static BitmapSource Mask()
    {
        const int w = 512, h = 256;
        uint seed = 0x2545F491; float Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216f; }
        float[] Lattice(int cx, int cy) { var v = new float[cx * cy]; for (int i = 0; i < v.Length; i++) v[i] = Next(); return v; }
        var coarse = Lattice(5, 3); var fine = Lattice(11, 6);
        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        static double Sample(float[] g, int cx, int cy, double u, double v)
        {
            double fx = u * cx, fy = v * cy; int x0 = (int)fx % cx, y0 = (int)fy % cy, x1 = (x0 + 1) % cx, y1 = (y0 + 1) % cy;
            double tx = Fade(fx - Math.Floor(fx)), ty = Fade(fy - Math.Floor(fy));
            double a = g[y0 * cx + x0] + (g[y0 * cx + x1] - g[y0 * cx + x0]) * tx, b = g[y1 * cx + x0] + (g[y1 * cx + x1] - g[y1 * cx + x0]) * tx;
            return a + (b - a) * ty;
        }
        double Patch(double x, double y) => Smooth(.36, .74, .62 * Sample(coarse, 5, 3, x / w, y / h) + .38 * Sample(fine, 11, 6, x / w, y / h));
        var streak = new float[w * h];
        for (int n = 0; n < 2600; n++)
        {
            double x = Next() * w, y = Next() * h, p = Patch(x, y);
            if (Next() > .15 + .85 * p) continue;
            double length = 6 + 28 * Next() * Next(), alpha = .35 + .65 * Next(), fy = y - Math.Floor(y); int row = (int)y;
            for (double d = -length / 2; d <= length / 2; d += .5)
            {
                double end = Math.Min(1, (length / 2 - Math.Abs(d)) / 3 + .15), a = alpha * end * .5;
                int px = ((int)Math.Floor(x + d) % w + w) % w;
                streak[(row % h) * w + px] += (float)(a * (1 - fy)); streak[((row + 1) % h) * w + px] += (float)(a * fy);
            }
        }
        var px2 = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double p = Patch(x, y), a = Math.Min(1, .32 * p + Math.Min(1, streak[y * w + x]) * (.35 + .65 * p));
                byte b = (byte)(255 * a); int i = (y * w + x) * 4;
                px2[i] = px2[i + 1] = px2[i + 2] = px2[i + 3] = b;
            }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px2, w * 4); bitmap.Freeze(); return bitmap;
    }

    // Tileable sheet of fine airborne grains (size x size px, premultiplied): specks are short horizontal streaks (1..maxLength
    // px, soft ends, sub-pixel rows) whose density follows meandering bands and soft patches, so a moving sheet reads as
    // wind streams of dust rather than a uniform pattern. Wraps on all sides (no seam when tiled).
    public static BitmapSource DustSheet(uint seed, int size, int specks, double maxLength, Color color)
    {
        float Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216f; }
        var lattice = new float[5 * 5]; for (int i = 0; i < lattice.Length; i++) lattice[i] = Next();
        double Patch(double u, double v)
        {
            double fx = u * 5, fy = v * 5; int x0 = (int)fx % 5, y0 = (int)fy % 5, x1 = (x0 + 1) % 5, y1 = (y0 + 1) % 5;
            double tx = fx - Math.Floor(fx), ty = fy - Math.Floor(fy); tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            double a = lattice[y0 * 5 + x0] + (lattice[y0 * 5 + x1] - lattice[y0 * 5 + x0]) * tx, b = lattice[y1 * 5 + x0] + (lattice[y1 * 5 + x1] - lattice[y1 * 5 + x0]) * tx;
            return a + (b - a) * ty;
        }
        double Density(double u, double v)
        {
            double band = .5 + .5 * Math.Sin(2 * Math.PI * (2 * v + .32 * Math.Sin(2 * Math.PI * u) + .14 * Math.Sin(2 * Math.PI * (2 * u + v))));
            return .12 + .88 * Smooth(.3, 1, band) * (.45 + .55 * Patch(u, v));
        }
        var acc = new float[size * size];
        for (int placed = 0, tries = 0; placed < specks && tries < specks * 20; tries++)
        {
            double x = Next() * size, y = Next() * size;
            if (Next() > Density(x / size, y / size)) continue;
            placed++;
            double length = 1 + (maxLength - 1) * Next() * Next(), alpha = .25 + .65 * Next(), fy = y - Math.Floor(y);
            int row = (int)Math.Floor(y);
            for (double d = -length / 2; d <= length / 2; d += .5)
            {
                double end = Math.Min(1, (length / 2 - Math.Abs(d)) / .9 + .25), a = alpha * end * .5; // two samples per px
                int px = ((int)Math.Floor(x + d) % size + size) % size;
                acc[(row % size) * size + px] += (float)(a * (1 - fy));
                acc[((row + 1) % size) * size + px] += (float)(a * fy);
            }
        }
        var bytes = new byte[size * size * 4];
        for (int i = 0; i < acc.Length; i++)
        {
            double a = Math.Min(1, acc[i]);
            bytes[i * 4] = (byte)(color.B * a); bytes[i * 4 + 1] = (byte)(color.G * a); bytes[i * 4 + 2] = (byte)(color.R * a); bytes[i * 4 + 3] = (byte)(255 * a);
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, bytes, size * 4); bitmap.Freeze(); return bitmap;
    }

    static float[] Blur(float[] source, int radius, int passes)
    {
        var a = (float[])source.Clone(); var b = new float[a.Length];
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { float s = 0; for (int k = -radius; k <= radius; k++) s += a[y * W + Math.Clamp(x + k, 0, W - 1)]; b[y * W + x] = s / (2 * radius + 1); }
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { float s = 0; for (int k = -radius; k <= radius; k++) s += b[Math.Clamp(y + k, 0, H - 1) * W + x]; a[y * W + x] = s / (2 * radius + 1); }
        }
        return a;
    }
    public static double Smooth(double from, double to, double value) { double t = Math.Clamp((value - from) / (to - from), 0, 1); return t * t * (3 - 2 * t); }

    static double Bilinear(float[] map, double u, double v)
    {
        if (map.Length == 0) return 0;
        double fx = Math.Clamp(u * W - .5, 0, W - 1.001), fy = Math.Clamp(v * H - .5, 0, H - 1.001);
        int x = (int)fx, y = (int)fy; double tx = fx - x, ty = fy - y; int i = y * W + x;
        double top = map[i] + (map[i + 1] - map[i]) * tx, bottom = map[i + W] + (map[i + W + 1] - map[i + W]) * tx;
        return top + (bottom - top) * ty;
    }
    public static double AirLight(double u, double v) => Bilinear(airLight, u, v);
    public static double Glow(double u, double v) => Bilinear(glow, u, v);
    public static (double X, double Y) Flow(double u, double v)
    {
        if (flowX.Length == 0) return (1, 0);
        int i = Math.Clamp((int)(v * H), 0, H - 1) * W + Math.Clamp((int)(u * W), 0, W - 1);
        double x = Bilinear(flowX, u, v), y = Bilinear(flowY, u, v), n = Math.Sqrt(x * x + y * y);
        return n > 1e-6 ? (x / n, y / n) : (flowX[i], flowY[i]);
    }
    // A random point on the lit dunes, weighted by Spawn (binary search in the cumulative weights).
    public static (double U, double V) SpawnPoint(double r1, double r2, double r3)
    {
        if (spawn.Length == 0) return (r1, .5 + .5 * r2);
        int i = Array.BinarySearch(spawn, (float)(r1 * spawn[^1])); if (i < 0) i = ~i; i = Math.Min(i, spawn.Length - 1);
        return ((i % W + r2) / W, (i / W + r3) / H);
    }
}
