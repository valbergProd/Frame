using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Frame.App;

// Cinematic D stone art (0.6.0): the 0.5.5 obsidian artwork (assets/2.png OFF, assets/1.png ON) decoded large enough for a
// ~320 DIP stone at high scaling, plus light layers derived from it once (no new artwork):
// - EmberOn / EmberOff: the molten fissures pushed toward hot gold over a soft bloom of the same mask (StoneArt.Ember's
//   recipe at 900 px). ON: bright pixels (core and cracks). OFF: warm pixels only, so its silver ring and grey highlights
//   stay dark.
// - RimOn / RimOff: light hugging the silhouette: a narrow and a wide glow just outside the alpha edge plus a thin inner edge
//   band, weighted by how close the fissures run (heat), so the rim is brightest where cracks reach the outline. They rotate
//   with the art, so the glow always follows the visible facets.
// - Silhouette: union alpha of both images (the screen-fixed bounce light from the ground is masked by it).
// - Facing tables: how much fissure light points to the ground (and to the lower left/right) at each rotation angle, from the
//   angular distribution of the outer fissures; the ground light follows it as bright cracks turn toward the ground.
// All geometry uses StoneArt.Centre/Radius (relative, resolution independent).
static class CinematicArt
{
    public const int Size = 900, Half = Size / 2;
    const int Bins = 72;
    static bool built;
    static BitmapSource? off, on, emberOn, emberOff, rimOn, rimOff, silhouette, softOn, softOff;
    static double[] facingOn = [], facingOff = [], lateralOn = [], lateralOff = [];
    public static double Milliseconds { get; private set; } // one-time cost (diagnostics)
    public static BitmapSource Off { get { Ensure(); return off!; } }
    public static BitmapSource On { get { Ensure(); return on!; } }
    public static BitmapSource EmberOn { get { Ensure(); return emberOn!; } }
    public static BitmapSource EmberOff { get { Ensure(); return emberOff!; } }
    public static BitmapSource RimOn { get { Ensure(); return rimOn!; } }
    public static BitmapSource RimOff { get { Ensure(); return rimOff!; } }
    public static BitmapSource Silhouette { get { Ensure(); return silhouette!; } }
    public static BitmapSource SoftOn { get { Ensure(); return softOn!; } }   // the ember at 1/8 size: soft light for the reflection
    public static BitmapSource SoftOff { get { Ensure(); return softOff!; } }

