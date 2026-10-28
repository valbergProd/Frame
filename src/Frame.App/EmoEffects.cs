using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

// Emo mode effects (0.6.0): dark rose petals, rain, bats and slow drifting fog. Independent layers over the illustration and
// beneath FRAME's UI (never hit-tested), advanced only by CaptureAtmosphere's clock (~30 updates/s, dt clamped, no catch-up)
// while the Capture page is shown and the effect is on. Per update they only change GPU-side properties: brush transforms
// (rain, fog: seamless tiles rendered once) or one matrix and opacity per sprite (petals, bats: retained visuals drawn
// once); there is no per-frame OnRender, layout or geometry. Particle counts are fixed (preallocated). Everything is calmer
// over FRAME's content column (Calm). Hide() drops the bitmaps and visuals; a hidden effect costs nothing.
abstract class EmoEffect : FrameworkElement
{
    protected Random Random = new(1);
    protected bool Seeded;
    public abstract string Key { get; }
    public int Steps { get; private set; }
    protected EmoEffect() { IsHitTestVisible = false; Focusable = false; Visibility = Visibility.Collapsed; }
    public bool On => Visibility == Visibility.Visible;
    public void Show(Random random) { if (On) return; Random = random; Seeded = false; Visibility = Visibility.Visible; }
    public void Hide() { if (!On) return; Visibility = Visibility.Collapsed; Seeded = false; Release(); }
    public void Advance(double dt) { if (!On || ActualWidth < 1 || ActualHeight < 1) return; Steps++; if (!Seeded) { Seed(); Seeded = true; } Step(dt); }
    // Still state (clock stopped): prepared for the current size; nothing moves.
    public void Settle() { if (!On || ActualWidth < 1 || ActualHeight < 1) return; if (!Seeded) { Seed(); Seeded = true; Step(0); } }
    protected abstract void Seed();
    protected abstract void Step(double dt);
    protected abstract void Release();
    protected double K => ActualHeight / 800;
    double Next() => Random.NextDouble();
    protected double Rand(double a, double b) => a + (b - a) * Next();
    // FRAME's content column (x 190 … ~860 DIP, fixed) gets about a third of an effect, the free area on the right all of it.
    protected static double Calm(double x) { double t = Math.Clamp((x - 520) / 520, 0, 1); return .34 + .66 * t * t * (3 - 2 * t); }
    protected static LinearGradientBrush CalmMask()
    {
        var g = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1040, 0), MappingMode = BrushMappingMode.Absolute };
        for (int i = 0; i <= 8; i++) { double x = 1040 * i / 8d; g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * Calm(x)), 0, 0, 0), i / 8d)); }
        g.Freeze(); return g;
    }
    protected static BitmapSource Render(double width, double height, double scale, Action<DrawingContext> draw)
    {
        var v = new DrawingVisual(); using (var dc = v.RenderOpen()) { dc.PushTransform(new ScaleTransform(scale, scale)); draw(dc); dc.Pop(); }
        var bmp = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width * scale)), Math.Max(1, (int)Math.Ceiling(height * scale)), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v); bmp.Freeze(); return bmp;
    }
    protected double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); InvalidateVisual(); }
}

// Base for retained sprites (petals, bats): DrawingVisuals drawn once; per update only Transform matrix and Visual.Opacity.
abstract class EmoSprites : EmoEffect
{
    readonly VisualCollection? children; // null while the base constructor sets properties
    protected VisualCollection Children => children!;
    protected EmoSprites() => children = new VisualCollection(this);
    protected override int VisualChildrenCount => children?.Count ?? 0;
    protected override Visual GetVisualChild(int index) => children![index];
    protected override void Release() => Children.Clear();
}

