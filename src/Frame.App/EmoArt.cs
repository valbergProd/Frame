using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

// Emo mode backgrounds (0.6.0): four illustrated night scenes drawn procedurally in WPF vector graphics (no image assets,
// nothing photographic): Simply Emo (dark and minimal: a rim-lit profile with a long side-swept fringe), Dead rose garden,
// Rainy neon alley (a lonely figure under the neon) and Moonlit graveyard (a lonely figure among the graves). A scene is a
// few layered Drawings (sky, far, mid, near) plus a finishing layer (readability veil over FRAME's content column, vignette,
// fine grain against banding). EmoScene shows them under one BitmapCache: the GPU rasterizes the illustration once per
// window size/background and afterwards only composites it. Composition: reference 1280 × 800 DIP; focal elements are
// anchored to the right edge (X) and scale with the height (K), so the left content column stays calm at every size.
static class EmoArt
{
    public readonly record struct Layer(Drawing Drawing, double Blur);

    public static string Name(string key) => key switch
    {
        "simply-emo" => "Simply Emo", "dead-rose-garden" => "Dead rose garden", "rainy-neon-alley" => "Rainy neon alley", _ => "Moonlit graveyard"
    };

    public static Layer[] Build(string key, double width, double height)
    {
        var s = new Stage(Math.Max(64, width), Math.Max(64, height));
        Layer[] scene = key switch { "simply-emo" => SimplyEmo(s), "rainy-neon-alley" => Alley(s), "moonlit-graveyard" => Graveyard(s), _ => RoseGarden(s) };
        return [.. scene, new Layer(Finish(s), 0)];
    }

    // Reference composition 1280 × 800: X(x) keeps a reference x at the same distance from the right edge (scaled), Y and S
    // scale with the height. Narrow windows compress horizontal positions a little (never sizes), so the right-side subject
    // moves toward the free area right of FRAME's controls instead of under them.
    sealed class Stage(double w, double h)
    {
        public readonly double W = w, H = h, K = h / 800, Fx = Math.Clamp((w - 560) / 720, .62, 1);
        public double X(double x) => W - (1280 - x) * K * Fx;
        public double Y(double y) => y * K;
        public double S(double v) => v * K;
        public Point P(double x, double y) => new(X(x), Y(y));
        // A subject centred at x, extending `half` (reference units) to each side, stays right of FRAME's controls (they end near
        // x 850 DIP) while the window is wide enough; never closer to the right edge than its own width.
        public double Free(double x, double half) => Math.Max(x, Math.Min(850 + half * K, W - 2 * half * K));
        public Rect All => new(0, 0, W, H);
    }