    static BitmapImage Load(string name)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.UriSource = new Uri("pack://application:,,,/Assets/" + name); image.DecodePixelWidth = Size; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        return image;
    }
    static byte[] Pixels(BitmapSource source, out int w, out int h)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth; h = bgra.PixelHeight; var px = new byte[w * h * 4]; bgra.CopyPixels(px, w * 4, 0); return px;
    }
    public static void Ensure()
    {
        if (built) return; built = true;
        var clock = Stopwatch.StartNew();
        var onImage = Load("obsidian-on.png"); var offImage = Load("obsidian-off.png"); on = onImage; off = offImage;
        var pOn = Pixels(onImage, out int w, out int h); var pOff = Pixels(offImage, out int w2, out int h2);
        var fOn = Fissures(pOn, warm: false); var fOff = Fissures(pOff, warm: true);
        emberOn = Ember(pOn, fOn, w, h); emberOff = Ember(pOff, fOff, w2, h2);
        softOn = new TransformedBitmap(emberOn, new ScaleTransform(.125, .125)); softOn.Freeze(); softOff = new TransformedBitmap(emberOff, new ScaleTransform(.125, .125)); softOff.Freeze();
        var aOn = Down(Alpha(pOn), w, h); var aOff = Down(Alpha(pOff), w2, h2); var hOn = Down(fOn, w, h); var hOff = Down(fOff, w2, h2);
        int n = w / 2;
        rimOn = Rim(aOn, hOn, n); rimOff = Rim(aOff, hOff, n);
        var union = new byte[n * n * 4];
        for (int i = 0; i < n * n; i++) union[i * 4] = union[i * 4 + 1] = union[i * 4 + 2] = union[i * 4 + 3] = (byte)(255 * Math.Max(aOn[i], aOff[i]));
        silhouette = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, union, n * 4); silhouette.Freeze();
        (facingOn, lateralOn) = Facing(hOn, n); (facingOff, lateralOff) = Facing(hOff, n);
        Milliseconds = clock.Elapsed.TotalMilliseconds;
    }
    static float[] Alpha(byte[] px) { var a = new float[px.Length / 4]; for (int i = 0; i < a.Length; i++) a[i] = px[i * 4 + 3] / 255f; return a; }
    // Fissure mask 0..1. ON: luminance (core and cracks glow). OFF: warm and bright (orange cracks; the silver ring is neutral).
    static float[] Fissures(byte[] px, bool warm)
    {
        var e = new float[px.Length / 4];
        for (int i = 0; i < e.Length; i++)
        {
            double b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2], lum = (.0722 * b + .7152 * g + .2126 * r) / 255;
            double v = warm ? DesertTerrain.Smooth(.18, .6, lum) * Math.Clamp((r - b) / 255 * 2.2 - .35, 0, 1) : DesertTerrain.Smooth(.3, .8, lum);
            e[i] = (float)(v * px[i * 4 + 3] / 255);
        }
        return e;
    }
    // 2 x 2 average (analysis maps at half resolution).
    static float[] Down(float[] a, int w, int h)
    {
        int n = w / 2, m = h / 2; var d = new float[n * m];
        for (int y = 0; y < m; y++) for (int x = 0; x < n; x++) { int i = 2 * y * w + 2 * x; d[y * n + x] = (a[i] + a[i + 1] + a[i + w] + a[i + w + 1]) * .25f; }
        return d;
    }
    // Box blur (sliding sums, clamped edges); three passes approximate a Gaussian.
    internal static float[] Blur(float[] source, int w, int h, int r, int passes)
    {
        var a = (float[])source.Clone(); var t = new float[a.Length]; float n = 2 * r + 1;
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w; float s = 0;
                for (int k = -r; k <= r; k++) s += a[row + Math.Clamp(k, 0, w - 1)];
                for (int x = 0; x < w; x++) { t[row + x] = s / n; s += a[row + Math.Min(w - 1, x + r + 1)] - a[row + Math.Max(0, x - r)]; }
            }
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int k = -r; k <= r; k++) s += t[Math.Clamp(k, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++) { a[y * w + x] = s / n; s += t[Math.Min(h - 1, y + r + 1) * w + x] - t[Math.Max(0, y - r) * w + x]; }
            }
        }
        return a;
    }
    // StoneArt.Ember's recipe: the fissures toward a hotter pale gold over an orange bloom of the same mask.
    static BitmapSource Ember(byte[] px, float[] e, int w, int h)
    {
        var bloom = Blur(e, w, h, Math.Max(2, w / 48), 3); var output = new byte[w * h * 4];
        for (int i = 0; i < e.Length; i++)
        {
            double aE = e[i] * .92, aB = Math.Min(1, bloom[i] * 1.6) * .7;
            double b0 = px[i * 4], g0 = px[i * 4 + 1], r0 = px[i * 4 + 2];
            double hb = b0 + (165 - b0) * .6, hg = g0 + (226 - g0) * .6, hr = r0 + (255 - r0) * .6, a = aE + aB * (1 - aE);
            output[i * 4] = (byte)Math.Clamp(hb * aE + 48 * aB * (1 - aE), 0, 255);
            output[i * 4 + 1] = (byte)Math.Clamp(hg * aE + 140 * aB * (1 - aE), 0, 255);
            output[i * 4 + 2] = (byte)Math.Clamp(hr * aE + 255 * aB * (1 - aE), 0, 255);
            output[i * 4 + 3] = (byte)Math.Clamp(255 * a, 0, 255);
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, output, w * 4); bitmap.Freeze(); return bitmap;
    }
    // Silhouette light: narrow (~6 DIP) and wide (~30 DIP) glow outside the alpha edge and a thin inner edge band, all scaled
    // by the nearby fissure heat (floor 30 %: the whole outline carries some light).
    static BitmapSource Rim(float[] a, float[] f, int n)
    {
        var near = Blur(a, n, n, 4, 2); var wide = Blur(a, n, n, 14, 3); var heat = Blur(f, n, n, 22, 3);
        float peak = 1e-6f; for (int i = 0; i < a.Length; i++) if (a[i] > .5f) peak = Math.Max(peak, heat[i]);
        var px = new byte[n * n * 4];
        for (int i = 0; i < a.Length; i++)
        {
            double em = .3 + .7 * Math.Min(1, heat[i] / (.55 * peak));
            double outer = Math.Max(0, near[i] - a[i]) * 1.7 + Math.Max(0, wide[i] - a[i]) * .5, inner = a[i] * Math.Clamp((1 - near[i]) * 2.2, 0, 1);
            double aO = Math.Min(1, outer * em), aI = Math.Min(1, inner * em * .5), al = aI + aO * (1 - aI);
            px[i * 4] = (byte)Math.Clamp(84 * aI + 52 * aO * (1 - aI), 0, 255);
            px[i * 4 + 1] = (byte)Math.Clamp(170 * aI + 132 * aO * (1 - aI), 0, 255);
            px[i * 4 + 2] = (byte)Math.Clamp(255 * al, 0, 255);
            px[i * 4 + 3] = (byte)Math.Clamp(255 * al, 0, 255);
        }
        var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4); bitmap.Freeze(); return bitmap;
    }
    // Outer fissure light per direction (72 bins, angle in screen space, y down), then for every rotation angle (1 degree) the
    // share facing down (cos² lobe) and the balance lower right vs lower left. Facing is normalised to mean 1 and limited.
    static (double[] Facing, double[] Lateral) Facing(float[] f, int n)
    {
        double cx = StoneArt.Centre.X * n, cy = StoneArt.Centre.Y * n, radius = StoneArt.Radius * n;
        var bins = new double[Bins];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = f[y * n + x]; if (v < .02f) continue;
                double dx = x + .5 - cx, dy = y + .5 - cy, r = Math.Sqrt(dx * dx + dy * dy);
                if (r < .5 * radius || r > 1.05 * radius) continue;
                bins[(int)((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360 / (360d / Bins)) % Bins] += v * r / radius;
            }
        static double Lobe(double d) { d = ((d % 360) + 540) % 360 - 180; return Math.Abs(d) >= 90 ? 0 : Math.Pow(Math.Cos(d * Math.PI / 180), 2); }
        var facing = new double[360]; var lateral = new double[360];
        for (int angle = 0; angle < 360; angle++)
        {
            double down = 0, right = 0, left = 0;
            for (int b = 0; b < Bins; b++) { double th = (b + .5) * 360 / Bins + angle; down += bins[b] * Lobe(th - 90); right += bins[b] * Lobe(th - 45); left += bins[b] * Lobe(th - 135); }
            facing[angle] = down; lateral[angle] = (right - left) / (right + left + 1e-9);
        }
        double mean = Math.Max(1e-9, facing.Average());
        for (int angle = 0; angle < 360; angle++) facing[angle] = Math.Clamp(1 + .8 * (facing[angle] / mean - 1), .55, 1.45);
        return (facing, lateral);
    }
    public static (double Facing, double Lateral) FacingAt(double angle, bool lit)
    {
        Ensure();
        var f = lit ? facingOn : facingOff; var l = lit ? lateralOn : lateralOff;
        double a = (angle % 360 + 360) % 360; int i = (int)a % 360, j = (i + 1) % 360; double t = a - Math.Floor(a);
        return (f[i] + (f[j] - f[i]) * t, l[i] + (l[j] - l[i]) * t);
    }
}