// Rain: three seamless tiles of slanted streaks (far: fine, dense, slow; near: long, sparse, fast) scrolled down the window
// along the streak direction. Streak length ≥ the distance a tile moves per update, so the fall reads as continuous at
// 30 updates/s. Gusts swell the near layer a little.
sealed class EmoRain : EmoEffect
{
    static readonly double[] Tile = [256, 384, 512], Speed = [480, 700, 980], Alpha = [.55, .7, .8], Length = [18, 30, 52], Thick = [.7, .9, 1.25];
    static readonly int[] Count = [130, 60, 22];
    const double Slant = .15; // dx per dy: a light wind from the left
    readonly TranslateTransform[] moves = [new(), new(), new()];
    readonly ImageBrush?[] sheets = new ImageBrush?[3];
    readonly double[] ox = new double[3], oy = new double[3];
    LinearGradientBrush? mask;
    double time;
    public override string Key => "rain";
    protected override void Seed()
    {
        var color = Color.FromRgb(180, 172, 204); double dpi = Dpi;
        for (int i = 0; i < 3; i++)
        {
            double t = Tile[i]; var rnd = new Random(41 + i);
            var bmp = Render(t, t, dpi, dc =>
            {
                for (int n = 0; n < Count[i]; n++)
                {
                    double x = rnd.NextDouble() * t, y = rnd.NextDouble() * t, len = Length[i] * (.75 + .5 * rnd.NextDouble()), a = Alpha[i] * (.35 + .65 * rnd.NextDouble());
                    var brush = new LinearGradientBrush(Color.FromArgb(0, color.R, color.G, color.B), Color.FromArgb((byte)(255 * a), color.R, color.G, color.B), 90); brush.Freeze();
                    var pen = new Pen(brush, Thick[i]) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze();
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                            dc.DrawLine(pen, new(x + dx * t, y + dy * t), new(x + dx * t + Slant * len, y + dy * t + len));
                }
            });
            sheets[i] = new ImageBrush(bmp) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, t, t), Stretch = Stretch.Fill, Transform = moves[i] };
        }
        mask = CalmMask(); InvalidateVisual();
    }
    protected override void Step(double dt)
    {
        time += dt;
        double k = Math.Max(.6, K);
        for (int i = 0; i < 3; i++)
        {
            double fall = Speed[i] * k * dt;
            oy[i] = (oy[i] + fall) % Tile[i]; ox[i] = (ox[i] + fall * Slant) % Tile[i];
            moves[i].X = ox[i]; moves[i].Y = oy[i];
        }
        sheets[2]!.Opacity = .82 + .18 * Drift.Noise(time * .35, 3.3);
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (sheets[0] == null || mask == null) return;
        var r = new Rect(RenderSize);
        dc.PushOpacityMask(mask); for (int i = 0; i < 3; i++) dc.DrawRectangle(sheets[i], null, r); dc.Pop();
    }
    protected override void Release() { for (int i = 0; i < 3; i++) sheets[i] = null; mask = null; InvalidateVisual(); }
}

// Slow drifting fog: three wide seamless bands of soft lavender-grey haze (high veil, middle wisps, ground fog) drifting to
// the right at different speeds (parallax) and breathing slowly. Tiles are rendered once at half resolution (soft content)
// and stretched to the band's height; only brush transforms and opacities change per update.
sealed class EmoFog : EmoEffect
{
    static readonly (double Top, double Bottom, double Tile, double Speed, double Alpha)[] Bands = [(.1, .48, 1500, 4.5, .55), (.38, .8, 1200, 8, .8), (.6, 1.04, 980, 13, 1)];
    readonly TranslateTransform[] moves = [new(), new(), new()];
    readonly ImageBrush?[] sheets = new ImageBrush?[3];
    readonly double[] travel = new double[3];
    LinearGradientBrush? mask;
    double time;
    public override string Key => "fog";
    protected override void Seed()
    {
        var color = Color.FromRgb(140, 130, 156);
        for (int i = 0; i < 3; i++)
        {
            var (_, _, tile, _, _) = Bands[i]; var rnd = new Random(71 + i); const double h = 400;
            var bmp = Render(tile, h, .5, dc =>
            {
                for (int n = 0; n < 16; n++)
                {
                    double x = rnd.NextDouble() * tile, y = h * (.38 + .24 * rnd.NextDouble()), rx = 110 + 240 * rnd.NextDouble(), ry = 36 + 70 * rnd.NextDouble(), a = .1 + .16 * rnd.NextDouble();
                    var g = new RadialGradientBrush { RadiusX = .5, RadiusY = .5 };
                    for (int s = 0; s <= 10; s++) { double r = s / 10d, f = 1 - r * r; g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * a * f * f), color.R, color.G, color.B), r)); }
                    g.Freeze();
                    for (int dx = -1; dx <= 1; dx++) dc.DrawEllipse(g, null, new(x + dx * tile, y), rx, ry);
                }
            });
            sheets[i] = new ImageBrush(bmp) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, Transform = moves[i] };
            travel[i] = rnd.NextDouble() * tile;
        }
        mask = CalmMask(); InvalidateVisual();
    }
    protected override void Step(double dt)
    {
        time += dt; double k = Math.Max(.6, K);
        for (int i = 0; i < 3; i++)
        {
            var band = Bands[i];
            travel[i] = (travel[i] + band.Speed * k * dt) % (band.Tile * k);
            moves[i].X = travel[i]; moves[i].Y = 6 * k * Drift.Noise(time * .06, i * 2.1);
            sheets[i]!.Opacity = band.Alpha * (.8 + .2 * Drift.Noise(time * .045, 5 + i));
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (sheets[0] == null || mask == null) return;
        double w = ActualWidth, h = ActualHeight, k = Math.Max(.6, h / 800);
        dc.PushOpacityMask(mask);
        for (int i = 0; i < 3; i++)
        {
            var band = Bands[i]; double top = band.Top * h, height = (band.Bottom - band.Top) * h;
            sheets[i]!.Viewport = new Rect(0, top, band.Tile * k, height);
            dc.DrawRectangle(sheets[i], null, new Rect(0, top, w, height));
        }
        dc.Pop();
    }
    protected override void Release() { for (int i = 0; i < 3; i++) sheets[i] = null; mask = null; InvalidateVisual(); }
}