    // ---- paint helpers (everything frozen) ----
    static readonly Color Ink = C(0x0A0809);
    static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
    static T F<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static SolidColorBrush Solid(Color c) => F(new SolidColorBrush(c));
    static Pen Stroke(Brush b, double width) => F(new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
    static LinearGradientBrush Linear(Point a, Point b, params (double At, Color Color)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = a, EndPoint = b, MappingMode = BrushMappingMode.Absolute };
        foreach (var (at, color) in stops) g.GradientStops.Add(new GradientStop(color, at));
        return F(g);
    }
    // Soft light: (1 - r²)² falloff in 12 stops, so a glow never shows a ring or an edge.
    static RadialGradientBrush Glow(Point c, double rx, double ry, Color color, double peak)
    {
        var g = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = rx, RadiusY = ry };
        for (int i = 0; i <= 12; i++) { double r = i / 12d, f = 1 - r * r; g.GradientStops.Add(new GradientStop(A(color, peak * f * f), r)); }
        return F(g);
    }
    static void Light(DrawingContext dc, Point c, double rx, double ry, Color color, double peak) => dc.DrawEllipse(Glow(c, rx, ry, color, peak), null, c, rx, ry);
    // Path markup in a design box, scaled and placed (mirror: flipped horizontally around x).
    static Geometry Shape(string data, double x, double y, double scale, bool mirror = false)
    {
        var g = Geometry.Parse(data); if (g.IsFrozen) g = g.Clone();
        g.Transform = new MatrixTransform(mirror ? -scale : scale, 0, 0, scale, x, y);
        return F(g);
    }
    static Geometry Poly(params Point[] p)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(p[0], true, true); for (int i = 1; i < p.Length; i++) c.LineTo(p[i], false, true); }
        return F(g);
    }
    static Point Bez(Point a, Point b, Point c, Point d, double t)
    {
        double u = 1 - t;
        return new(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X, u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }
    static Vector BezDir(Point a, Point b, Point c, Point d, double t)
    {
        double u = 1 - t;
        var v = 3 * u * u * (b - a) + 6 * u * t * (c - b) + 3 * t * t * (d - c); if (v.Length > 1e-9) v.Normalize(); return v;
    }
    static Geometry Curve(Point a, Point b, Point c, Point d)
    {
        var g = new StreamGeometry();
        using (var x = g.Open()) { x.BeginFigure(a, false, false); x.BezierTo(b, c, d, true, true); }
        return F(g);
    }

    // Finishing layer, shared by every scene: the readability veil over FRAME's content column (absolute DIP: the column
    // starts at x 190 and FRAME's controls end near x 860), a soft vignette and fine grain (dithers 8-bit dark gradients).
    static Drawing Finish(Stage s)
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Linear(new(0, 0), new(1010, 0), (0, A(Ink, .52)), (.6, A(Ink, .42)), (.84, A(Ink, .18)), (1, A(Ink, 0))), null, s.All);
            dc.DrawRectangle(Linear(new(0, 0), new(0, s.Y(170)), (0, A(Ink, .35)), (1, A(Ink, 0))), null, new Rect(0, 0, Math.Min(s.W, 1010), s.Y(170)));
            var vignette = new RadialGradientBrush { Center = new(.64, .42), GradientOrigin = new(.64, .42), RadiusX = .9, RadiusY = 1 };
            vignette.GradientStops.Add(new GradientStop(A(Colors.Black, 0), 0)); vignette.GradientStops.Add(new GradientStop(A(Colors.Black, 0), .55));
            vignette.GradientStops.Add(new GradientStop(A(Colors.Black, .22), .8)); vignette.GradientStops.Add(new GradientStop(A(Colors.Black, .5), 1));
            dc.DrawRectangle(F(vignette), null, s.All);
            dc.DrawRectangle(Grain(), null, s.All);
        }
        return F(g);
    }
    static ImageBrush Grain()
    {
        const int n = 128; var px = new byte[n * n * 4]; var rnd = new Random(7);
        for (int i = 0; i < n * n; i++)
        {
            int a = rnd.Next(0, 13); byte v = (byte)(rnd.Next(2) == 0 ? 0 : a * 255 / 255); // premultiplied black or white speck
            px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = v; px[i * 4 + 3] = (byte)a;
        }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
        return F(new ImageBrush(F(bmp)) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, n, n), Stretch = Stretch.Fill });
    }

    // ---- figures (design boxes; y down) ----
    // Head and shoulders in profile, facing left and slightly bowed; the long fringe sweeps over the brow and hangs in front of
    // the eye; choppy layers at the back. Box 440 × 560.
    const string Profile =
        "M 127,232 C 121,238 113,247 107,255 L 101,262 C 103,267 109,270 116,270 C 117,275 115,279 112,282 C 116,285 117,288 115,291 " +
        "C 113,295 116,298 121,299 C 124,303 122,309 122,314 C 122,324 130,332 142,335 C 160,339 178,341 192,347 C 200,370 206,398 210,420 " +
        "C 186,440 140,458 96,478 C 60,494 30,520 10,560 L 440,560 C 436,520 424,486 400,462 C 380,444 352,432 336,420 C 330,396 328,372 330,350 " +
        "C 340,356 352,362 364,374 C 360,352 356,336 356,322 C 372,330 386,340 398,354 C 390,326 386,304 386,286 C 400,292 414,302 424,314 " +
        "C 416,284 412,260 412,240 C 424,244 432,252 438,260 C 430,226 424,200 418,180 C 414,130 392,84 352,58 C 318,36 268,30 228,36 " +
        "C 184,44 146,66 122,96 C 102,122 90,150 84,178 C 80,194 72,208 62,218 C 74,214 84,208 90,200 C 92,220 90,242 84,264 " +
        "C 96,252 102,238 105,224 C 108,228 110,232 112,236 Z";
    static readonly string[] ProfileStrands =
    [
        "M 300,52 C 236,56 160,104 102,200", "M 324,74 C 254,88 178,140 108,236", "M 276,44 C 206,52 140,96 90,172",
        "M 334,64 C 372,104 398,172 404,240", "M 318,92 C 352,150 370,230 368,300", "M 300,120 C 330,190 340,260 330,330",
    ];
    // Standing figure, slim, hands in pockets, the fringe hanging in front of the face (facing left). Box 64 × 200, feet at 200.
    const string Figure =
        "M 34,0 C 44,0 52,4 55,11 C 57,16 58,20 58,24 C 60,27 61,30 62,33.5 C 59,32.4 57,31.6 55,31.4 C 56,34 56.4,36 57,38.6 " +
        "C 54,37.4 51,36.6 48,36.2 L 47,40 C 54,42 60,45 61,52 C 63,64 63,78 61,92 L 58,104 L 54,106 L 53,148 L 51,192 C 53,195 53,199 50,200 " +
        "L 41,200 L 41,194 L 40,150 L 37,111 L 34,150 L 33,194 L 33,200 L 21,200 C 21,197 24,194 27,193 L 27,148 L 26,106 L 22,104 " +
        "C 18,92 18,78 19,66 C 20,56 23,48 30,45 L 33,40 C 31,39.5 29,38.5 28,37.5 C 26.5,37 25.6,36 25.6,34.6 C 25.2,33.6 24.4,32.6 24.8,31.7 " +
        "C 24.2,31 23.8,30.4 24.2,29.8 L 22.8,28.2 C 23.2,27.2 24,26 24.8,25 C 22,28.5 18.5,33 14,37.6 C 14.4,32 15.2,27 16.4,22.5 " +
        "C 17.6,15 21,7 26,3.4 C 28.6,1.4 31,0.2 34,0 Z";

    // A figure with soft back light: halo, silhouette, and a thin rim inside the edge toward the light.
    static void LitFigure(DrawingContext dc, Geometry g, Point light, double reach, Color rim, double strength, double k)
    {
        dc.DrawGeometry(null, Stroke(Glow(light, reach, reach, rim, .13 * strength), 9 * k), g);
        dc.DrawGeometry(null, Stroke(Glow(light, reach, reach, rim, .22 * strength), 3.5 * k), g);
        dc.DrawGeometry(Solid(C(0x050406)), null, g);
        dc.PushClip(g); dc.DrawGeometry(null, Stroke(Glow(light, reach, reach, rim, .95 * strength), 2.2 * k), g); dc.Pop();
    }

    // ---- Simply Emo: clean, dark, minimal ----
    static Layer[] SimplyEmo(Stage s)
    {
        var sky = new DrawingGroup();
        using (var dc = sky.Open())
        {
            dc.DrawRectangle(Linear(new(0, 0), new(0, s.H), (0, C(0x0C0A0D)), (.6, C(0x0E0A10)), (1, C(0x080609))), null, s.All);
            Light(dc, s.P(1010, 380), s.S(620), s.S(500), C(0x2B2035), .85);
            Light(dc, s.P(1230, 760), s.S(460), s.S(300), C(0x3A1222), .6);
            Light(dc, s.P(860, 300), s.S(260), s.S(240), C(0x45182A), .3);
            // a thin crescent high on the right, with a breath of light
            Light(dc, s.P(1150, 150), s.S(120), s.S(120), C(0x5E5070), .14);
            var moon = Geometry.Combine(new EllipseGeometry(s.P(1150, 150), s.S(28), s.S(28)), new EllipseGeometry(s.P(1161, 142), s.S(25.5), s.S(25.5)), GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(Solid(A(C(0xDCCFDA), .6)), null, F(moon));
            var rnd = new Random(5);
            for (int i = 0; i < 26; i++) { var p = s.P(700 + rnd.Next(580), 40 + rnd.Next(300)); dc.DrawEllipse(Solid(A(C(0xC8BCD0), .08 + .18 * rnd.NextDouble())), null, p, s.S(.7), s.S(.7)); }
        }
        var figure = new DrawingGroup();
        using (var dc = figure.Open())
        {
            double scale = s.K, x = Math.Max(s.X(812), Math.Min(850 - 62 * s.K, s.W - 160 * s.K)), y = s.Y(176); // the fringe stays right of the controls
            var g = Shape(Profile, x, y, scale);
            var light = s.P(760, 250);
            dc.DrawGeometry(null, Stroke(Glow(light, s.S(430), s.S(430), C(0xA33A55), .12), s.S(18)), g);
            dc.DrawGeometry(null, Stroke(Glow(light, s.S(430), s.S(430), C(0xA33A55), .2), s.S(6)), g);
            dc.DrawGeometry(Linear(s.P(900, 200), s.P(1220, 560), (0, C(0x0A080B)), (1, C(0x050406))), null, g);
            dc.PushClip(g);
            foreach (var strand in ProfileStrands) dc.DrawGeometry(null, Stroke(Solid(A(C(0x2A2030), .55)), s.S(1.1)), Shape(strand, x, y, scale));
            dc.DrawGeometry(null, Stroke(Glow(light, s.S(400), s.S(400), C(0xD8667E), 1), s.S(2.6)), g);
            dc.DrawGeometry(null, Stroke(Glow(s.P(1330, 280), s.S(300), s.S(300), C(0x6E5C96), .55), s.S(2.2)), g);
            dc.Pop();
        }
        return [new(F(sky), 0), new(F(figure), 0)];
    }

    // ---- Dead rose garden ----
    static readonly Geometry Petal = F(Geometry.Parse("M 0,0 C -0.62,-0.16 -0.76,-0.8 -0.2,-1 C 0,-1.06 0.3,-1.02 0.44,-0.9 C 0.8,-0.6 0.6,-0.14 0,0 Z"));
    static readonly Geometry Leaf = F(Geometry.Parse("M 0,0 C 0.28,-0.26 0.72,-0.3 1,0 C 0.72,0.26 0.3,0.24 0,0 Z"));
    // A rose from above, tilted (squash < 1 shows it from the side): three rings of petals shaded dark at the heart, crimson
    // in the body and browned at the withered rims; thin dark edges separate the petals.
    static void Rose(DrawingContext dc, Point c, double r, double rotation, double squash, Random rnd, double light = 1)
    {
        var body = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new(0, 0), GradientOrigin = new(0, 0), RadiusX = 1, RadiusY = 1 };
        body.GradientStops.Add(new GradientStop(C(0x14030A), 0)); body.GradientStops.Add(new GradientStop(C(0x3A0916), .4));
        body.GradientStops.Add(new GradientStop(Scale(C(0x701628), light), .76)); body.GradientStops.Add(new GradientStop(C(0x4A0F1C), .93)); body.GradientStops.Add(new GradientStop(C(0x2A0A10), 1));
        F(body); var edge = F(new Pen(Solid(C(0x120207)), .03));
        var place = Matrix.Identity; place.Scale(r, r * squash); place.Rotate(rotation); place.Translate(c.X, c.Y);
        int[] count = [7, 6, 5, 4]; double[] length = [1, .84, .64, .42], width = [1, .92, .86, .8];
        for (int ring = 0; ring < 4; ring++)
        {
            double offset = rnd.NextDouble() * 360;
            for (int i = 0; i < count[ring]; i++)
            {
                var m = Matrix.Identity; m.Scale(width[ring] * (.9 + .2 * rnd.NextDouble()), length[ring] * (.9 + .16 * rnd.NextDouble()));
                m.Rotate(offset + i * 360d / count[ring] + rnd.NextDouble() * 14); m.Append(place);
                dc.PushTransform(new MatrixTransform(m)); dc.DrawGeometry(body, edge, Petal); dc.Pop();
            }
        }
        dc.PushTransform(new MatrixTransform(place));
        dc.DrawGeometry(null, F(new Pen(Solid(C(0x0E0205)), .05)), F(Geometry.Parse("M -0.12,0.02 A 0.12,0.12 0 1 1 0.1,0.06 A 0.22,0.22 0 1 1 -0.24,0.04")));
        dc.Pop();
    }
    // A rose seen from the side (base at 0,0, bloom toward -y, unit height): sepals, two back petals, the closed heart, two
    // petals curling outward, the front cup and (wilted) one petal hanging loose. Rotate past 90° to hang the head.
    static readonly Geometry[] Cup = new[]
    {
        "M -0.05,-0.25 C -0.45,-0.3 -0.64,-0.7 -0.5,-1.0 C -0.32,-1.05 -0.12,-0.99 0.04,-0.88 Z",
        "M 0.05,-0.25 C 0.45,-0.3 0.64,-0.72 0.48,-1.02 C 0.3,-1.07 0.1,-0.99 -0.04,-0.9 Z",
        "M -0.24,-0.6 C -0.3,-0.92 0.22,-1.0 0.26,-0.66 C 0.12,-0.76 -0.1,-0.74 -0.24,-0.6 Z",
        "M -0.06,-0.1 C -0.5,-0.12 -0.8,-0.46 -0.74,-0.86 C -0.6,-0.72 -0.44,-0.64 -0.3,-0.6 C -0.26,-0.4 -0.18,-0.22 -0.06,-0.1 Z",
        "M 0.06,-0.1 C 0.5,-0.12 0.8,-0.46 0.74,-0.86 C 0.6,-0.72 0.44,-0.64 0.3,-0.6 C 0.26,-0.4 0.18,-0.22 0.06,-0.1 Z",
        "M -0.58,-0.64 C -0.62,-0.3 -0.32,-0.02 0,0 C 0.32,-0.02 0.62,-0.3 0.58,-0.64 C 0.36,-0.52 0.16,-0.56 0,-0.68 C -0.16,-0.56 -0.36,-0.52 -0.58,-0.64 Z",
        "M 0.52,-0.56 C 0.74,-0.42 0.84,-0.06 0.74,0.26 C 0.64,0.06 0.56,-0.2 0.42,-0.42 Z",
    }.Select(d => F(Geometry.Parse(d))).ToArray();
    static readonly Geometry Sepals = F(Geometry.Parse("M 0,0 C -0.18,0.06 -0.36,0.2 -0.46,0.38 C -0.28,0.28 -0.12,0.18 0.02,0.08 Z M 0,0 C 0.18,0.06 0.36,0.2 0.46,0.38 C 0.28,0.28 0.12,0.18 -0.02,0.08 Z M -0.05,0 C -0.05,0.16 -0.02,0.3 0.02,0.44 C 0.07,0.28 0.07,0.12 0.05,0 Z"));
    static readonly Geometry CupRim = F(Geometry.Parse("M -0.56,-0.63 C -0.36,-0.52 -0.16,-0.56 0,-0.67 C 0.16,-0.56 0.36,-0.52 0.56,-0.63"));
    static void SideRose(DrawingContext dc, Point at, double size, double rotation, double light, bool wilted)
    {
        var m = Matrix.Identity; m.Scale(size, size); m.Rotate(rotation); m.Translate(at.X, at.Y);
        dc.PushTransform(new MatrixTransform(m));
        var edge = F(new Pen(Solid(C(0x100206)), .022));
        for (int i = 0; i < Cup.Length; i++)
        {
            if (i == 6 && !wilted) continue;
            double shade = light * (i < 2 ? .72 : i == 2 ? .6 : i < 5 ? .92 : 1.06);
            dc.DrawGeometry(Linear(new(0, .1), new(0, -1.05), (0, C(0x1A040B)), (.42, Scale(C(0x4E0F1E), shade)), (.84, Scale(C(0x7C1A31), shade)), (1, C(0x3A0A14))), edge, Cup[i]);
        }
        dc.DrawGeometry(null, F(new Pen(Solid(A(C(0xB4425C), .42)), .028)), CupRim);
        dc.DrawGeometry(Solid(C(0x140B0F)), null, Sepals);
        dc.Pop();
    }
    static Color Scale(Color c, double f) => Color.FromRgb((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f));
    static void LeafAt(DrawingContext dc, Point at, double length, double angle, Brush fill, Pen? vein)
    {
        var m = Matrix.Identity; m.Scale(length, length); m.Rotate(angle); m.Translate(at.X, at.Y);
        dc.PushTransform(new MatrixTransform(m)); dc.DrawGeometry(fill, null, Leaf); if (vein != null) dc.DrawLine(vein, new(0.05, 0), new(0.9, 0)); dc.Pop();
    }
    // A thorny stem along a cubic curve, with leaves and a rose (or a closed, drooping bud) at its end.
    static void Stem(DrawingContext dc, Stage s, Point a, Point b, Point c, Point d, double width, Random rnd, Brush leaf, Pen vein)
    {
        var stem = Stroke(Linear(a, d, (0, C(0x0C080A)), (1, C(0x1A0F14))), s.S(width));
        dc.DrawGeometry(null, stem, Curve(a, b, c, d));
        var thorn = Solid(C(0x0E090B));
        for (double t = .08; t < .95; t += .07 + .05 * rnd.NextDouble())
        {
            var p = Bez(a, b, c, d, t); var dir = BezDir(a, b, c, d, t); var n = new Vector(-dir.Y, dir.X) * ((int)(t * 100) % 2 == 0 ? 1 : -1);
            double sz = s.S(width * (1.3 + rnd.NextDouble()));
            dc.DrawGeometry(thorn, null, Poly(p - dir * sz * .5, p + n * sz * 1.4 + dir * sz * .6, p + dir * sz * .5));
            if (rnd.NextDouble() < .32) LeafAt(dc, p, s.S(16 + 12 * rnd.NextDouble()), Math.Atan2(n.Y, n.X) * 180 / Math.PI + rnd.Next(-35, 35), leaf, vein);
        }
    }

    static Layer[] RoseGarden(Stage s)
    {
        var rnd = new Random(31);
        var sky = new DrawingGroup();
        using (var dc = sky.Open())
        {
            dc.DrawRectangle(Linear(new(0, 0), new(0, s.H), (0, C(0x0A080B)), (.36, C(0x120B12)), (.64, C(0x27101A)), (.8, C(0x150A0F)), (1, C(0x0A0708))), null, s.All);
            Light(dc, s.P(1040, 560), s.S(640), s.S(230), C(0x5A1C2E), .6);
            Light(dc, s.P(1170, 500), s.S(280), s.S(150), C(0x7E2C40), .28);
            Light(dc, s.P(1130, 110), s.S(440), s.S(260), C(0x2A1E34), .5);
            for (int i = 0; i < 7; i++) Light(dc, s.P(760 + rnd.Next(540), 150 + rnd.Next(300)), s.S(150 + rnd.Next(220)), s.S(5 + rnd.Next(9)), C(0x070507), .5);
            // a hazy, low moon veiled by the dusk
            Light(dc, s.P(1188, 230), s.S(70), s.S(70), C(0x9C7E92), .22);
            dc.DrawEllipse(Solid(A(C(0xB9A2B2), .32)), null, s.P(1188, 230), s.S(17), s.S(17));
        }
        var far = new DrawingGroup();
        using (var dc = far.Open())
        {
            var hedge = Solid(C(0x1B111A));
            for (double x = 300; x < 1320; x += 22)
            {
                double y = 560 + 10 * Math.Sin(x * .013) + rnd.Next(-6, 6), r = 24 + rnd.Next(26);
                dc.DrawEllipse(hedge, null, s.P(x, y), s.S(r * 1.2), s.S(r));
            }
            dc.DrawRectangle(hedge, null, new Rect(0, s.Y(570), s.W, s.H - s.Y(570)));
            foreach (var (cx, top) in new[] { (996.0, 400.0), (1030.0, 430.0), (1252.0, 380.0) }) // cypresses
                dc.DrawGeometry(Solid(C(0x150D15)), null, Shape("M 0,0 C 10,40 18,110 16,200 L -16,200 C -18,110 -10,40 0,0 Z", s.X(cx), s.Y(top), s.K));
        }
        var mid = new DrawingGroup();
        using (var dc = mid.Open())
        {
            // bushes behind the fence with small dark roses
            var leaf = Solid(C(0x150C11)); var leaf2 = Solid(C(0x1E1016));
            for (int i = 0; i < 70; i++) LeafAt(dc, s.P(620 + rnd.Next(680), 586 + rnd.Next(70)), s.S(14 + rnd.Next(14)), rnd.Next(360), i % 3 == 0 ? leaf2 : leaf, null);
            for (int i = 0; i < 9; i++) Rose(dc, s.P(700 + rnd.Next(580), 588 + rnd.Next(40)), s.S(7 + rnd.Next(5)), rnd.Next(360), .65, rnd, .8);
            // stone gate post
            var post = Linear(s.P(892, 0), s.P(934, 0), (0, C(0x1C1418)), (.3, C(0x120D11)), (1, C(0x0B080A)));
            dc.DrawRectangle(post, null, new Rect(s.X(892), s.Y(476), s.X(934) - s.X(892), s.Y(720) - s.Y(476)));
            dc.DrawRectangle(Solid(C(0x150F13)), null, new Rect(s.X(886), s.Y(466), s.X(940) - s.X(886), s.Y(12)));
            dc.DrawEllipse(Glow(s.P(908, 444), s.S(18), s.S(18), C(0x2A1C22), 1), null, s.P(913, 450), s.S(13), s.S(13));
            dc.DrawEllipse(Solid(C(0x120C10)), null, s.P(913, 452), s.S(12), s.S(12));
            // wrought-iron fence with spear finials and a faint rim of the dusk light
            var iron = Solid(C(0x0A0709)); var rim = Solid(A(C(0x8A3A50), .32));
            double top = 520, bottom = 720;
            dc.DrawRectangle(iron, null, new Rect(s.X(560), s.Y(top + 26), s.W - s.X(560), s.S(4)));
            dc.DrawRectangle(iron, null, new Rect(s.X(560), s.Y(bottom - 50), s.W - s.X(560), s.S(4)));
            for (double x = 560; x < 1300; x += 24)
            {
                if (x > 880 && x < 946) continue; // the gate post
                double px = s.X(x), lift = (x % 48 == 8) ? 10 : 0;
                dc.DrawRectangle(rim, null, new Rect(px - s.S(2.2), s.Y(top + 10 - lift), s.S(1), s.Y(bottom) - s.Y(top + 10 - lift)));
                dc.DrawRectangle(iron, null, new Rect(px - s.S(1.5), s.Y(top + 10 - lift), s.S(3), s.Y(bottom) - s.Y(top + 10 - lift)));
                dc.DrawGeometry(iron, null, Poly(new(px, s.Y(top - lift)), new(px + s.S(3.4), s.Y(top + 9 - lift)), new(px, s.Y(top + 13 - lift)), new(px - s.S(3.4), s.Y(top + 9 - lift))));
            }
            Light(dc, s.P(980, 660), s.S(640), s.S(48), C(0x6E5464), .16);
        }
        var near = new DrawingGroup();
        using (var dc = near.Open())
        {
            var leaf = Solid(C(0x120B0F)); var vein = F(new Pen(Solid(A(C(0x3A1E28), .7)), .02));
            Stem(dc, s, s.P(1238, 760), s.P(1226, 600), s.P(1150, 430), s.P(1072, 392), 4.2, rnd, leaf, vein);
            Stem(dc, s, s.P(1210, 760), s.P(1200, 610), s.P(1188, 470), s.P(1162, 318), 3.6, rnd, leaf, vein);
            Stem(dc, s, s.P(1268, 760), s.P(1272, 620), s.P(1250, 530), s.P(1222, 478), 3.2, rnd, leaf, vein);
            Stem(dc, s, s.P(1186, 760), s.P(1150, 660), s.P(1112, 610), s.P(1058, 572), 2.6, rnd, leaf, vein);
            Stem(dc, s, s.P(1176, 520), s.P(1140, 500), s.P(1110, 520), s.P(1094, 548), 2.2, rnd, leaf, vein);
            // dead roses hanging their heads; one closed bud; petals fallen on the path
            SideRose(dc, s.P(1072, 392), s.S(46), 198, .96, true);
            SideRose(dc, s.P(1162, 318), s.S(40), -16, 1, false);
            SideRose(dc, s.P(1222, 478), s.S(32), 152, .96, true);
            SideRose(dc, s.P(1094, 548), s.S(20), 168, .86, false);
            dc.DrawGeometry(Solid(C(0x3C0A16)), F(new Pen(Solid(C(0x14030A)), s.S(.8))), Shape("M 0,0 C -6,6 -8,18 0,26 C 8,18 6,6 0,0 Z", s.X(1052), s.Y(572), s.K));
            for (int i = 0; i < 9; i++)
            {
                var m = Matrix.Identity; m.Scale(s.S(5 + rnd.Next(4)), s.S(3 + rnd.Next(3))); m.Rotate(rnd.Next(360)); var p = s.P(860 + rnd.Next(400), 646 + rnd.Next(18)); m.Translate(p.X, p.Y);
                dc.PushTransform(new MatrixTransform(m)); dc.DrawGeometry(Solid(C(i % 2 == 0 ? 0x4A0E1Cu : 0x2E0810u)), null, Petal); dc.Pop();
            }
        }
        return [new(F(sky), 0), new(F(far), 3), new(F(mid), 0), new(F(near), 0)];
    }

    // ---- Rainy neon alley ----
    static Layer[] Alley(Stage s)
    {
        var rnd = new Random(17);
        Point vp = s.P(948, 418);
        double fx0 = s.X(902), fx1 = s.X(988), farGround = s.Y(450), farTop = s.Y(170);
        double M = (farGround - vp.Y) / 1.6; // pixels per metre at the far end (eye height 1.6 m)
        double RightX(double t) => vp.X + (fx1 - vp.X) * t;                    // right wall at depth scale t (1 = far end)
        double RightY(double t, double metres) => vp.Y + (farGround - vp.Y) * t - metres * M * t;
        double LeftX(double t) => vp.X + (fx0 - vp.X) * t;
        double xLeftBottom = vp.X + (fx0 - vp.X) * (s.H - vp.Y) / (farGround - vp.Y), yRightEdge = vp.Y + (farGround - vp.Y) * (s.W - vp.X) / (fx1 - vp.X);

        var far = new DrawingGroup();
        using (var dc = far.Open())
        {
            dc.DrawRectangle(Solid(C(0x0A080B)), null, s.All);
            dc.DrawRectangle(Linear(new(0, 0), new(0, farTop), (0, C(0x100D18)), (1, C(0x2C2240))), null, new Rect(fx0 - s.S(6), 0, fx1 - fx0 + s.S(12), farTop + 2));
            dc.DrawRectangle(Linear(new(0, farTop), new(0, farGround), (0, C(0x1E1828)), (1, C(0x161220))), null, new Rect(fx0 - s.S(6), farTop, fx1 - fx0 + s.S(12), farGround - farTop));
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                {
                    if (rnd.NextDouble() < .45) continue;
                    var c = rnd.NextDouble() < .7 ? C(0x7E6A9E) : C(0xA45A74);
                    dc.DrawRectangle(Solid(A(c, .25 + .4 * rnd.NextDouble())), null, new Rect(fx0 + s.S(6 + col * 16), farTop + s.S(14 + row * 34), s.S(7), s.S(11)));
                }
            Light(dc, s.P(944, 360), s.S(120), s.S(110), C(0x9A86C4), .5);
            dc.DrawEllipse(Solid(A(C(0xE4DAF0), .85)), null, s.P(944, 360), s.S(2.6), s.S(2.6));
        }
        var walls = new DrawingGroup();
        using (var dc = walls.Open())
        {
            // wet ground
            var ground = Poly(new(fx0, farGround), new(fx1, farGround), new(s.W + 10, yRightEdge), new(s.W + 10, s.H + 10), new(xLeftBottom, s.H + 10));
            dc.DrawGeometry(Linear(new(0, farGround), new(0, s.H), (0, C(0x1C1626)), (.25, C(0x120E16)), (1, C(0x09070A))), null, ground);
            dc.PushClip(ground);
            dc.DrawGeometry(Linear(new(0, farGround), new(0, s.H), (0, A(C(0x6A5890), .5)), (.5, A(C(0x3A2C52), .18)), (1, A(C(0x3A2C52), 0))), null, Poly(new(vp.X - s.S(30), farGround), new(vp.X + s.S(30), farGround), new(vp.X + s.S(170), s.H), new(vp.X - s.S(150), s.H)));
            for (int i = 0; i < 14; i++) // puddles catching the light
            {
                double y = farGround + (s.H - farGround) * Math.Pow(rnd.NextDouble(), 1.6), t = (y - vp.Y) / (farGround - vp.Y);
                double x = vp.X + (rnd.NextDouble() - .45) * s.S(60) * t;
                dc.DrawEllipse(Glow(new(x, y), s.S(9) * t, s.S(1.1) * t, C(0x8E7AB4), .2), null, new(x, y), s.S(9) * t, s.S(1.1) * t);
            }
            dc.Pop();
            // left wall: dark, faint brick courses converging on the vanishing point (FRAME's controls sit over it)
            var left = Poly(new(-10, -10), new(fx0, -10), new(fx0, farGround), new(xLeftBottom, s.H + 10), new(-10, s.H + 10));
            dc.DrawGeometry(Linear(new(0, 0), new(fx0, 0), (0, C(0x09070A)), (.75, C(0x110D14)), (1, C(0x1E1828))), null, left);
            dc.PushClip(left);
            var course = F(new Pen(Solid(A(C(0x2A2230), .5)), s.S(.8)));
            for (double y = farTop - s.S(160); y < farGround; y += s.S(4.5))
            {
                double x = -10, yy = vp.Y + (y - vp.Y) * (x - vp.X) / (fx0 - vp.X);
                dc.DrawLine(course, new(fx0, y), new(x, yy));
            }
            dc.DrawRectangle(Solid(A(C(0x8E7AB4), .35)), null, new Rect(LeftX(1.5) - s.S(10), RightY(1.5, 4.2), s.S(8), s.S(14)));
            dc.Pop();
            // right wall with a door, pipes and a fire escape
            var right = Poly(new(s.W + 10, -10), new(fx1, -10), new(fx1, farGround), new(s.W + 10, yRightEdge));
            dc.DrawGeometry(Linear(new(fx1, 0), new(s.W, 0), (0, C(0x221A2C)), (.3, C(0x141018)), (1, C(0x0A080C))), null, right);
            dc.PushClip(right);
            var rcourse = F(new Pen(Solid(A(C(0x2E2434), .3)), s.S(.7)));
            for (double y = farTop - s.S(200); y < farGround; y += s.S(9))
                dc.DrawLine(rcourse, new(fx1, y), new(s.W + 10, vp.Y + (y - vp.Y) * (s.W + 10 - vp.X) / (fx1 - vp.X)));
            dc.DrawGeometry(Solid(C(0x0C090E)), F(new Pen(Solid(A(C(0x4A3A5A), .5)), s.S(1))), Poly(new(RightX(6.2), RightY(6.2, 0)), new(RightX(6.2), RightY(6.2, 2.2)), new(RightX(7.6), RightY(7.6, 2.2)), new(RightX(7.6), RightY(7.6, 0))));
            Light(dc, new(RightX(6.9), RightY(6.9, 2.55)), s.S(46), s.S(40), C(0x8A78B0), .3); dc.DrawRectangle(Solid(A(C(0xD8CCEA), .7)), null, new Rect(RightX(6.9) - s.S(5), RightY(6.9, 2.55) - s.S(1.5), s.S(10), s.S(3)));
            var pipe = F(new Pen(Solid(C(0x07060A)), s.S(4)));
            dc.DrawLine(pipe, new(RightX(4.2), -10), new(RightX(4.2), RightY(4.2, 0)));
            dc.Pop();
            var rail = F(new Pen(Solid(C(0x060508)), s.S(1.6)));
            foreach (double h in new[] { 5.4, 8.6 })
            {
                double t0 = 2.4, t1 = 4.4; var a0 = new Point(RightX(t0), RightY(t0, h)); var a1 = new Point(RightX(t1), RightY(t1, h));
                var b0 = new Point(a0.X - 1.1 * M * t0, a0.Y); var b1 = new Point(a1.X - 1.1 * M * t1, a1.Y);
                dc.DrawGeometry(Solid(A(C(0x050407), .9)), null, Poly(a0, a1, b1, b0));
                for (int i = 0; i <= 6; i++) { double t = t0 + (t1 - t0) * i / 6; var p = new Point(RightX(t) - 1.1 * M * t, RightY(t, h)); dc.DrawLine(rail, p, new(p.X, p.Y - .95 * M * t)); }
                dc.DrawLine(rail, new(b0.X, b0.Y - .95 * M * t0), new(b1.X, b1.Y - .95 * M * t1));
            }
            dc.DrawLine(rail, new(RightX(2.6) - .9 * M * 2.6, RightY(2.6, 5.4)), new(RightX(4.2) - .3 * M * 4.2, RightY(4.2, 8.6)));
            // overhead wires
            var wire = F(new Pen(Solid(A(C(0x050407), .95)), s.S(1.3)));
            foreach (var (x0, y0, x1, y1, sag) in new[] { (880.0, 70.0, 1180.0, 30.0, 70.0), (892.0, 150.0, 1290.0, 96.0, 60.0), (870.0, 30.0, 1100.0, 120.0, 40.0) })
            {
                var g = new StreamGeometry(); using (var c = g.Open()) { c.BeginFigure(s.P(x0, y0), false, false); c.QuadraticBezierTo(s.P((x0 + x1) / 2, (y0 + y1) / 2 + sag), s.P(x1, y1), true, true); }
                dc.DrawGeometry(null, wire, F(g));
            }
        }
        var neon = new DrawingGroup();
        using (var dc = neon.Open())
        {
            // crimson blade sign: a neon heart with a crack, its glow on the walls and its reflection on the wet ground
            double t = 5, sx1 = RightX(t), sx0 = sx1 - .78 * M * t, top = RightY(t, 5.2), bottom = RightY(t, 3.4);
            Light(dc, new((sx0 + sx1) / 2, (top + bottom) / 2), s.S(230), s.S(210), C(0xA0243E), .3);
            double y0 = RightY(t, 0) + s.S(4), cx = (sx0 + sx1) / 2;
            for (double y = y0; y < s.H; y += s.S(4 + 5 * rnd.NextDouble()))
            {
                double f = Math.Pow(1 - (y - y0) / (s.H - y0), 1.4), half = (sx1 - sx0) * (.25 + .35 * rnd.NextDouble()), x = cx + s.S(14) * (rnd.NextDouble() - .5) + (y - y0) * .08;
                Light(dc, new(x, y), half, s.S(1.6 + 1.6 * rnd.NextDouble()), C(0xD03656), .55 * f);
            }
            Light(dc, new(cx, y0 + s.S(40)), s.S(60), s.S(60), C(0x8A1E36), .16);
            dc.DrawLine(F(new Pen(Solid(C(0x07060A)), s.S(2))), new(sx0 + s.S(8), top - s.S(10)), new(sx1, top - s.S(22)));
            dc.DrawRectangle(Solid(C(0x0F0A10)), F(new Pen(Solid(A(C(0x5A2034), .8)), s.S(1.2))), new Rect(sx0, top, sx1 - sx0 - s.S(6), bottom - top));
            var heart = Shape("M 0,8 C -2,2 -14,0 -14,10 C -14,20 -4,26 0,32 C 4,26 14,20 14,10 C 14,0 2,2 0,8 Z", (sx0 + sx1 - s.S(6)) / 2, top + (bottom - top) * .22, (sx1 - sx0) / 44);
            var crack = Shape("M 0,8 L -3,15 L 2,19 L -2,26", (sx0 + sx1 - s.S(6)) / 2, top + (bottom - top) * .22, (sx1 - sx0) / 44);
            Neon(dc, heart, C(0xFF3D63), s.K); Neon(dc, crack, C(0xFF3D63), s.K * .7);
            var bar = Shape("M -12,0 L 12,0", (sx0 + sx1 - s.S(6)) / 2, top + (bottom - top) * .82, (sx1 - sx0) / 44);
            Neon(dc, bar, C(0xB46CFF), s.K * .7);
            // small purple sign further down the alley
            double u = 2.3, ux1 = RightX(u), ux0 = ux1 - .55 * M * u;
            Light(dc, new((ux0 + ux1) / 2, RightY(u, 3.2)), s.S(80), s.S(70), C(0x8A5ACC), .35);
            dc.DrawRectangle(Solid(C(0x0E0A12)), null, new Rect(ux0, RightY(u, 3.8), ux1 - ux0, RightY(u, 2.6) - RightY(u, 3.8)));
            Neon(dc, F(new RectangleGeometry(new Rect(ux0 + s.S(2.5), RightY(u, 3.8) + s.S(2.5), ux1 - ux0 - s.S(5), RightY(u, 2.6) - RightY(u, 3.8) - s.S(5)))), C(0xA070FF), s.K * .55);
            for (double y = RightY(u, 0) + s.S(3); y < RightY(u, 0) + s.S(110); y += s.S(4 + 4 * rnd.NextDouble()))
                Light(dc, new((ux0 + ux1) / 2 + s.S(6) * (rnd.NextDouble() - .5), y), (ux1 - ux0) * (.4 + .3 * rnd.NextDouble()), s.S(1.3), C(0xA070FF), .4 * (1 - (y - RightY(u, 0)) / s.S(110)));
        }
        var figure = new DrawingGroup();
        using (var dc = figure.Open())
        {
            double scale = s.S(.93), fx = s.Free(s.X(1006), 30), fy = s.Y(596) - 200 * scale;
            var g = Shape(Figure, fx - 32 * scale, fy, scale);
            // reflection on the wet street
            dc.PushOpacityMask(Linear(new(0, fy + 200 * scale), new(0, fy + 290 * scale), (0, A(Colors.Black, .45)), (1, A(Colors.Black, 0))));
            dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 2 * (fy + 200 * scale)));
            dc.DrawGeometry(Solid(C(0x060508)), null, g);
            dc.Pop(); dc.Pop();
            LitFigure(dc, g, s.P(944, 360), s.S(330), C(0xA690D6), 1, s.K);
        }
        return [new(F(far), 1.2), new(F(walls), 0), new(F(neon), 0), new(F(figure), 0)];
    }
    // Neon tube: wide faint glow, tighter glow, coloured tube and a pale hot core.
    static void Neon(DrawingContext dc, Geometry g, Color c, double k)
    {
        dc.DrawGeometry(null, Stroke(Solid(A(c, .07)), 16 * k), g);
        dc.DrawGeometry(null, Stroke(Solid(A(c, .16)), 7 * k), g);
        dc.DrawGeometry(null, Stroke(Solid(A(c, .9)), 2.6 * k), g);
        dc.DrawGeometry(null, Stroke(Solid(A(Color.FromRgb((byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2)), .9)), 1 * k), g);
    }

    // ---- Moonlit graveyard ----
    static Layer[] Graveyard(Stage s)
    {
        var rnd = new Random(23);
        var moonAt = s.P(990, 188);
        var sky = new DrawingGroup();
        using (var dc = sky.Open())
        {
            dc.DrawRectangle(Linear(new(0, 0), new(0, s.H), (0, C(0x08070C)), (.45, C(0x110F1C)), (.7, C(0x1C1726)), (1, C(0x0B090E))), null, s.All);
            Light(dc, moonAt, s.S(430), s.S(400), C(0x342C4C), .7);
            Light(dc, moonAt, s.S(160), s.S(160), C(0x9088AE), .22);
            for (int i = 0; i < 46; i++) { var p = s.P(640 + rnd.Next(640), 40 + rnd.Next(380)); if ((p - moonAt).Length < s.S(150)) continue; dc.DrawEllipse(Solid(A(C(0xD8D2E8), .12 + .4 * rnd.NextDouble() * rnd.NextDouble())), null, p, s.S(.8), s.S(.8)); }
            var disk = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = moonAt + new Vector(-s.S(14), -s.S(12)), GradientOrigin = moonAt + new Vector(-s.S(14), -s.S(12)), RadiusX = s.S(74), RadiusY = s.S(74) };
            disk.GradientStops.Add(new GradientStop(C(0xEDE6F0), 0)); disk.GradientStops.Add(new GradientStop(C(0xD6CDDF), .6)); disk.GradientStops.Add(new GradientStop(C(0xB2A6C2), 1));
            dc.DrawEllipse(F(disk), null, moonAt, s.S(52), s.S(52));
            dc.PushClip(F(new EllipseGeometry(moonAt, s.S(52), s.S(52))));
            foreach (var (x, y, r) in new[] { (-20.0, -14.0, 13.0), (14.0, 8.0, 18.0), (-8.0, 26.0, 9.0), (26.0, -24.0, 8.0), (-30.0, 14.0, 7.0) })
                Light(dc, moonAt + new Vector(s.S(x), s.S(y)), s.S(r), s.S(r), C(0x9A8EAE), .55);
            dc.Pop();
            foreach (var (x, y, rx, ry, a) in new[] { (930.0, 232.0, 210.0, 9.0, .85), (1050.0, 214.0, 160.0, 6.0, .7), (820.0, 300.0, 260.0, 10.0, .6), (1160.0, 120.0, 200.0, 8.0, .5) })
                Light(dc, s.P(x, y), s.S(rx), s.S(ry), C(0x0A080F), a);
        }
        var far = new DrawingGroup();
        using (var dc = far.Open())
        {
            var hill = new StreamGeometry();
            using (var c = hill.Open())
            {
                c.BeginFigure(new(-10, s.H + 10), true, true); c.LineTo(new(-10, s.Y(574)), false, false);
                c.BezierTo(s.P(520, 560), s.P(760, 520), s.P(940, 536), true, true); c.BezierTo(s.P(1080, 548), s.P(1200, 560), new(s.W + 10, s.Y(552)), true, true);
                c.LineTo(new(s.W + 10, s.H + 10), false, false);
            }
            dc.DrawGeometry(Solid(C(0x17131E)), null, F(hill));
            var dim = Solid(C(0x120F17));
            foreach (var (x, h, kind) in new[] { (760.0, 16.0, 0), (800.0, 22.0, 1), (846.0, 14.0, 0), (1110.0, 18.0, 0), (1150.0, 24.0, 1), (1196.0, 15.0, 0), (1240.0, 19.0, 0) })
                Stone(dc, s, s.P(x, 548 - (x > 1000 ? 4 : 10)), s.S(h * .55), s.S(h), kind, 0, dim, null);
            // a small chapel on the far hill
            dc.DrawGeometry(dim, null, Shape("M 0,0 L 0,-22 L 10,-34 L 20,-22 L 20,0 Z M 6,-34 L 10,-58 L 14,-34 Z", s.X(1004), s.Y(540), s.K));
            dc.DrawRectangle(Solid(A(C(0xB08E9E), .4)), null, new Rect(s.X(1012), s.Y(526), s.S(3), s.S(5)));
        }
        var near = new DrawingGroup();
        using (var dc = near.Open())
        {
            var ground = new StreamGeometry();
            using (var c = ground.Open())
            {
                c.BeginFigure(new(-10, s.H + 10), true, true); c.LineTo(new(-10, s.Y(640)), false, false);
                c.BezierTo(s.P(600, 630), s.P(860, 600), s.P(1060, 612), true, true); c.BezierTo(s.P(1160, 618), s.P(1240, 632), new(s.W + 10, s.Y(626)), true, true);
                c.LineTo(new(s.W + 10, s.H + 10), false, false);
            }
            dc.DrawGeometry(Linear(new(0, s.Y(600)), new(0, s.H), (0, C(0x100D14)), (1, C(0x08070A))), null, F(ground));
            Light(dc, s.P(1000, 610), s.S(560), s.S(36), C(0x7E7698), .13);
            Tree(dc, s, rnd);
            var fill = Solid(C(0x0C0A10)); var moonlit = Solid(A(C(0x8A82A4), .55));
            var away = moonAt - s.P(1100, 600); away.Normalize();
            foreach (var (x, y, w, h, kind, tilt) in new[] { (872.0, 632.0, 30.0, 48.0, 0, -4.0), (932.0, 640.0, 26.0, 58.0, 1, 3.0), (1128.0, 628.0, 34.0, 44.0, 0, 6.0), (1206.0, 642.0, 22.0, 62.0, 2, 0.0), (1262.0, 650.0, 36.0, 52.0, 0, -8.0) })
                Stone(dc, s, s.P(x, y), s.S(w), s.S(h), kind, tilt, fill, (moonlit, away * s.S(1.4)));
            var grass = F(new Pen(Solid(C(0x0A080C)), s.S(1.1)));
            for (int i = 0; i < 160; i++)
            {
                double x = 760 + rnd.Next(540), y = 614 + 34 * rnd.NextDouble() + (x > 1150 ? 10 : 0);
                var p = s.P(x, y); dc.DrawLine(grass, p, p + new Vector(s.S(rnd.Next(-5, 5)), -s.S(5 + rnd.Next(9))));
            }
            double scale = s.S(.86), fx = s.Free(s.X(1012), 30), fy = s.Y(632) - 200 * scale;
            LitFigure(dc, Shape(Figure, fx - 32 * scale, fy, scale), moonAt, s.S(560), C(0xB4ACD0), .9, s.K);
        }
        return [new(F(sky), 0), new(F(far), 1.4), new(F(near), 0)];
    }
    // Headstone: kind 0 rounded, 1 cross, 2 obelisk. lit: rim copy offset toward the moon (drawn first, then the stone).
    static void Stone(DrawingContext dc, Stage s, Point foot, double w, double h, int kind, double tilt, Brush fill, (Brush Brush, Vector Offset)? lit)
    {
        string data = kind switch
        {
            1 => "M -0.12,0 L -0.12,-0.62 L -0.5,-0.62 L -0.5,-0.78 L -0.12,-0.78 L -0.12,-1 L 0.12,-1 L 0.12,-0.78 L 0.5,-0.78 L 0.5,-0.62 L 0.12,-0.62 L 0.12,0 Z",
            2 => "M -0.38,0 L -0.3,-0.82 L 0,-1 L 0.3,-0.82 L 0.38,0 Z",
            _ => "M -0.5,0 L -0.5,-0.68 C -0.5,-1.08 0.5,-1.08 0.5,-0.68 L 0.5,0 Z",
        };
        var g = Geometry.Parse(data).Clone();
        var m = Matrix.Identity; m.Scale(w, h); m.Rotate(tilt); m.Translate(foot.X, foot.Y); g.Transform = new MatrixTransform(m); F(g);
        if (lit is { } l) { dc.PushTransform(new TranslateTransform(l.Offset.X, l.Offset.Y)); dc.DrawGeometry(l.Brush, null, g); dc.Pop(); }
        dc.DrawGeometry(fill, null, g);
    }
    // Bare tree: recursive tapered branches in one geometry (seamless), each branch three gently bent parts; twigs at the ends.
    static void Tree(DrawingContext dc, Stage s, Random rnd)
    {
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = g.Open()) Branch(c, s.P(1214, 652), -Math.PI / 2 - .16, s.S(150), s.S(26), 7, rnd);
        dc.DrawGeometry(Solid(C(0x07060A)), null, F(g));
    }
    static void Branch(StreamGeometryContext c, Point p, double angle, double length, double width, int depth, Random rnd)
    {
        double a = angle, w = width; var at = p;
        for (int part = 0; part < 3; part++)
        {
            a += (rnd.NextDouble() - .5) * .34;
            var next = at + new Vector(Math.Cos(a), Math.Sin(a)) * length / 3; double nw = w * .88;
            Taper(c, at, next, w, nw); Knot(c, next, nw / 2); at = next; w = nw;
        }
        if (depth == 0 || w < .5)
        {
            for (int i = 0; i < 2; i++) { double t = a + (i == 0 ? -.5 : .45) + (rnd.NextDouble() - .5) * .3; Taper(c, at, at + new Vector(Math.Cos(t), Math.Sin(t)) * length * .35, Math.Max(.4, w * .7), .2); }
            return;
        }
        int count = depth >= 6 ? 2 : rnd.Next(2, 4);
        for (int i = 0; i < count; i++)
        {
            double spread = (i - (count - 1) / 2d) * (.5 + .25 * rnd.NextDouble()) + (rnd.NextDouble() - .5) * .3 - .07; // leans toward the open sky (left)
            Branch(c, at, a + spread, length * (.64 + .18 * rnd.NextDouble()), w * (.6 + .16 * rnd.NextDouble()), depth - 1, rnd);
        }
    }
    static void Taper(StreamGeometryContext c, Point a, Point b, double wa, double wb)
    {
        var d = b - a; if (d.Length < 1e-6) return; d.Normalize(); var n = new Vector(-d.Y, d.X);
        c.BeginFigure(a + n * wa / 2, true, true); c.LineTo(b + n * wb / 2, false, false); c.LineTo(b - n * wb / 2, false, false); c.LineTo(a - n * wa / 2, false, false);
    }
    static void Knot(StreamGeometryContext c, Point p, double r)
    {
        c.BeginFigure(p + new Vector(r, 0), true, true);
        // Counterclockwise like the branch quads (Taper), so the Nonzero fill never cancels into rings at the joints.
        c.ArcTo(p - new Vector(r, 0), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
        c.ArcTo(p + new Vector(r, 0), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
    }
}