// The one light state of Cinematic D: every glow (fissures, rim, halo, spill, ground pool, reflection, bounce on the stone,
// warm text and slider highlights) is derived from these few numbers, so all layers move together.
//   Angle     stone rotation, one turn per 40 s (constant speed, both states)
//   Lit       eased activation: OFF -> ON in ~0.75 s once the engine confirmed Shadow Recording, back in ~0.9 s
//   Breath    the calm ~2.6 s pulse while active (asymmetric: rises over 42 %, relaxes over 58 %; beats vary slightly), 0 idle
//   Facing    fissure light pointing at the ground at this angle (CinematicArt tables, ~0.55..1.45, mean 1), Lateral its
//             lower-right vs lower-left balance: the ground light swells and shifts as bright cracks turn downward
//   Emission  overall output: a faint ember residual idle, ~0.7..1 while active (breath), with a slight molten shimmer
// Advanced only by CaptureAtmosphere's clock (through the layout's IDecorativeClient); Snap shows the exact still state.
sealed class CinematicLight
{
    public const double TurnSeconds = 40, Beat = 2.6, Idle = .2;
    double angle, activation, beat, time;
    public bool Active { get; set; }
    public double Angle => angle;
    public double Lit { get; private set; }
    public double Breath { get; private set; }
    public double Facing { get; private set; } = 1;
    public double Lateral { get; private set; }
    public double Emission { get; private set; } = Idle;
    public int Updates { get; private set; }
    // Layer levels (0..1, before each layer's own maximum).
    public double Fissure { get; private set; }            // ember of the ON fissures (pulses)
    public double Residual => .34 * (1 - Lit);              // OFF cracks: steady ember, no pulse
    public double Rim => .32 + .68 * Emission;              // silhouette light
    public double Halo => Emission;                         // halo and spill around the stone
    public double Pool => Emission * Facing;                // warm light on the ground (and the faint reflection)
    public double Bounce => .5 * Emission * Facing;         // ground light returned onto the stone's lower half
    public double Warmth => Emission;                       // text and slider highlights
    // Asymmetric breath (as the 0.5.5 stone): C1-continuous at the joins.
    static double Pulse(double t)
    {
        double s = t / Beat % 1, w = s < .42 ? s / .42 * .5 : .5 + (s - .42) / .58 * .5;
        return .5 - .5 * Math.Cos(2 * Math.PI * w);
    }
    public void Advance(double dt)
    {
        Updates++; time += dt;
        angle = (angle + dt * 360 / TurnSeconds) % 360;
        activation = Math.Clamp(activation + Math.Clamp((Active ? 1 : 0) - activation, -dt / .9, dt / .75), 0, 1);
        if (activation > 0) beat += dt; else beat = 0; // every activation starts at the relaxed point of the breath
        Compute(false);
    }
    // Clock stopped (hidden, tray, reduced motion): the exact state for Active. still = no motion wanted at all: a steady
    // mid-breath light; otherwise the current phase is kept so the stone continues seamlessly when shown again.
    public void Snap(bool still) { activation = Active ? 1 : 0; if (!Active) beat = 0; Compute(still); }
    public void Restart() { angle = 0; beat = 0; time = 0; activation = Active ? 1 : 0; Compute(false); }
    void Compute(bool still)
    {
        double e = activation * activation * (3 - 2 * activation);
        double p = still ? .5 : Pulse(beat) * (.92 + .08 * Drift.Noise(beat * .37, 5.2));
        Lit = e; Breath = e * p;
        var (fOn, lOn) = CinematicArt.FacingAt(angle, true); var (fOff, lOff) = CinematicArt.FacingAt(angle, false);
        Facing = fOff + (fOn - fOff) * e; Lateral = lOff + (lOn - lOff) * e;
        double shimmer = still ? 1 : 1 + .035 * e * Drift.Noise(time * 2.3, 9.1);
        Emission = (Idle + (1 - Idle) * e * (.68 + .32 * p)) * shimmer;
        Fissure = e * (still ? .5 : .14 + .86 * p) * shimmer;
    }
}