// Dark rose petals: 24 petals in four shades (deep crimson to almost black) fall slowly through the window, carried a little
// to the right, swaying and tumbling (a 3-D flip as horizontal scale). Depth z sets size, speed and brightness (parallax).
sealed class EmoPetals : EmoSprites
{
    struct Petal { public double X, Y, Z, Fall, Drift, Rot, Spin, Flip, FlipRate, Sway, SwayRate, SwayAmp; }
    const int Count = 24;
    readonly Petal[] petals = new Petal[Count];
    readonly MatrixTransform[] moves = new MatrixTransform[Count];
    readonly DrawingVisual[] sprites = new DrawingVisual[Count];
    public override string Key => "petals";
    static readonly Geometry Shape = Frozen(Geometry.Parse("M 0,7 C -4.6,5.4 -7,0.4 -5.6,-3.8 C -4.6,-6.8 -1.8,-7.2 0,-5.6 C 1.6,-7.4 4.8,-6.8 5.8,-3.6 C 7,0.6 4.4,5.6 0,7 Z"));
    static readonly Geometry Vein = Frozen(Geometry.Parse("M 0,6 C -0.6,2 -0.4,-1.6 0.4,-4.4"));
    static Geometry Frozen(Geometry g) { if (!g.IsFrozen) g.Freeze(); return g; }
    static Drawing Look(Color edge, Color body, Color heart)
    {
        var fill = new RadialGradientBrush { GradientOrigin = new(.5, .95), Center = new(.5, .8), RadiusX = .85, RadiusY = .95 };
        fill.GradientStops.Add(new GradientStop(heart, 0)); fill.GradientStops.Add(new GradientStop(body, .55)); fill.GradientStops.Add(new GradientStop(edge, 1)); fill.Freeze();
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawGeometry(fill, null, Shape);
            var vein = new Pen(new SolidColorBrush(Color.FromArgb(70, 20, 2, 8)), .5); vein.Freeze(); dc.DrawGeometry(null, vein, Vein);
        }
        group.Freeze(); return group;
    }
    protected override void Seed()
    {
        Children.Clear();
        Drawing[] looks =
        [
            Look(Color.FromRgb(84, 16, 32), Color.FromRgb(136, 28, 50), Color.FromRgb(48, 6, 16)),
            Look(Color.FromRgb(58, 10, 22), Color.FromRgb(96, 18, 36), Color.FromRgb(30, 4, 10)),
            Look(Color.FromRgb(40, 8, 16), Color.FromRgb(68, 12, 26), Color.FromRgb(20, 3, 8)),
            Look(Color.FromRgb(64, 18, 40), Color.FromRgb(98, 30, 58), Color.FromRgb(34, 8, 20)),
        ];
        double w = ActualWidth, h = ActualHeight;
        for (int i = 0; i < Count; i++)
        {
            var v = new DrawingVisual { Transform = moves[i] = new MatrixTransform() };
            using (var dc = v.RenderOpen()) dc.DrawDrawing(looks[i % looks.Length]);
            sprites[i] = v; Children.Add(v);
            Spawn(ref petals[i], w, h, anywhere: true);
        }
    }
    void Spawn(ref Petal p, double w, double h, bool anywhere)
    {
        double k = Math.Max(.6, K);
        p.Z = .55 + .8 * Math.Pow(Random.NextDouble(), 1.5);
        p.X = Rand(-.12, 1.02) * w; p.Y = anywhere ? Rand(-.05, 1) * h : -Rand(14, 70) * k;
        p.Fall = Rand(20, 34) * p.Z * k; p.Drift = Rand(6, 16) * p.Z * k;
        p.Rot = Rand(0, 360); p.Spin = Rand(-38, 38); p.Flip = Rand(0, Math.Tau); p.FlipRate = Rand(.5, 1.5);
        p.Sway = Rand(0, Math.Tau); p.SwayRate = Rand(.35, .75); p.SwayAmp = Rand(8, 20) * p.Z * k;
    }
    protected override void Step(double dt)
    {
        double w = ActualWidth, h = ActualHeight, k = Math.Max(.6, K);
        for (int i = 0; i < Count; i++)
        {
            ref var p = ref petals[i];
            p.Sway += p.SwayRate * dt; p.Flip += p.FlipRate * dt; p.Rot += p.Spin * dt;
            double sway = Math.Cos(p.Sway) * p.SwayAmp * p.SwayRate; // derivative of a sine drift: smooth side-to-side
            p.X += (p.Drift + sway) * dt; p.Y += p.Fall * (.8 + .3 * Math.Abs(Math.Sin(p.Flip))) * dt;
            if (p.Y > h + 24 * k || p.X > w + 30 * k) Spawn(ref p, w, h, anywhere: false);
            double fx = Math.Cos(p.Flip); if (Math.Abs(fx) < .14) fx = fx < 0 ? -.14 : .14;
            var m = Matrix.Identity; m.Scale(fx * p.Z * k * 1.7, p.Z * k * 1.7); m.Rotate(p.Rot); m.Translate(p.X, p.Y);
            moves[i].Matrix = m;
            sprites[i].Opacity = Calm(p.X) * (.55 + .45 * Math.Min(1, p.Z)) * (.8 + .2 * Math.Abs(fx));
        }
    }
}

