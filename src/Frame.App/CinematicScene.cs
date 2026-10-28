using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Frame.App;

// Ground of the Cinematic D composition, generated once (~0.2 s, no assets): a band of dark dusty ground in perspective
// (a ground plane seen at a grazing angle: texture shrinks and flattens toward the horizon) with scattered rocks of three
// sizes (convex: lit top, dark front, a short shadow toward the viewer), a warm haze near the soft irregular horizon and
// soft side edges. Placed under the stone in stone units (Span R x 4 R), so it scales with the stone. Base: the ground
// itself (premultiplied dark umber, alpha up to ~0.9). Grit: an alpha mask of where the stone's warm light lands (dust,
// rock tops; rock fronts and shadows stay dark), so the light pool reveals a rough surface instead of a flat floor.
static class GroundArt
{
    public const int W = 1400, H = 400, Span = 14; // stone units: Span R wide, 4 R high
    static BitmapSource? baseImage, grit;
    public static double Milliseconds { get; private set; }
    public static BitmapSource Base { get { Ensure(); return baseImage!; } }
    public static BitmapSource Grit { get { Ensure(); return grit!; } }
    static uint Hash(int x, int y, uint seed) { uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ seed * 0xCB1AB31Fu; h ^= h >> 13; h *= 0x5BD1E995u; h ^= h >> 15; return h; }
    static double Unit(uint h) => (h & 0xFFFFFF) / 16777216d;
    // Rocks: one candidate per cell (most cells empty) with an irregular outline (two angular harmonics) around a jittered
    // point, in screen-proportional coordinates (they stick up, so perspective does not flatten them like the ground).
    // Returns the body (0..1), its light (the upper side of a convex rock catches the stone's light; the side facing the
    // viewer stays dark) and the short shadow it casts toward the viewer, so rocks read as solid shapes on the lit ground.
    static (double Body, double Lit, double Shadow) Rocks(double x, double y, uint seed, double fill)
    {
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y); double best = 0, lit = 0, shadow = 0;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                uint h = Hash(cx + i, cy + j, seed); if (Unit(h >> 3) > fill) continue;
                double px = cx + i + .2 + .6 * Unit(h), py = cy + j + .2 + .6 * Unit(h >> 7 | h << 25), radius = .18 + .3 * Unit(h >> 11 | h << 21);
                double dx = x - px, dy = y - py, r = Math.Sqrt(dx * dx + dy * dy);
                if (r >= 1.9 * radius) continue;
                double a = Math.Atan2(dy, dx), shape = radius * (1 + .2 * Math.Sin(3 * a + 6.3 * Unit(h >> 5)) + .1 * Math.Sin(5 * a + 6.3 * Unit(h >> 13)));
                double d = r / shape;
                if (d >= 1) { if (dy > 0 && d < 1.8) shadow = Math.Max(shadow, (1 - (d - 1) / .8) * Math.Clamp(dy / shape * 1.6, 0, 1)); continue; }
                double dome = 1 - d * d; if (dome <= best) continue;
                best = dome; lit = Math.Clamp(.25 - 1.3 * dy / shape, 0, 1) * Math.Pow(dome, .25) * (.55 + .45 * Unit(h * 2654435761u));
            }
        return (Smooth(.02, .2, best), lit, shadow * shadow);
    }    // Smooth value noise (lattice, smoothstep interpolation), about -1..1.
    static double Value(double x, double y, uint seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y); double tx = x - x0, ty = y - y0; tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        double a = Unit(Hash(x0, y0, seed)), b = Unit(Hash(x0 + 1, y0, seed)), c = Unit(Hash(x0, y0 + 1, seed)), d = Unit(Hash(x0 + 1, y0 + 1, seed));
        return 2 * (a + (b - a) * tx + (c + (d - c) * tx - a - (b - a) * tx) * ty) - 1;
    }
    static double Smooth(double a, double b, double v) => DesertTerrain.Smooth(a, b, v);
    static void Ensure()
    {
        if (baseImage != null) return;
        var clock = Stopwatch.StartNew();
        var basePx = new byte[W * H * 4]; var gritPx = new byte[W * H * 4];
        // Ground plane: horizon above the band's top edge; X across, D in depth (perspective: features shrink and flatten
        // toward the horizon).
        const double horizon = -.45, zb = 1 / (1 - horizon), depthScale = 8;
        for (int py = 0; py < H; py++)
        {
            double v = (py + .5) / H, z = 1 / (v - horizon);
            for (int px = 0; px < W; px++)
            {
                double u = (px + .5) / W, X = (u - .5) * Span * z / zb, D = z * depthScale / zb;
                // Dusty ground with soft patches; scattered rocks of three sizes (large ones rare). Base: dark rocks with a faint
                // lit top. Grit (where the warm light lands): the ground, rock tops bright, the rock sides facing us in shade.
                double dust = .5 + .32 * Value(X * 1.3, D * 1.3, 3) + .18 * Value(X * 4.1, D * 4.1, 5), grain = .5 + .5 * Value(X * 16, D * 16, 7);
                double Y = 1.7 * 4 / zb * Math.Log(v - horizon) + 3; // rock rows: same DIP scale as X here, rocks 1.7x flattened
                var (b1, l1, s1) = Rocks(X * .55, Y * .55, 13, .08);
                var (b2, l2, s2) = Rocks(X * 1.5, Y * 1.5, 23, .2);
                var (b3, l3, s3) = Rocks(X * 3.8, Y * 3.8, 37, .3);
                double body = Math.Max(b1, Math.Max(b2, b3)), lit = b1 >= b2 && b1 >= b3 ? l1 : b2 >= b3 ? l2 : l3, shade = Math.Max(s1, Math.Max(s2, s3));
                double ground = (.22 + .14 * dust + .08 * (grain - .5)) * (1 - .45 * shade);
                double albedo = Math.Clamp(ground * (1 - body) + body * (.08 + .32 * lit), 0, 1);
                double rough = .55 + .45 * Value(X * 9, Y * 9, 19);
                double tops = Math.Clamp((.3 + .24 * dust + .22 * (grain - .5)) * (1 - .75 * shade) * (1 - body) + body * (.12 + .88 * lit * rough), 0, 1);
                // Far ground catches the warm sky light near the horizon (static haze).
                double haze = 1 - Smooth(.1, .45, v);
                // Irregular soft horizon (stone units: fades in over ~0.8 R) and soft side edges.
                double edge = .1 + .05 * Math.Sin(u * 23 + 1.3) + .03 * Math.Sin(u * 61 + .4);
                double alpha = Smooth(edge - .04, edge + .17, v) * Smooth(0, .06, u) * (1 - Smooth(.8, 1, u)) * (.84 + .08 * v);
                double r = 10 + 34 * albedo + 34 * haze, g = 8 + 24 * albedo + 13 * haze, b = 7 + 18 * albedo + 4 * haze;
                int i = (py * W + px) * 4;
                basePx[i] = (byte)(b * alpha); basePx[i + 1] = (byte)(g * alpha); basePx[i + 2] = (byte)(r * alpha); basePx[i + 3] = (byte)(255 * alpha);
                byte m = (byte)(255 * tops * Smooth(edge - .06, edge + .1, v));
                gritPx[i] = gritPx[i + 1] = gritPx[i + 2] = gritPx[i + 3] = m;
            }
        }
        baseImage = BitmapSource.Create(W, H, 96, 96, PixelFormats.Pbgra32, null, basePx, W * 4); baseImage.Freeze();
        grit = BitmapSource.Create(W, H, 96, 96, PixelFormats.Pbgra32, null, gritPx, W * 4); grit.Freeze();
        Milliseconds = clock.Elapsed.TotalMilliseconds;
    }
}

