using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

// Geometry and generated light of the obsidian stone artwork (ObsidianOff = assets/2.png, ObsidianOn = assets/1.png),
// measured once from the decoded bitmaps. The stone is not exactly centred in its image: Centre = centre of the dark
// stone's alpha bounds (alpha > 40), Radius = the farthest visible pixel of either image from that centre, i.e. the
// circle the rotating stone sweeps. SweptLeft adds the pulse swell and the hover scale (about the box centre), so
// layout next to the stone (PlaceShadowGroup) can keep its distance from every rotation angle and state.
static class StoneArt
{
    public const double HoverScale = 1.025, SwellMax = 1.007;
    static bool measured;
    static Point centre = new(.5, .5);
    static double radius = .5;
    static BitmapSource? ember;
    public static Point Centre { get { Measure(); return centre; } }
    public static double Radius { get { Measure(); return radius; } }
    public static double SweptLeft => .5 - (.5 - (Centre.X - Radius * SwellMax)) * HoverScale;
    static byte[] Pixels(string key, out int w, out int h)
    {
        var bgra = new FormatConvertedBitmap((BitmapSource)Application.Current.Resources[key], PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth; h = bgra.PixelHeight; var px = new byte[w * h * 4]; bgra.CopyPixels(px, w * 4, 0); return px;
    }
    static void Measure()
    {
        if (measured) return; measured = true;
        var off = Pixels("ObsidianOff", out int w, out int h);
        int left = w, right = -1, top = h, bottom = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (off[(y * w + x) * 4 + 3] > 40) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < 0) return;
        double cx = (left + right + 1) / 2d, cy = (top + bottom + 1) / 2d, r2 = 0;
        foreach (var (px, pw, ph) in new[] { (off, w, h), (Pixels("ObsidianOn", out int ow, out int oh), ow, oh) })
            for (int y = 0; y < ph; y++) for (int x = 0; x < pw; x++)
                if (px[(y * pw + x) * 4 + 3] > 40) { double dx = (x + .5) * w / pw - cx, dy = (y + .5) * h / ph - cy; r2 = Math.Max(r2, dx * dx + dy * dy); }
        centre = new Point(cx / w, cy / h); radius = Math.Sqrt(r2) / w;
    }

    // Molten light of the ON stone: its fissures and core (bright pixels) pushed toward a hotter gold, over a soft orange
    // bloom of the same mask (box blur x3 ~ Gaussian). Drawn over the ON image at the pulse opacity, so the cracks
    // brighten and the warm glow swells without changing the stone's texture, shape or size.
    public static BitmapSource Ember => ember ??= MakeEmber();
    static BitmapSource MakeEmber()
    {
        var px = Pixels("ObsidianOn", out int w, out int h);
        var e = new float[w * h];
        for (int i = 0; i < e.Length; i++)
        {
            double lum = (.0722 * px[i * 4] + .7152 * px[i * 4 + 1] + .2126 * px[i * 4 + 2]) / 255;
            e[i] = (float)(DesertTerrain.Smooth(.3, .8, lum) * px[i * 4 + 3] / 255);
        }
        var bloom = (float[])e.Clone(); var tmp = new float[e.Length]; int r = Math.Max(2, w / 48);
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < h; y++) { float s = 0; for (int k = -r; k <= r; k++) s += bloom[y * w + Math.Clamp(k, 0, w - 1)]; for (int x = 0; x < w; x++) { tmp[y * w + x] = s / (2 * r + 1); s += bloom[y * w + Math.Min(w - 1, x + r + 1)] - bloom[y * w + Math.Max(0, x - r)]; } }
            for (int x = 0; x < w; x++) { float s = 0; for (int k = -r; k <= r; k++) s += tmp[Math.Clamp(k, 0, h - 1) * w + x]; for (int y = 0; y < h; y++) { bloom[y * w + x] = s / (2 * r + 1); s += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x]; } }
        }
        var output = new byte[w * h * 4];
        for (int i = 0; i < e.Length; i++)
        {
            double aE = e[i] * .92, aB = Math.Min(1, bloom[i] * 1.6) * .7;
            double b0 = px[i * 4], g0 = px[i * 4 + 1], r0 = px[i * 4 + 2];
            double hb = b0 + (165 - b0) * .6, hg = g0 + (226 - g0) * .6, hr = r0 + (255 - r0) * .6; // hotter, toward pale gold
            double a = aE + aB * (1 - aE);
            output[i * 4] = (byte)Math.Clamp(hb * aE + 48 * aB * (1 - aE), 0, 255);
            output[i * 4 + 1] = (byte)Math.Clamp(hg * aE + 140 * aB * (1 - aE), 0, 255);
            output[i * 4 + 2] = (byte)Math.Clamp(hr * aE + 255 * aB * (1 - aE), 0, 255);
            output[i * 4 + 3] = (byte)Math.Clamp(255 * a, 0, 255);
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, output, w * 4); bitmap.Freeze(); return bitmap;
    }
}