// The Cinematic D Shadow stone: a Button whose own box is the visible stone circle (Diameter), so hit area, keyboard focus
// ring and layout are the stone itself; the art box (larger: the image has transparent margins) and its light overflow the
// box without clipping. Layers (back to front, all rotating together about StoneArt.Centre): OFF art, ON art, OFF ember,
// ON ember, OFF/ON rim light; then the screen-fixed bounce light from the ground on the lower half (silhouette mask turning
// with the art). Hover lifts the stone 2 % and its rim light a little, press settles it to 97.5 % (render transforms only:
// nothing moves in the layout). Click, Enter and Space toggle Shadow Recording (the layout wires Click).
sealed class CinematicStone : Button
{
    static readonly ControlTemplate Plain = MakePlain();
    static readonly Style Ring = MakeRing();
    static ControlTemplate MakePlain() { var t = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }; t.Seal(); return t; }
    // Keyboard-only focus cue (FocusVisualStyle): a quiet dotted ring just outside the stone.
    static Style MakeRing()
    {
        var ring = new FrameworkElementFactory(typeof(Ellipse));
        ring.SetValue(MarginProperty, new Thickness(-9)); ring.SetResourceReference(Shape.StrokeProperty, "Muted"); ring.SetValue(Shape.StrokeThicknessProperty, 1.0);
        ring.SetValue(Shape.StrokeDashArrayProperty, new DoubleCollection { 1, 3 }); ring.SetValue(OpacityProperty, .8);
        var style = new Style(); style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = ring })); style.Seal(); return style;
    }
    readonly Grid face = new();
    readonly Canvas stage = new() { IsHitTestVisible = false };
    readonly Grid art = new();
    readonly Image off = Layer(CinematicArt.Off), on = Layer(CinematicArt.On), emberOff = Layer(CinematicArt.EmberOff), emberOn = Layer(CinematicArt.EmberOn), rimOff = Layer(CinematicArt.RimOff), rimOn = Layer(CinematicArt.RimOn);
    readonly Rectangle bounce = new() { IsHitTestVisible = false };
    readonly RotateTransform turn = new(), maskTurn = new();
    readonly ScaleTransform press = new();
    readonly Ellipse hit = new() { Fill = Brushes.Transparent };
    double diameter, hover;
    bool hinted;
    CinematicLight? light;
    static Image Layer(ImageSource source) { var image = new Image { Source = source, Stretch = Stretch.Fill, IsHitTestVisible = false }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); return image; }
    public double Diameter => diameter;
    public double ArtBox => diameter / (2 * StoneArt.Radius);

    public CinematicStone()
    {
        Template = Plain; FocusVisualStyle = Ring; Cursor = Cursors.Hand; Background = Brushes.Transparent; Padding = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(this, "Start or stop shadow recording");
        ToolTipService.SetShowOnDisabled(this, true);
        art.RenderTransformOrigin = StoneArt.Centre; art.RenderTransform = turn;
        foreach (var layer in new[] { off, on, emberOff, emberOn, rimOff, rimOn }) art.Children.Add(layer);
        on.Opacity = emberOn.Opacity = rimOn.Opacity = 0;
        // Bounce light: warm from below, fixed in screen space, on the silhouette only.
        var up = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
        up.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 140, 60), .4)); up.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 140, 60), .72)); up.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 150, 70), .9)); up.Freeze();
        maskTurn.CenterX = StoneArt.Centre.X; maskTurn.CenterY = StoneArt.Centre.Y;
        bounce.Fill = up; bounce.OpacityMask = new ImageBrush(CinematicArt.Silhouette) { Stretch = Stretch.Fill, RelativeTransform = maskTurn };
        stage.Children.Add(art); stage.Children.Add(bounce);
        stage.RenderTransform = press;
        face.Children.Add(hit); face.Children.Add(stage);
        Content = face;
        IsEnabledChanged += (_, _) => stage.Opacity = IsEnabled ? 1 : .62;
    }
    // Visible stone diameter (DIP). The art box and the press/hover centre follow it.
    public void SetDiameter(double value)
    {
        if (Math.Abs(value - diameter) < .01) return;
        diameter = value; Width = Height = face.Width = face.Height = value;
        double box = ArtBox;
        foreach (var e in new FrameworkElement[] { art, bounce }) { e.Width = e.Height = box; Canvas.SetLeft(e, value / 2 - StoneArt.Centre.X * box); Canvas.SetTop(e, value / 2 - StoneArt.Centre.Y * box); }
        press.CenterX = press.CenterY = value / 2;
    }
    // Hover look also when the pointer is on the title (the same action).
    public void Hint(bool on) { hinted = on; Lift(); }
    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); Lift(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); Lift(); }
    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e) { base.OnIsPressedChanged(e); Lift(); }
    void Lift()
    {
        bool over = IsEnabled && (IsMouseOver || hinted);
        double to = IsPressed ? .975 : over ? 1.02 : 1;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(IsPressed ? .08 : .2)) { EasingFunction = ease };
        press.BeginAnimation(ScaleTransform.ScaleXProperty, a); press.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        hover = over ? 1 : 0; if (light != null) Apply(light);
    }
    static void Fade(UIElement e, double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        e.Opacity = opacity; var v = opacity < .003 ? Visibility.Hidden : Visibility.Visible; if (e.Visibility != v) e.Visibility = v;
    }
    public void Apply(CinematicLight l)
    {
        light = l;
        turn.Angle = maskTurn.Angle = l.Angle;
        double e = l.Lit, d = Math.Clamp(e * 2 - 1, 0, 1);
        Fade(on, e); Fade(off, 1 - d * d * (3 - 2 * d));
        Fade(emberOn, l.Fissure); Fade(emberOff, l.Residual);
        double rim = l.Rim * (1 + .3 * hover);
        Fade(rimOn, e * rim); Fade(rimOff, (1 - e) * rim * .8);
        Fade(bounce, l.Bounce);
    }
}
