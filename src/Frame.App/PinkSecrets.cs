using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Frame.App;

// Hidden secrets of Pink mode (0.6.0): three small clickable surprises, placed only where FRAME shows no UI on the Capture
// page (PinkScene recomputes the free spots from ModeContext.UiRects on size/page/mode changes, never per frame). No
// tooltip and no hint text; a hand cursor is the only hover cue. Each raises a PinkWorld signal (dogs may react):
//   "secret-heart"  a tiny heart in the empty middle of the sidebar → a fountain of little hearts; butterflies flutter to it.
//   "secret-treat"  a heart-shaped sugar cookie resting on the ground line right of the content → it wiggles (dogs can run
//                   to the point), then crumbles into sprinkles after ~4 s (or at once on "treat-eaten"); back after 25 s.
//   "secret-wish"   a faint twinkling star in the free sky → a shooting star with a glittering tail, then a soft shower of
//                   tiny stars; the star returns after 30 s.
// Only the three spots are hit-testable (Canvas without background); burst particles never are. Idle twinkles run only
// while the scene's clock runs; an event keeps the clock wanted until it has finished (Busy).
sealed class PinkSecrets : Canvas
{
    // A clickable spot: a transparent 26 x 26 hit area with the art (and a soft glow) drawn by two posed DrawingVisuals.
    sealed class Spot : FrameworkElement
    {
        readonly VisualCollection parts;
        public readonly MatrixTransform Art = new(), Glow = new();
        readonly DrawingVisual art, glow;
        public Point At; public bool Placed;
        public Spot(Drawing drawing, string name)
        {
            parts = new VisualCollection(this); Width = Height = 26; Cursor = Cursors.Hand; Focusable = false;
            System.Windows.Automation.AutomationProperties.SetName(this, name);
            glow = new DrawingVisual { Transform = Glow, Opacity = 0 }; using (var dc = glow.RenderOpen()) dc.DrawDrawing(PinkArt.Sparkles[0]);
            art = new DrawingVisual { Transform = Art }; using (var dc = art.RenderOpen()) dc.DrawDrawing(drawing);
            parts.Add(glow); parts.Add(art);
        }
        protected override int VisualChildrenCount => parts.Count;
        protected override Visual GetVisualChild(int index) => parts[index];
        protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 26, 26));
        public void Pose(double scale, double degrees, double dy, double opacity, double glowScale, double glowOpacity)
        {
            double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            Art.Matrix = new Matrix(scale * c, scale * s, -scale * s, scale * c, 13, 13 + dy); art.Opacity = opacity;
            Glow.Matrix = new Matrix(glowScale, 0, 0, glowScale, 13, 13 + dy); glow.Opacity = glowOpacity;
        }
    }
    // Burst particles (hearts, sprinkles, stars, glints), pooled; not hit-testable.
    sealed class Bits : FrameworkElement
    {
        public readonly VisualCollection Children;
        public Bits() { Children = new VisualCollection(this); IsHitTestVisible = false; }
        protected override int VisualChildrenCount => Children.Count;
        protected override Visual GetVisualChild(int index) => Children[index];
    }
    const int HeartArt = 0, SprinkleArt = 4, StarArt = 8, GlintArt = 9;
    static Drawing ArtFor(int kind) => kind < 4 ? PinkArt.Hearts[kind] : kind < 8 ? PinkArt.Sprinkles[kind - 4] : kind == StarArt ? PinkArt.Sparkles[3] : PinkArt.Sparkles[0];
    readonly PinkStage stage;
    readonly Spot heart = new(PinkArt.Hearts[0], "Hidden heart"), treat = new(PinkArt.Cookie, "Sugar cookie"), star = new(PinkArt.Sparkles[3], "Wishing star");
    readonly Bits layer = new();
    readonly DrawingVisual streak = new() { Opacity = 0 }; readonly MatrixTransform streakPose = new();
    static readonly Drawing StreakArt = MakeStreak();
    static Drawing MakeStreak()
    {
        var fill = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(-150, 0) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(200, 255, 170, 214), .3)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 120, 190), 1)); fill.Freeze();
        var glow = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(-150, 0) };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(110, 255, 110, 185), 0)); glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 110, 185), 1)); glow.Freeze();
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(glow, null, Geometry.Parse("M 4,0 C 4,-6 0,-8 -10,-7 L -150,0 L -10,7 C 0,8 4,6 4,0 Z")));
        g.Children.Add(new GeometryDrawing(fill, null, Geometry.Parse("M 2,0 C 2,-2.6 0,-3.2 -6,-3 L -150,0 L -6,3 C 0,3.2 2,2.6 2,0 Z")));
        g.Freeze(); return g;
    }
    readonly Mote[] bits = new Mote[64];
    int cursor;
    double treatWait = 1.4, time, heartEvent = -1, heartPulse = 6, treatEvent = -1, treatGone = -1, starEvent = -1, starGone = -1, shooting = -1;
    Point shotAt; Vector shotVelocity;
    public PinkSecrets(PinkStage stage)
    {
        this.stage = stage; Background = null; ClipToBounds = true;
        Children.Add(layer);
        streak.Transform = streakPose; using (var dc = streak.RenderOpen()) dc.DrawDrawing(StreakArt); layer.Children.Add(streak);
        foreach (var spot in new[] { heart, treat, star }) { spot.Visibility = Visibility.Collapsed; Children.Add(spot); }
        heart.MouseLeftButtonDown += (_, e) => { e.Handled = true; Trigger("heart"); };
        treat.MouseLeftButtonDown += (_, e) => { e.Handled = true; Trigger("treat"); };
        star.MouseLeftButtonDown += (_, e) => { e.Handled = true; Trigger("wish"); };
        stage.World.Signal += (name, _, _) => { if (name == "treat-eaten" && treatEvent >= 0 && treatGone < 0) Crumble(); };
        Present();
    }
    public bool Busy
    {
        get { if (heartEvent >= 0 || treatEvent >= 0 || starEvent >= 0) return true; foreach (var b in bits) if (b != null && b.Age < b.Life) return true; return false; }
    }
    public event Action? Started;
    public IEnumerable<(string Name, Point At, bool Placed)> Spots => [("heart", heart.At, heart.Placed), ("treat", treat.At, treat.Placed), ("wish", star.At, star.Placed)];
    public UIElement? SpotElement(string name) => name switch { "heart" => heart, "treat" => treat, "wish" => star, _ => null };

    // Positions in scene (= window) coordinates; null hides that secret (no free place for it).
    public void Place(Point? heartAt, Point? treatAt, Point? starAt)
    {
        void Put(Spot spot, Point? at)
        {
            spot.Placed = at.HasValue; spot.Visibility = at.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (at is Point p) { spot.At = p; SetLeft(spot, p.X - 13); SetTop(spot, p.Y - 13); }
        }
        Put(heart, heartAt); Put(treat, treatAt); Put(star, starAt);
    }
    public void Trigger(string name)
    {
        var world = stage.World;
        if (name == "heart" && heart.Placed && heartEvent < 0)
        {
            heartEvent = 0; var p = heart.At;
            // The fountain arcs out of the sidebar into the open scene (towards the larger free side).
            double side = p.X < world.Width * .5 ? 1 : -1;
            for (int i = 0; i < 22; i++) Bit(i % 3 == 0 ? HeartArt : i % 3 == 1 ? 1 : 3, p, side * stage.Range(50, 330), stage.Range(-340, -160), 210, stage.Range(1.8, 2.8), stage.Range(9, 16), stage.Range(-120, 120));
            for (int i = 0; i < 8; i++) Bit(GlintArt, p + new Vector(stage.Range(-14, 14), stage.Range(-14, 4)), side * stage.Range(0, 80), stage.Range(-110, -30), 0, stage.Range(.6, 1.1), stage.Range(8, 13), 60);
            world.Raise("secret-heart", p);
        }
        else if (name == "treat" && treat.Placed && treatEvent < 0 && treatGone < 0)
        {
            // With dogs around it waits for them (up to 6 s, or until "treat-eaten"); otherwise it crumbles after a short wiggle.
            treatEvent = 0; treatWait = world.Things.Exists(t => t.Kind == "dog") ? 6 : 1.4; world.Raise("secret-treat", treat.At);
        }
        else if (name == "wish" && star.Placed && starEvent < 0 && starGone < 0)
        {
            starEvent = 0; shooting = 0; shotAt = star.At;
            shotVelocity = star.At.X > world.Width * .5 ? new Vector(-640, 250) : new Vector(640, 250);
            Bit(StarArt, star.At, shotVelocity.X, shotVelocity.Y, 0, 1.15, 26, 260).Z = 0; // the falling star itself (undamped)
            world.Raise("secret-wish", star.At);
        }
        else return;
        Started?.Invoke();
    }
    void Crumble()
    {
        var p = treat.At; treatGone = 25;
        for (int i = 0; i < 14; i++) Bit(SprinkleArt + i % 4, p, stage.Range(-90, 90), stage.Range(-170, -70), 320, stage.Range(.9, 1.4), stage.Range(6, 9), stage.Range(-400, 400));
        for (int i = 0; i < 3; i++) Bit(HeartArt + i, p, stage.Range(-40, 40), stage.Range(-150, -110), 120, stage.Range(1.1, 1.5), stage.Range(6, 9), stage.Range(-90, 90));
    }
    Mote Bit(int kind, Point at, double vx, double vy, double gravity, double life, double size, double spin)
    {
        var b = bits[cursor] ??= NewBit(); cursor = (cursor + 1) % bits.Length;
        if (b.Kind != kind) { b.Kind = kind; using var dc = b.Visual.RenderOpen(); dc.DrawDrawing(ArtFor(kind)); }
        b.X = at.X; b.Y = at.Y; b.Vx = vx; b.Vy = vy; b.Sway = gravity; b.Age = 0; b.Life = life; b.Size = size; b.Spin = spin; b.Angle = stage.Range(-25, 25); b.Z = 1;
        return b;
    }
    Mote NewBit()
    {
        var b = new Mote { Kind = -1, Transform = new MatrixTransform() };
        b.Visual = new DrawingVisual { Transform = b.Transform, Opacity = 0 }; layer.Children.Add(b.Visual); return b;
    }

    public void Advance(double dt)
    {
        time += dt; heartPulse -= dt;
        var world = stage.World;
        if (heartEvent >= 0 && (heartEvent += dt) > 2.4) heartEvent = -1;
        if (treatEvent >= 0) { treatEvent += dt; if (treatEvent > treatWait && treatGone < 0) Crumble(); if (treatEvent > treatWait + 1.5) treatEvent = -1; }
        if (treatGone >= 0 && treatEvent < 0 && (treatGone -= dt) <= 0) treatGone = -1;
        if (starGone >= 0 && (starGone -= dt) <= 0) starGone = -1;
        if (shooting >= 0)
        {
            shooting += dt; shotAt += shotVelocity * dt;
            for (int k = 0; k < 3; k++) Bit(GlintArt, shotAt - shotVelocity * (stage.Next() * .03) + new Vector(stage.Range(-5, 5), stage.Range(-5, 5)), stage.Range(-20, 20) - shotVelocity.X * .05, stage.Range(-10, 25), 0, stage.Range(.45, .8), stage.Range(8, 13), 0);
            if (shooting > 1.15 || shotAt.X < -40 || shotAt.X > world.Width + 40 || shotAt.Y > world.Height)
            {
                shooting = -1; starGone = 30;
                for (int i = 0; i < 18; i++) { var at = shotAt - shotVelocity * stage.Range(0, .45) + new Vector(stage.Range(-40, 40), stage.Range(-30, 30)); Bit(i % 2 == 0 ? StarArt : GlintArt, at, stage.Range(-15, 15), stage.Range(15, 45), 16, stage.Range(1.5, 2.4), stage.Range(8, 13), stage.Range(-60, 60)); }
            }
        }
        if (starEvent >= 0 && shooting < 0 && (starEvent += dt) > 2.4) starEvent = -1;
        foreach (var b in bits)
        {
            if (b == null || b.Age >= b.Life) continue;
            b.Age += dt; b.Vy += b.Sway * dt; if (b.Z > 0) b.Vx *= 1 - .9 * dt; b.X += b.Vx * dt; b.Y += b.Vy * dt; b.Angle += b.Spin * dt;
        }
        Present();
    }
    public void Present()
    {
        // Hidden heart: a soft pulse every ~8 s; on its event it swells and glows.
        double h = heartEvent >= 0 ? Math.Sin(Math.PI * Math.Min(1, heartEvent / .9)) : heartPulse < 0 && heartPulse > -.5 ? Math.Sin(Math.PI * -heartPulse / .5) * .3 : 0;
        if (heartPulse <= -.5) heartPulse = stage.Range(7, 11);
        heart.Pose(.5 * (1 + .8 * h), 0, 0, .95, 1.3 * (.6 + h), h);
        // Cookie: still; wiggles and hops while it waits for a dog; gone (and back with a fade) after it crumbled.
        double w = treatEvent >= 0 && treatGone < 0 ? 1 : 0, back = treatGone >= 0 ? 0 : 1;
        treat.Pose(.72, w * 14 * Math.Sin(time * 22), -w * 3 * Math.Abs(Math.Sin(time * 11)), back, 1, w * .6);
        // Wishing star: a gentle twinkle; flares when clicked; gone while it is falling and for a while after.
        double tw = .5 + .5 * Math.Sin(time * 2.6), flare = starEvent >= 0 && shooting >= 0 ? 1 - shooting / 1.15 : 0;
        star.Pose(starGone >= 0 || shooting >= 0 ? .001 : .55 * (1 + .18 * tw), time * 12, 0, starGone >= 0 ? 0 : .7 + .3 * tw, 1.2 + .5 * tw, starGone >= 0 ? 0 : .25 + .3 * tw + flare);
        // The shooting star's luminous streak, aligned with its flight.
        if (shooting >= 0)
        {
            double angle = Math.Atan2(shotVelocity.Y, shotVelocity.X), c0 = Math.Cos(angle), s0 = Math.Sin(angle), len = Math.Min(1, shooting / .25);
            streakPose.Matrix = new Matrix(c0 * len, s0 * len, -s0, c0, shotAt.X, shotAt.Y); streak.Opacity = Math.Min(1, (1.15 - shooting) / .3);
        }
        else if (streak.Opacity != 0) streak.Opacity = 0;
        foreach (var b in bits)
        {
            if (b == null || b.Age >= b.Life && b.Shown == 0) continue;
            double a = b.Age < b.Life ? 1 - b.Age / b.Life : 0, k = b.Size / 20 * (b.Kind == GlintArt ? .4 + .6 * Math.Sin(Math.PI * (1 - a)) : 1);
            double r = b.Angle * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            b.Transform.Matrix = new Matrix(k * c, k * s, -k * s, k * c, b.X, b.Y);
            double o = Math.Round(Math.Min(1, a * 1.6), 2); if (o != b.Shown) { b.Visual.Opacity = o; b.Shown = o; }
        }
    }
    public void Reset()
    {
        heartEvent = treatEvent = treatGone = starEvent = starGone = shooting = -1;
        foreach (var b in bits) if (b != null) b.Age = b.Life;
        Present();
    }
}