// The obsidian Shadow stone (ObsidianAction template parts). Rotates the artwork only (inner "StoneArt": RotateTransform
// about the measured visual centre, one turn per 40 s, constant speed, both states); the template element "Stone" with its
// layout box, hover/press ScaleTransform, hit area and label are untouched. Tag="Active" (set only after the engine
// confirmed Shadow Recording) crossfades OFF -> ON (~0.75 s, out ~0.9 s) and starts a calm ~2.6 s pulse: the generated
// ember light of the fissures and the StoneBreath halo brighten and relax (asymmetric breath, slightly varying beats),
// with at most a 0.7 % swell. Everything advances only from the atmosphere clock; Present(still) shows the exact static
// state (reduced motion: lit stone with a steady glow) and is used whenever the clock is not running.
sealed class ObsidianStone(Button button, double haloLevel)
{
    const double TurnSeconds = 40, Beat = 2.6;
    FrameworkElement? art;
    RotateTransform? turn;
    ScaleTransform? swell;
    UIElement? dark, lit, ember, halo;
    double angle, level, beat;
    public int Updates { get; private set; }
    public double Angle => angle;
    public double Level => level;
    public double Glow => ember?.Opacity ?? 0;
    bool Active => button.Tag as string == "Active";
    public bool Shown => button.IsVisible; // 0.6.0: advanced only while its page/layout is on screen
    bool Attach()
    {
        if (art != null) return true;
        button.ApplyTemplate();
        if (button.Template?.FindName("StoneArt", button) is not FrameworkElement found) return false;
        dark = button.Template.FindName("Dark", button) as UIElement; lit = button.Template.FindName("Lit", button) as UIElement;
        ember = button.Template.FindName("Ember", button) as UIElement; halo = button.Template.FindName("StoneBreath", button) as UIElement;
        if (ember is Image image) image.Source = StoneArt.Ember;
        swell = new ScaleTransform(); turn = new RotateTransform();
        var group = new TransformGroup(); group.Children.Add(swell); group.Children.Add(turn);
        found.RenderTransformOrigin = StoneArt.Centre; found.RenderTransform = group; art = found;
        return true;
    }
    // Asymmetric breath: rises over 42 % of the beat, relaxes over 58 %; C1-continuous (cosine flat at the joins).
    static double Pulse(double t)
    {
        double s = t / Beat % 1, w = s < .42 ? s / .42 * .5 : .5 + (s - .42) / .58 * .5;
        return .5 - .5 * Math.Cos(2 * Math.PI * w);
    }
    public void Advance(double seconds)
    {
        if (!Attach()) return;
        Updates++;
        angle = (angle + seconds * 360 / TurnSeconds) % 360;
        double target = Active ? 1 : 0;
        level = Math.Clamp(level + Math.Clamp(target - level, -seconds / .9, seconds / .75), 0, 1);
        if (level > 0) beat += seconds; else beat = 0; // each activation starts at the relaxed point of the breath
        Apply(false);
    }
    public void Restart() { angle = 0; beat = 0; level = Active ? 1 : 0; }
    public void Present(bool still) { if (!Attach()) return; level = Active ? 1 : 0; if (level == 0) beat = 0; Apply(still); }
    void Apply(bool still)
    {
        double e = level * level * (3 - 2 * level), d = Math.Clamp(level * 2 - 1, 0, 1);
        double p = still ? 0 : Pulse(beat) * (.92 + .08 * Drift.Noise(beat * .37, 5.2));
        if (lit != null) lit.Opacity = e;
        if (dark != null) dark.Opacity = 1 - d * d * (3 - 2 * d);
        if (ember != null) ember.Opacity = e * (still ? .45 : .1 + .9 * p);
        if (halo != null) halo.Opacity = haloLevel * e * (still ? .3 : .14 + .36 * p);
        if (swell != null) swell.ScaleX = swell.ScaleY = 1 + (StoneArt.SwellMax - 1) * e * p;
        if (turn != null) turn.Angle = angle;
    }
}