// Cinematic D scene (0.6.0): full-window layers behind the sidebar and the Capture layout, above the 0.5.5 desert
// (backdrop, dune light and sand keep running unchanged underneath):
// - Shade: a static vignette that keeps the sun-lit upper right and darkens the left and the lower half, so the stone is
//   the brightest focus (as in the reference composition).
// - Ground: GroundArt under the stone (stone units), so it rests on dark rocky ground.
// - Stone light (only while the Capture page is shown): spill and halo around the stone, the warm light pool on the
//   pebbles (Grit mask; it shifts toward the side the bright cracks face), a soft bounce under the stone, the contact
//   occlusion and a very faint reflection of the fissure light (the ember art mirrored, squashed and broken up by the
//   pebbles: no wet floor). None of them animates on its own: CinematicLight (advanced by the layout's clock client)
//   sets their opacities, the pool centre and the reflection's rotation, the same state that lights the stone, rim, text
//   and slider. Placement comes from the layout (Place, window coordinates) whenever the stone moves or resizes.
sealed class CinematicScene : IModeScene
{
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static RadialGradientBrush Light(byte peak, byte r, byte g, byte b)
    {
        var brush = new RadialGradientBrush();
        for (int s = 0; s <= 12; s++) { double o = s / 12d, f = (1 - o * o) * (1 - o * o); brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(peak * f), r, g, b), o)); }
        return Frozen(brush);
    }
    readonly Grid root = new() { IsHitTestVisible = false };
    readonly Canvas world = new() { IsHitTestVisible = false }, lights = new() { IsHitTestVisible = false };
    readonly Image ground = new() { Stretch = Stretch.Fill };
    readonly Ellipse spill = new() { Fill = Light(46, 255, 112, 40) }, halo = new() { Fill = Light(92, 255, 128, 48) };
    readonly Ellipse soft = new() { Fill = Light(120, 255, 140, 62) }, contact = new();
    readonly Rectangle pool = new();
    readonly RadialGradientBrush poolBrush = new();
    readonly Canvas reflection = new();
    readonly Image reflectOn = new() { Stretch = Stretch.Fill }, reflectOff = new() { Stretch = Stretch.Fill };
    readonly RotateTransform turnOn = new(), turnOff = new();
    readonly ScaleTransform mirrorOn = new(), mirrorOff = new();
    Rect groundRect;
    Point poolCentre;
    double poolShift;
    bool placed;
    public FrameworkElement Root => root;
    public FrameworkElement? Tools => null;
    public FrameworkElement? Hero => null;
    public bool WantsFrames => false; // the layout's light client drives every layer
    public bool FullRate => false;

    public CinematicScene()
    {
        // Vignette: transparent around the sun (upper right), darker toward the left and the bottom.
        var vignette = new RadialGradientBrush { Center = new Point(.86, .02), GradientOrigin = new Point(.86, .02), RadiusX = 1.05, RadiusY = 1.2 };
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 6, 4, 3), 0)); vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 6, 4, 3), .32));
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 6, 4, 3), .66)); vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0xA8, 6, 4, 3), 1));
        root.Children.Add(new Rectangle { Fill = Frozen(vignette) });
        var left = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        left.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 6, 4, 3), 0)); left.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 6, 4, 3), .32)); left.GradientStops.Add(new GradientStop(Color.FromArgb(0, 6, 4, 3), .55));
        root.Children.Add(new Rectangle { Fill = Frozen(left) });
        root.Children.Add(world);
        ground.Source = GroundArt.Base; ground.Visibility = Visibility.Hidden; world.Children.Add(ground); // shown once placed
        world.Children.Add(lights);
        lights.Children.Add(spill);
        for (int s = 0; s <= 10; s++) { double o = s / 10d, f = (1 - o * o) * (1 - o * o); poolBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * f), 255, 150, 70), o)); }
        pool.Fill = poolBrush;
        lights.Children.Add(pool); lights.Children.Add(soft);
        var occlusion = new RadialGradientBrush(); occlusion.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 4, 3, 2), 0)); occlusion.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 4, 3, 2), .55)); occlusion.GradientStops.Add(new GradientStop(Color.FromArgb(0, 4, 3, 2), 1));
        contact.Fill = Frozen(occlusion); lights.Children.Add(contact);
        // Reflection: fissure light mirrored at the contact line; its own mask fades it within ~40 % of the stone height,
        // the container's Grit mask breaks it into glints on the pebbles.
        var fade = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .5)); fade.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), .92)); Frozen(fade);
        foreach (var (image, source, turn, mirror) in new[] { (reflectOn, CinematicArt.SoftOn, turnOn, mirrorOn), (reflectOff, CinematicArt.SoftOff, turnOff, mirrorOff) })
        {
            image.Source = source; image.OpacityMask = fade; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
            var group = new TransformGroup(); group.Children.Add(turn); group.Children.Add(mirror); image.RenderTransform = group;
            reflection.Children.Add(image);
        }
        lights.Children.Add(reflection);
        lights.Children.Add(halo);
    }
    public void Enter() { }
    public void Leave() { }
    public void Advance(double dt) { }
    public void Present() { }
    public void PageChanged(bool capturePage) => lights.Visibility = capturePage ? Visibility.Visible : Visibility.Collapsed;

    Brush Grit(Rect area) => Frozen(new ImageBrush(GroundArt.Grit) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(groundRect.X - area.X, groundRect.Y - area.Y, groundRect.Width, groundRect.Height) });
    static void Put(FrameworkElement e, double x, double y, double w, double h) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); e.Width = Math.Max(0, w); e.Height = Math.Max(0, h); }
    // Stone geometry in window coordinates: visible centre and radius, and the art box (the rotating image square).
    public void Place(Point centre, double radius, Rect art)
    {
        placed = true; ground.Visibility = Visibility.Visible; double r = radius, contactY = centre.Y + .95 * r;
        groundRect = new Rect(centre.X - GroundArt.Span / 2d * r, contactY - .75 * r, GroundArt.Span * r, 4 * r);
        Put(ground, groundRect.X, groundRect.Y, groundRect.Width, groundRect.Height);
        // Light layers cover only where their light falls (small intermediate surfaces for the masks); the Grit masks are
        // mapped onto the whole ground rectangle, so every layer lights the same pebbles.
        var poolRect = new Rect(centre.X - 3.2 * r, contactY - 1.05 * r, 6.4 * r, 2.2 * r);
        Put(pool, poolRect.X, poolRect.Y, poolRect.Width, poolRect.Height); pool.OpacityMask = Grit(poolRect);
        poolCentre = new Point(.5, (contactY - .04 * r - poolRect.Y) / poolRect.Height); poolShift = .4 * r / poolRect.Width;
        poolBrush.RadiusX = 2.9 * r / poolRect.Width; poolBrush.RadiusY = .8 * r / poolRect.Height;
        Put(soft, centre.X - 1.7 * r, contactY - .3 * r, 3.4 * r, .7 * r);
        Put(contact, centre.X - .62 * r, contactY - .12 * r, 1.24 * r, .2 * r);
        Put(spill, centre.X - 3.3 * r, centre.Y - 3 * r, 6.6 * r, 6.3 * r);
        Put(halo, centre.X - 1.6 * r, centre.Y - 1.55 * r, 3.2 * r, 3.2 * r);
        var mirrorRect = new Rect(art.X, contactY - .15 * r, art.Width, 1.05 * r);
        Put(reflection, mirrorRect.X, mirrorRect.Y, mirrorRect.Width, mirrorRect.Height); reflection.OpacityMask = Grit(mirrorRect);
        double box = art.Width, mirrorLine = contactY - art.Y;
        foreach (var (image, turn, mirror) in new[] { (reflectOn, turnOn, mirrorOn), (reflectOff, turnOff, mirrorOff) })
        {
            Put(image, art.X - mirrorRect.X, art.Y - mirrorRect.Y, box, box);
            turn.CenterX = StoneArt.Centre.X * box; turn.CenterY = StoneArt.Centre.Y * box;
            mirror.ScaleY = -.36; mirror.CenterY = mirrorLine;
        }
    }
    static void Fade(UIElement e, double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        e.Opacity = opacity; var v = opacity < .003 ? Visibility.Hidden : Visibility.Visible; if (e.Visibility != v) e.Visibility = v;
    }
    public void Apply(CinematicLight l)
    {
        if (!placed) return;
        Fade(spill, l.Halo); Fade(halo, l.Halo);
        Fade(pool, .85 * l.Pool); Fade(soft, .8 * l.Pool);
        var c = new Point(poolCentre.X + poolShift * l.Lateral, poolCentre.Y); poolBrush.Center = poolBrush.GradientOrigin = c;
        turnOn.Angle = turnOff.Angle = l.Angle;
        Fade(reflectOn, l.Lit * .55 * l.Pool); Fade(reflectOff, (1 - l.Lit) * .7 * l.Pool);
    }
}