// Bats: at most four, rarely more than two at once. Each flies a gentle curve through the sky on the right (in from the
// right edge and out over the top, or the reverse), flapping through five wing poses (child visuals; one shown at a time),
// then rests 3–11 s off-screen. Small, dark, with a faint violet edge so they read against the night sky.
sealed class EmoBats : EmoSprites
{
    const int Count = 4, Poses = 5;
    struct Bat { public bool Flying; public double Wait, T, Duration, Phase, Rate, Size, Wobble; public Point P0, P1, P2; public int Pose; }
    readonly Bat[] bats = new Bat[Count];
    readonly ContainerVisual[] hosts = new ContainerVisual[Count];
    readonly DrawingVisual[][] poses = new DrawingVisual[Count][];
    readonly MatrixTransform[] moves = new MatrixTransform[Count];
    public override string Key => "bats";
    static readonly double[] Lift = [1, .45, -.25, -.85, -.1]; // wing tip height per pose (up → down → rising)
    static Drawing Pose(double u)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int side = -1; side <= 1; side += 2)
            {
                double span = 1 - .2 * Math.Abs(u);
                Point P(double x, double y) => new(side * x, y);
                c.BeginFigure(P(.06, -.06), true, true);
                c.BezierTo(P(.2, -.16 - .2 * u), P(.36, -.22 - .3 * u), P(.48, -.2 - .34 * u), true, true);
                c.LineTo(P(span, -.06 - .5 * u), true, true);
                c.QuadraticBezierTo(P(.84 * span, .02 - .32 * u), P(.74 * span, .08 - .26 * u), true, true);
                c.QuadraticBezierTo(P(.62 * span, .02 - .16 * u), P(.5 * span, .12 - .14 * u), true, true);
                c.QuadraticBezierTo(P(.38, .06 - .08 * u), P(.26, .14 - .04 * u), true, true);
                c.QuadraticBezierTo(P(.16, .08), P(.06, .14), true, true);
            }
            c.BeginFigure(new(0, -.2), true, true); // head with ears, body
            c.LineTo(new(.05, -.27), true, true); c.LineTo(new(.07, -.15), true, true); c.QuadraticBezierTo(new(.1, .08), new(0, .22), true, true);
            c.QuadraticBezierTo(new(-.1, .08), new(-.07, -.15), true, true); c.LineTo(new(-.05, -.27), true, true);
        }
        g.Freeze();
        var fill = new SolidColorBrush(Color.FromRgb(8, 5, 10)); fill.Freeze();
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(120, 74, 58, 96)), .045); edge.Freeze();
        var d = new GeometryDrawing(fill, edge, g); d.Freeze(); return d;
    }
    protected override void Seed()
    {
        Children.Clear();
        var looks = Lift.Select(Pose).ToArray();
        for (int i = 0; i < Count; i++)
        {
            var host = new ContainerVisual { Transform = moves[i] = new MatrixTransform(), Opacity = 0 };
            poses[i] = new DrawingVisual[Poses];
            for (int p = 0; p < Poses; p++) { var v = new DrawingVisual { Opacity = p == 0 ? 1 : 0 }; using (var dc = v.RenderOpen()) dc.DrawDrawing(looks[p]); poses[i][p] = v; host.Children.Add(v); }
            hosts[i] = host; Children.Add(host);
            bats[i] = new Bat { Wait = i == 0 ? Rand(.4, 1.5) : Rand(2, 9) };
        }
        // Still presentation (before the first update or with motion off): two bats already in the air.
        Launch(ref bats[0], 0); bats[0].T = .42; Launch(ref bats[1], 1); bats[1].T = .58;
    }
    void Launch(ref Bat b, int i)
    {
        double w = ActualWidth, h = ActualHeight, k = Math.Max(.6, K);
        b.Flying = true; b.T = 0; b.Size = Rand(14, 22) * k; b.Rate = Rand(4.2, 6.2); b.Phase = Rand(0, 1); b.Wobble = Rand(0, Math.Tau);
        if (Random.NextDouble() < .62) { b.P0 = new(w + 40 * k, Rand(.16, .48) * h); b.P1 = new(Rand(.6, .84) * w, Rand(.12, .42) * h); b.P2 = new(Rand(.68, .98) * w, -50 * k); }
        else { b.P0 = new(Rand(.7, .97) * w, -40 * k); b.P1 = new(Rand(.6, .82) * w, Rand(.2, .46) * h); b.P2 = new(w + 50 * k, Rand(.1, .38) * h); }
        double length = (b.P1 - b.P0).Length + (b.P2 - b.P1).Length;
        b.Duration = length / (Rand(85, 130) * k * (b.Size / (15 * k)));
        hosts[i].Opacity = 0;
    }
    protected override void Step(double dt)
    {
        for (int i = 0; i < Count; i++)
        {
            ref var b = ref bats[i];
            if (!b.Flying)
            {
                b.Wait -= dt;
                if (b.Wait > 0) continue;
                Launch(ref b, i);
                // Now and then a second bat follows closely.
                for (int j = 0; j < Count; j++) if (j != i && !bats[j].Flying && bats[j].Wait > 1.2 && Random.NextDouble() < .3) { bats[j].Wait = Rand(.5, 1.1); break; }
            }
            b.T += dt / b.Duration; b.Phase += b.Rate * dt; b.Wobble += dt * 2.2;
            if (b.T >= 1) { b.Flying = false; b.Wait = Rand(3, 11); hosts[i].Opacity = 0; continue; }
            double t = b.T, u = 1 - t;
            var p = new Point(u * u * b.P0.X + 2 * u * t * b.P1.X + t * t * b.P2.X, u * u * b.P0.Y + 2 * u * t * b.P1.Y + t * t * b.P2.Y);
            var d = new Vector(2 * u * (b.P1.X - b.P0.X) + 2 * t * (b.P2.X - b.P1.X), 2 * u * (b.P1.Y - b.P0.Y) + 2 * t * (b.P2.Y - b.P1.Y)); if (d.Length > 1e-6) d.Normalize();
            p += new Vector(-d.Y, d.X) * (b.Size * .7 * Math.Sin(b.Wobble) + b.Size * .18 * Math.Sin(b.Wobble * 4.3));
            int pose = (int)(b.Phase * Poses % Poses);
            if (pose != b.Pose) { poses[i][b.Pose].Opacity = 0; poses[i][pose].Opacity = 1; b.Pose = pose; }
            double lift = Lift[pose] * .06 * b.Size; // the body bobs against the wing beat
            var m = Matrix.Identity; m.Scale(b.Size, b.Size); m.Rotate(Math.Clamp(d.X * d.Y * 30, -14, 14)); m.Translate(p.X, p.Y + lift);
            moves[i].Matrix = m;
            hosts[i].Opacity = .95 * Calm(p.X) * Math.Clamp(Math.Min(t, 1 - t) / .04, 0, 1);
        }
    }
}
