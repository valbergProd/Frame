using System.Windows;
using System.Windows.Media;

namespace Frame.App;

// Pink mode effects (0.6.0): hearts, flowers, sakura, sparkles, butterflies and ribbons (dogs: PinkDogsLayer). Every layer is
// driven by PinkScene from CaptureAtmosphere's clock and shares one PinkStage: the wind field (all particles drift together
// and a gust visibly sweeps every effect from the left), the heart swirl, the particle budget and the readability zone.
// Rendering: particles are DrawingVisuals holding one frozen PinkArt sprite each; a step only rewrites each sprite's
// MatrixTransform and opacity (no OnRender, no geometry rebuilds, no allocations per frame). Ribbons are the exception:
// a few twisted satin strips rebuilt into reusable StreamGeometries. Counts scale with the window area and shrink when
// many effects are on (PinkStage.Load), so all seven together stay light.

// Shared state of the Pink layers (internal to this file set; the cross-agent contract is PinkWorld).
sealed class PinkStage(PinkWorld world, PinkWind wind)
{
    public readonly PinkWorld World = world;
    public readonly PinkWind Wind = wind;
    public readonly PinkSwirl Swirl = new();
    public double Load = 1;          // particle budget per effect (1 with one or two effects, ~.6 with all)
    public double Area = 1;          // window area relative to 1280 x 800
    public Rect Calm = Rect.Empty;   // FRAME's content column: particles are dimmed there (text stays readable)
    public Rect Free = Rect.Empty;   // largest free area (right of the content, above the bottom bar)
    public Rect Sky = Rect.Empty;    // upper part of the free area
    public Vector PointerVelocity;   // DIP/s of the mouse over the window (decays), for the pointer's little wind puff
    public bool SparklesOn;          // bursts and trails only when the Sparkles effect is on
    public double Next() => World.Random.NextDouble();
    public double Range(double a, double b) => a + (b - a) * World.Random.NextDouble();
    public int Count(double basis, int min = 1) => Math.Max(min, (int)Math.Round(basis * Load * Math.Clamp(Area, .65, 1.7)));
    // .62 inside the content column, easing to 1 within 70 DIP outside it.
    public double CalmAlpha(double x, double y)
    {
        if (Calm.IsEmpty) return 1;
        double d = Math.Max(Math.Max(Calm.Left - x, x - Calm.Right), Math.Max(Calm.Top - y, y - Calm.Bottom));
        return d <= 0 ? .62 : d >= 70 ? 1 : .62 + .38 * d / 70;
    }
    // A quick mouse movement pushes nearby particles along (a small breath of wind from the pointer).
    public Vector Puff(double x, double y)
    {
        if (World.Pointer is not Point p || PointerVelocity.LengthSquared < 150 * 150) return default;
        double dx = x - p.X, dy = y - p.Y, d2 = dx * dx + dy * dy; if (d2 > 90 * 90) return default;
        double f = 1 - Math.Sqrt(d2) / 90; return PointerVelocity * (.9 * f * f);
    }
    public Point RandomIn(Rect area) => new(area.X + area.Width * Next(), area.Y + area.Height * Next());
    public static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
}

// One wind for the whole scene: a left-to-right breeze whose mean strength wanders, gusts (sin² bumps 3.5–6.5 s long every
// 7–18 s) whose front crosses the window from the left at 520 DIP/s and lifts a little, and divergence-free swirls (curl of
// three travelling waves) that drift with the flow. Deterministic for a given seed; advanced only by the scene's clock.
sealed class PinkWind
{
    public const double Base = 24, Front = 520;
    readonly double[] start = new double[4], length = new double[4], amp = new double[4];
    double nextGust;
    uint seed;
    public double Time { get; private set; }
    public double Travel { get; private set; }
    public double Mean { get; private set; } = 1;
    public PinkWind(int seed) => Reset(seed);
    public void Reset(int s)
    {
        seed = (uint)s * 2654435761u | 1; Time = Travel = 0; Mean = 1;
        for (int i = 0; i < 4; i++) { start[i] = -99; length[i] = 1; amp[i] = 0; }
        nextGust = 4 + 5 * Next();
    }
    double Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216d; }
    public void Advance(double dt)
    {
        Time += dt;
        Mean = 1 + .22 * Drift.Noise(Time * .07, 1.3) + .08 * Drift.Noise(Time * .23, 5.1);
        Travel += Base * Mean * dt;
        while (Time + 3 > nextGust)
        {
            int slot = 0; for (int i = 1; i < 4; i++) if (start[i] + length[i] < start[slot] + length[slot]) slot = i;
            start[slot] = nextGust; length[slot] = 3.5 + 3 * Next(); amp[slot] = .7 + .9 * Next();
            nextGust += 7 + 11 * Next();
        }
    }
    public double Gust(double x)
    {
        double g = 0;
        for (int i = 0; i < 4; i++) { double s = (Time - x / Front - start[i]) / length[i]; if (s > 0 && s < 1) { double b = Math.Sin(Math.PI * s); g += amp[i] * b * b; } }
        return g;
    }
    public Vector At(double x, double y)
    {
        double g = Gust(x), s = Mean + g, X = x - .5 * Travel, t = Time;
        double c1 = Math.Cos(.012 * X + .016 * y + .3 * t + .7), c2 = Math.Cos(-.027 * X + .031 * y - .62 * t + 2.3), c3 = Math.Cos(.051 * X - .046 * y + 1.1 * t + 4.1);
        double u = 420 * .016 * c1 + 220 * .031 * c2 - 90 * .046 * c3, v = -(420 * .012 * c1 - 220 * .027 * c2 + 90 * .051 * c3), k = .75 + .35 * g;
        return new Vector(Base * s + u * k, Base * s * .1 + v * k - 9 * g);
    }
}

// Secret combination of Flowers + Hearts: every 1–2 minutes some flowers and hearts gather into a heart-shaped figure for a
// few seconds (alternating flower, heart, flower…), travel slowly round its outline while it beats, then the wind takes
// them again. PinkScene starts it; the two layers pick their nearest particles when Version changes.
sealed class PinkSwirl
{
    public const double Gather = 2.4, Hold = 3.8, Release = 1.8, Length = Gather + Hold + Release;
    public bool Active { get; private set; }
    public double T { get; private set; }
    public Point Centre { get; private set; }
    public double Radius { get; private set; } = 90;
    public int PerLayer { get; private set; }  // participants per layer (slots = 2 × PerLayer)
    public int Version { get; private set; }
    public double NextAt = -1;                 // scene time of the next swirl (-1: not scheduled)
    public void Start(Point centre, double radius, int perLayer) { Active = true; T = 0; Centre = centre; Radius = radius; PerLayer = perLayer; Version++; }
    public void Stop() { Active = false; T = 0; }
    public void Advance(double dt) { if (Active && (T += dt) >= Length) Active = false; }
    public double Pull => !Active ? 0 : T < Gather ? PinkStage.Ease(T / Gather) : T < Gather + Hold ? 1 : 1 - PinkStage.Ease((T - Gather - Hold) / Release);
    public bool Holding => Active && T >= Gather && T < Gather + Hold;
    // Point of slot i on the heart curve x = 16 sin³a, y = 13 cos a − 5 cos 2a − 2 cos 3a − cos 4a (screen y down), with a
    // slow travel along the outline and a soft double beat.
    public Point Slot(int i)
    {
        int n = Math.Max(2, PerLayer * 2); var c = Curve(2 * Math.PI * i / n + .3 * T) * (Radius * Scale);
        return new Point(Centre.X + c.X, Centre.Y + c.Y);
    }
    public static Vector Curve(double a)
    {
        double s = Math.Sin(a);
        return new Vector(s * s * s, (-(13 * Math.Cos(a) - 5 * Math.Cos(2 * a) - 2 * Math.Cos(3 * a) - Math.Cos(4 * a)) - 2.5) / 16);
    }
    // 1 plus a soft double heartbeat while the figure is held.
    public double Scale { get { double p = T % 1.15; return 1 + (Holding ? .05 * (Math.Exp(-Math.Pow((p - .12) / .06, 2)) + .6 * Math.Exp(-Math.Pow((p - .36) / .07, 2))) : 0); } }
}

// A particle drawn as one sprite: position/velocity, depth, rotation, flip phase, life; its visual and published thing.
sealed class Mote
{
    public double X, Y, Vx, Vy, Z = 1, Angle, Spin, Phase, Rate, Age, Life, Size, Sway, Beat, Wait;
    public int Kind, Slot = -1;
    public bool Retire, Edge;
    public DrawingVisual Visual = null!;
    public MatrixTransform Transform = null!;
    public double Shown = -1;
    public PinkThing Thing = null!;
}

// Base of the sprite layers: a FrameworkElement hosting DrawingVisuals (never hit-testable). Enabled = false removes every
// visual and published thing (nothing is kept running or allocated while off).
abstract class SpriteLayer : FrameworkElement, IPinkLayer
{
    protected readonly PinkStage stage;
    readonly VisualCollection children;
    bool enabled;
    protected bool seeded;
    protected SpriteLayer(PinkStage stage) { this.stage = stage; children = new VisualCollection(this); IsHitTestVisible = false; Visibility = Visibility.Collapsed; }
    public abstract string Key { get; }      // PinkOptions.Effects key (UIElement.Effect is taken)
    string IPinkLayer.Effect => Key;
    public virtual bool FullRate => false;
    public FrameworkElement Visual => this;
    public bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; Visibility = value ? Visibility.Visible : Visibility.Collapsed; if (!value) Reset(); }
    }
    protected override int VisualChildrenCount => children.Count;
    protected override Visual GetVisualChild(int index) => children[index];
    protected void Add(Visual v) => children.Add(v);
    protected void Remove(Visual v) => children.Remove(v);
    protected virtual void Reset() { children.Clear(); seeded = false; stage.World.Things.RemoveAll(t => t.Owner == this); }
    public void Advance(double dt, PinkWorld world)
    {
        if (!enabled || world.Width <= 0) return;
        if (!seeded) { Seed(world); seeded = true; }
        Step(dt, world); Show(world);
    }
    public void Present(PinkWorld world)
    {
        if (!enabled || world.Width <= 0) return;
        if (!seeded) { Seed(world); seeded = true; }
        Show(world);
    }
    public virtual void Rebalance() { }
    protected abstract void Seed(PinkWorld world);
    protected abstract void Step(double dt, PinkWorld world);
    protected abstract void Show(PinkWorld world);

    protected static DrawingVisual Sprite(Drawing drawing, out MatrixTransform transform)
    {
        transform = new MatrixTransform(); var v = new DrawingVisual { Transform = transform };
        using (var dc = v.RenderOpen()) dc.DrawDrawing(drawing);
        return v;
    }
    protected static void Redraw(DrawingVisual v, Drawing drawing) { using var dc = v.RenderOpen(); dc.DrawDrawing(drawing); }
    // Sprite pose: scale (sx along the sprite's own axis: a flip), rotation, position; opacity only when it changed.
    protected static void Pose(Mote m, double sx, double sy, double degrees, double opacity, double dx = 0, double dy = 0)
    {
        double r = degrees * (Math.PI / 180), c = Math.Cos(r), s = Math.Sin(r);
        m.Transform.Matrix = new Matrix(sx * c, sx * s, -sy * s, sy * c, m.X + dx, m.Y + dy);
        opacity = Math.Round(opacity, 2);
        if (opacity != m.Shown) { m.Visual.Opacity = opacity; m.Shown = opacity; }
    }
    protected PinkThing Publish(string kind, Mote m, double radius)
    {
        var thing = new PinkThing { Kind = kind, Owner = this, Size = radius, Position = new Point(m.X, m.Y) };
        stage.World.Things.Add(thing); return thing;
    }
}

// Drifting motes (sakura petals, flowers): a pool that follows the particle budget; extra motes fade out instead of popping.
abstract class DriftLayer(PinkStage stage) : SpriteLayer(stage)
{
    protected readonly List<Mote> motes = [];
    protected abstract double Basis { get; }
    protected abstract string ThingKind { get; }
    protected abstract Drawing Art(int kind);
    protected abstract void Spawn(Mote m, PinkWorld world, bool edge);
    protected override void Reset() { base.Reset(); motes.Clear(); }
    protected Mote Create(PinkWorld world, bool edge)
    {
        var m = new Mote(); Spawn(m, world, edge);
        m.Visual = Sprite(Art(m.Kind), out m.Transform); Add(m.Visual);
        m.Thing = Publish(ThingKind, m, m.Size * .5); motes.Add(m); return m;
    }
    protected void Respawn(Mote m, PinkWorld world, bool edge)
    {
        int kind = m.Kind; Spawn(m, world, edge); m.Slot = -1; m.Retire = false;
        if (m.Kind != kind) Redraw(m.Visual, Art(m.Kind));
        m.Thing.Claimed = null; m.Thing.Landed = false; m.Thing.Remove = false; m.Thing.Size = m.Size * .5;
    }
    protected override void Seed(PinkWorld world) { int n = stage.Count(Basis, 3); for (int i = 0; i < n; i++) Create(world, edge: false); }
    public override void Rebalance()
    {
        if (!seeded) return;
        int target = stage.Count(Basis, 3), live = motes.Count(m => !m.Retire);
        for (int i = live; i < target; i++) Create(stage.World, edge: true);
        for (int i = motes.Count - 1; i >= 0 && live > target; i--) if (!motes[i].Retire) { motes[i].Retire = true; motes[i].Life = motes[i].Age + 1; live--; }
    }
    // Fade in after an interior spawn, out at the end of life (always out when retired).
    protected static double Fade(Mote m) => (m.Edge ? 1 : PinkStage.Ease(m.Age / .8)) * PinkStage.Ease((m.Life - m.Age) / 1.1);
    protected void Expire(int i, PinkWorld world)
    {
        var m = motes[i];
        if (m.Retire) { Remove(m.Visual); world.Things.Remove(m.Thing); motes.RemoveAt(i); }
        else Respawn(m, world, edge: true);
    }
    protected static bool Outside(Mote m, PinkWorld world, double margin) => m.X > world.Width + margin || m.Y > world.Height + margin || m.X < -margin * 3 || m.Y < -margin * 4;
}

// Sakura: illustrated cherry petals in depth (size, speed and opacity by depth). Falling-leaf motion: a petal turns over and
// over (its width follows cos of the flip phase, the sheen shows which side is up), glides sideways while it is broadside
// and drops faster edge-on, spins, and is carried by the shared wind (gusts sweep the petals together).
sealed class SakuraLayer(PinkStage stage) : DriftLayer(stage)
{
    public override string Key => "sakura";
    protected override double Basis => 64;
    protected override string ThingKind => "petal";
    protected override Drawing Art(int kind) => PinkArt.Sakura[kind];
    protected override void Spawn(Mote m, PinkWorld world, bool edge)
    {
        double w = world.Width, h = world.Height, r = stage.Next();
        m.Z = stage.Range(.55, 1.25); m.Size = stage.Range(9, 13) * m.Z; m.Kind = r < .3 ? 0 : r < .6 ? 1 : r < .8 ? 2 : 3;
        if (edge) { if (stage.Next() < .68) { m.X = stage.Range(-.15 * w, w * .95); m.Y = -14 - 30 * stage.Next(); } else { m.X = -16; m.Y = stage.Range(-.05 * h, .8 * h); } }
        else { m.X = stage.Range(-.05 * w, w); m.Y = stage.Range(-.05 * h, h); }
        m.Rate = stage.Range(2.2, 4.6) * (stage.Next() < .5 ? -1 : 1); m.Phase = stage.Range(0, 6.3); m.Spin = stage.Range(-80, 80); m.Angle = stage.Range(0, 360); m.Sway = stage.Range(0, 6.3);
        m.Age = edge ? 0 : stage.Range(0, 8); m.Life = m.Age + stage.Range(16, 30); m.Edge = edge;
        var wind = world.Wind(new Point(m.X, m.Y), world.Time); m.Vx = wind.X * m.Z; m.Vy = wind.Y + 20 * m.Z;
    }
    protected override void Step(double dt, PinkWorld world)
    {
        double relax = 1 - Math.Exp(-dt / .35);
        for (int i = motes.Count - 1; i >= 0; i--)
        {
            var m = motes[i]; m.Age += dt; m.Phase += m.Rate * dt;
            var wind = world.Wind(new Point(m.X, m.Y), world.Time);
            double flip = Math.Cos(m.Phase), broad = Math.Abs(flip), depth = .55 + .45 * m.Z;
            double tx = wind.X * depth + 16 * Math.Sin(m.Phase) * m.Z, ty = wind.Y * depth + (12 + 24 * (1 - broad)) * m.Z + 5 * Math.Sin(m.Phase * .5 + m.Sway);
            m.Vx += (tx - m.Vx) * relax; m.Vy += (ty - m.Vy) * relax;
            var puff = stage.Puff(m.X, m.Y); m.Vx += puff.X * dt * 3; m.Vy += puff.Y * dt * 3;
            m.X += m.Vx * dt; m.Y += m.Vy * dt; m.Angle += (m.Spin + 30 * Math.Sin(m.Phase)) * dt;
            m.Thing.Position = new Point(m.X, m.Y); m.Thing.Velocity = new Vector(m.Vx, m.Vy);
            if (m.Age >= m.Life || m.Thing.Remove || Outside(m, world, 24)) Expire(i, world);
        }
    }
    protected override void Show(PinkWorld world)
    {
        foreach (var m in motes)
        {
            double flip = Math.Cos(m.Phase), k = m.Size / 20;
            Pose(m, k * (Math.Abs(flip) < .06 ? .06 * Math.Sign(flip + 1e-9) : flip), k * (.92 + .08 * Math.Abs(flip)), m.Angle, Fade(m) * (.5 + .5 * Math.Min(1, m.Z)) * stage.CalmAlpha(m.X, m.Y));
        }
    }
}

// Flowers: mostly pink blossoms, roses and loose petals, now and then a red one. Heavier than sakura: they drift down
// slowly with the shared wind, turn, and loose petals tumble. Butterflies like to land on them; with Hearts on they join
// the heart swirl.
sealed class FlowersLayer(PinkStage stage) : DriftLayer(stage)
{
    int version;
    public override string Key => "flowers";
    protected override double Basis => 16;
    protected override string ThingKind => "flower";
    protected override Drawing Art(int kind) => PinkArt.Flowers[kind];
    public int Available => motes.Count(m => !m.Retire);
    protected override void Spawn(Mote m, PinkWorld world, bool edge)
    {
        double w = world.Width, h = world.Height, r = stage.Next();
        // ~18 % red: red blossom, red petal or red rose; the rest pink (blossoms most often).
        m.Kind = r < .16 ? 0 : r < .29 ? 1 : r < .39 ? 2 : r < .56 ? 4 : r < .66 ? 6 : r < .73 ? 3 : r < .82 ? 5 : r < .88 ? 7 : 0;
        bool petal = m.Kind is 4 or 5; m.Z = stage.Range(.8, 1.15); m.Size = (petal ? stage.Range(13, 17) : stage.Range(17, 25)) * m.Z;
        if (edge) { if (stage.Next() < .7) { m.X = stage.Range(-.1 * w, w * .9); m.Y = -24 - 30 * stage.Next(); } else { m.X = -24; m.Y = stage.Range(0, .7 * h); } }
        else { m.X = stage.Range(0, w); m.Y = stage.Range(0, h * .95); }
        m.Rate = stage.Range(1.2, 2.6) * (stage.Next() < .5 ? -1 : 1); m.Phase = stage.Range(0, 6.3); m.Spin = stage.Range(-45, 45); m.Angle = stage.Range(0, 360); m.Sway = stage.Range(0, 6.3);
        m.Age = edge ? 0 : stage.Range(0, 6); m.Life = m.Age + stage.Range(28, 46); m.Edge = edge;
        var wind = world.Wind(new Point(m.X, m.Y), world.Time); m.Vx = wind.X * .8; m.Vy = wind.Y + 14;
    }
    protected override void Step(double dt, PinkWorld world)
    {
        double relax = 1 - Math.Exp(-dt / .7);
        var swirl = stage.Swirl;
        if (swirl.Version != version) { version = swirl.Version; Join(swirl, 0); }
        double pull = swirl.Pull, hold = 1 - Math.Exp(-dt * 7 * pull);
        for (int i = motes.Count - 1; i >= 0; i--)
        {
            var m = motes[i]; m.Age += dt; m.Phase += m.Rate * dt;
            var wind = world.Wind(new Point(m.X, m.Y), world.Time);
            double tx = wind.X * .85 + 8 * Math.Sin(m.Phase), ty = wind.Y * .7 + 15 * m.Z + 6 * Math.Sin(m.Phase * .7 + m.Sway);
            m.Vx += (tx - m.Vx) * relax; m.Vy += (ty - m.Vy) * relax;
            var puff = stage.Puff(m.X, m.Y); m.Vx += puff.X * dt * 2; m.Vy += puff.Y * dt * 2;
            m.X += m.Vx * dt; m.Y += m.Vy * dt; m.Angle += m.Spin * dt;
            if (m.Slot >= 0)
            {
                if (!swirl.Active) { m.Slot = -1; m.Life = Math.Max(m.Life, m.Age + 12); }
                else
                {
                    var at = swirl.Slot(m.Slot); m.X += (at.X - m.X) * hold; m.Y += (at.Y - m.Y) * hold;
                    if (swirl.T >= PinkSwirl.Gather + PinkSwirl.Hold && swirl.T - dt < PinkSwirl.Gather + PinkSwirl.Hold) { var o = new Vector(m.X - swirl.Centre.X, m.Y - swirl.Centre.Y); if (o.Length > 1) { o.Normalize(); m.Vx += o.X * 55; m.Vy += o.Y * 55; } }
                }
            }
            m.Thing.Position = new Point(m.X, m.Y); m.Thing.Velocity = new Vector(m.Vx, m.Vy);
            if (m.Slot < 0 && (m.Age >= m.Life || m.Thing.Remove || Outside(m, world, 30))) Expire(i, world);
        }
    }
    // Take the n motes nearest to the swirl's centre (even slots: flower, heart, flower, …).
    void Join(PinkSwirl swirl, int parity)
    {
        foreach (var m in motes) m.Slot = -1;
        if (!swirl.Active) return;
        var chosen = motes.Where(m => !m.Retire).OrderBy(m => (m.X - swirl.Centre.X) * (m.X - swirl.Centre.X) + (m.Y - swirl.Centre.Y) * (m.Y - swirl.Centre.Y)).Take(swirl.PerLayer).ToList();
        for (int k = 0; k < chosen.Count; k++) { chosen[k].Slot = 2 * k + parity; chosen[k].Life = Math.Max(chosen[k].Life, chosen[k].Age + PinkSwirl.Length + 4); }
    }
    protected override void Show(PinkWorld world)
    {
        foreach (var m in motes)
        {
            double k = m.Size / 20, c = Math.Cos(m.Phase), sx, sy;
            if (m.Kind is 4 or 5) { sx = k * (Math.Abs(c) < .08 ? .08 * Math.Sign(c + 1e-9) : c); sy = k; } // loose petals tumble
            else { sx = k; sy = k * (.8 + .2 * c); }                                                       // blossoms tilt
            Pose(m, sx, sy, m.Angle, Fade(m) * stage.CalmAlpha(m.X, m.Y));
        }
    }
}

// Hearts: buoyant little hearts carried up and along by the wind, bobbing and wobbling, with an occasional double heartbeat.
// A dog that catches one (signal "heart-caught" with the heart's thing) pops it into a burst of tiny hearts (and sparkles
// when Sparkles is on); with Flowers on they join the heart swirl.
sealed class HeartsLayer : SpriteLayer
{
    readonly List<Mote> hearts = [];
    readonly Mote[] bits = new Mote[18];
    int version, nextBit;
    public HeartsLayer(PinkStage stage) : base(stage) => stage.World.Signal += Signal;
    public override string Key => "hearts";
    public int Available => hearts.Count(h => !h.Retire && h.Wait <= 0);
    protected override void Reset() { base.Reset(); hearts.Clear(); Array.Clear(bits); nextBit = 0; }
    void Spawn(Mote m, PinkWorld world, bool edge)
    {
        double w = world.Width, h = world.Height, r = stage.Next();
        m.Kind = r < .36 ? 0 : r < .66 ? 1 : r < .82 ? 2 : 3; m.Z = stage.Range(.75, 1.15); m.Size = stage.Range(16, 24) * m.Z;
        if (edge) { if (stage.Next() < .62) { m.X = stage.Range(-.05 * w, .85 * w); m.Y = h + 22; } else { m.X = -22; m.Y = stage.Range(.3 * h, h); } }
        else { m.X = stage.Range(0, w); m.Y = stage.Range(.1 * h, h); }
        m.Phase = stage.Range(0, 6.3); m.Sway = stage.Range(.25, .45); m.Rate = stage.Range(18, 30); m.Beat = stage.Range(1, 5);
        m.Age = edge ? 0 : stage.Range(0, 6); m.Life = m.Age + stage.Range(18, 30); m.Edge = edge; m.Slot = -1; m.Retire = false; m.Wait = 0;
        var wind = world.Wind(new Point(m.X, m.Y), world.Time); m.Vx = wind.X * .7; m.Vy = -m.Rate;
    }
    Mote Create(PinkWorld world, bool edge)
    {
        var m = new Mote(); Spawn(m, world, edge);
        m.Visual = Sprite(PinkArt.Hearts[m.Kind], out m.Transform); Add(m.Visual);
        m.Thing = Publish("heart", m, m.Size * .5); hearts.Add(m); return m;
    }
    // A popped or finished heart comes back as a new thing (a dog's reference to the old one is not moved elsewhere).
    void Renew(Mote m, PinkWorld world, double wait)
    {
        int kind = m.Kind; world.Things.Remove(m.Thing); Spawn(m, world, edge: true); m.Wait = wait;
        if (m.Kind != kind) Redraw(m.Visual, PinkArt.Hearts[m.Kind]);
        m.Thing = Publish("heart", m, m.Size * .5);
    }
    protected override void Seed(PinkWorld world) { int n = stage.Count(12, 3); for (int i = 0; i < n; i++) Create(world, edge: false); }
    public override void Rebalance()
    {
        if (!seeded) return;
        int target = stage.Count(12, 3), live = hearts.Count(m => !m.Retire);
        for (int i = live; i < target; i++) Create(stage.World, edge: true);
        for (int i = hearts.Count - 1; i >= 0 && live > target; i--) if (!hearts[i].Retire) { hearts[i].Retire = true; hearts[i].Life = hearts[i].Age + 1; live--; }
    }
    void Signal(string name, Point at, PinkThing? thing)
    {
        if (!Enabled || name != "heart-caught" || thing?.Owner != this) return;
        var m = hearts.FirstOrDefault(h => h.Thing == thing); if (m != null && m.Wait <= 0) Pop(m, stage.World);
    }
    void Pop(Mote m, PinkWorld world)
    {
        var at = new Point(m.X, m.Y);
        for (int k = 0; k < 7; k++)
        {
            var b = bits[nextBit] ??= NewBit(); nextBit = (nextBit + 1) % bits.Length;
            double a = k * Math.PI * 2 / 7 + stage.Range(-.3, .3), v = stage.Range(55, 95);
            b.X = at.X; b.Y = at.Y; b.Vx = Math.Cos(a) * v; b.Vy = Math.Sin(a) * v - 30; b.Age = 0; b.Life = stage.Range(.7, 1.1); b.Size = stage.Range(5, 8); b.Angle = stage.Range(-30, 30); b.Spin = stage.Range(-120, 120);
            if (b.Kind != m.Kind) { b.Kind = m.Kind; Redraw(b.Visual, PinkArt.Hearts[m.Kind]); }
        }
        world.Raise("heart-popped", at, m.Thing);
        if (stage.SparklesOn) world.Raise("sparkle-burst", at);
        m.Slot = -1;
        if (m.Retire) { m.Life = m.Age; m.Wait = 0; } else Renew(m, world, wait: stage.Range(1.2, 2.5));
    }
    Mote NewBit() { var b = new Mote { Life = 0, Kind = 0 }; b.Visual = Sprite(PinkArt.Hearts[0], out b.Transform); b.Visual.Opacity = 0; b.Shown = 0; Add(b.Visual); return b; }
    protected override void Step(double dt, PinkWorld world)
    {
        var swirl = stage.Swirl;
        if (swirl.Version != version) { version = swirl.Version; Join(swirl); }
        double relax = 1 - Math.Exp(-dt / .9), hold = 1 - Math.Exp(-dt * 7 * swirl.Pull), t = world.Time;
        for (int i = hearts.Count - 1; i >= 0; i--)
        {
            var m = hearts[i];
            if (m.Wait > 0) { m.Wait -= dt; continue; }
            if (m.Thing.Remove) { Pop(m, world); continue; }
            m.Age += dt; m.Beat -= dt;
            var wind = world.Wind(new Point(m.X, m.Y), t);
            double tx = wind.X * .8, ty = wind.Y * .5 - m.Rate + 8 * Math.Sin(2 * Math.PI * m.Sway * t + m.Phase);
            m.Vx += (tx - m.Vx) * relax; m.Vy += (ty - m.Vy) * relax;
            var puff = stage.Puff(m.X, m.Y); m.Vx += puff.X * dt * 2; m.Vy += puff.Y * dt * 2;
            m.X += m.Vx * dt; m.Y += m.Vy * dt;
            if (m.Slot >= 0)
            {
                if (!swirl.Active) { m.Slot = -1; m.Life = Math.Max(m.Life, m.Age + 10); }
                else
                {
                    var at = swirl.Slot(m.Slot); m.X += (at.X - m.X) * hold; m.Y += (at.Y - m.Y) * hold;
                    if (swirl.T >= PinkSwirl.Gather + PinkSwirl.Hold && swirl.T - dt < PinkSwirl.Gather + PinkSwirl.Hold) { var o = new Vector(m.X - swirl.Centre.X, m.Y - swirl.Centre.Y); if (o.Length > 1) { o.Normalize(); m.Vx += o.X * 55; m.Vy += o.Y * 55; } }
                }
            }
            if (m.Beat < -.55) m.Beat = stage.Range(2.5, 6);
            m.Thing.Position = new Point(m.X, m.Y); m.Thing.Velocity = new Vector(m.Vx, m.Vy);
            if (m.Slot < 0 && (m.Age >= m.Life || m.Y < -30 || m.X > world.Width + 30 || m.X < -60 || m.Y > world.Height + 60))
            {
                if (m.Retire) { Remove(m.Visual); world.Things.Remove(m.Thing); hearts.RemoveAt(i); }
                else Renew(m, world, 0);
            }
        }
        foreach (var b in bits)
        {
            if (b == null || b.Age >= b.Life) continue;
            b.Age += dt; b.Vy += 140 * dt; b.Vx *= 1 - 1.2 * dt; b.X += b.Vx * dt; b.Y += b.Vy * dt; b.Angle += b.Spin * dt;
        }
    }
    void Join(PinkSwirl swirl)
    {
        foreach (var m in hearts) m.Slot = -1;
        if (!swirl.Active) return;
        var chosen = hearts.Where(m => !m.Retire && m.Wait <= 0).OrderBy(m => (m.X - swirl.Centre.X) * (m.X - swirl.Centre.X) + (m.Y - swirl.Centre.Y) * (m.Y - swirl.Centre.Y)).Take(swirl.PerLayer).ToList();
        for (int k = 0; k < chosen.Count; k++) { chosen[k].Slot = 2 * k + 1; chosen[k].Life = Math.Max(chosen[k].Life, chosen[k].Age + PinkSwirl.Length + 4); }
    }
    protected override void Show(PinkWorld world)
    {
        double t = world.Time;
        foreach (var m in hearts)
        {
            if (m.Wait > 0) { Pose(m, 0, 0, 0, 0); continue; }
            double beat = m.Beat < 0 ? .14 * Math.Exp(-Math.Pow((-m.Beat - .08) / .06, 2)) + .09 * Math.Exp(-Math.Pow((-m.Beat - .3) / .07, 2)) : 0;
            double k = m.Size / 20 * (1 + beat), top = Math.Clamp((m.Y + 10) / 70, 0, 1);
            double fade = (m.Edge ? 1 : PinkStage.Ease(m.Age / .8)) * PinkStage.Ease((m.Life - m.Age) / 1.2) * (m.Slot >= 0 ? 1 : top);
            Pose(m, k, k, 11 * Math.Sin(.9 * t + m.Phase) + Math.Clamp(m.Vx * .12, -12, 12), fade * stage.CalmAlpha(m.X, m.Y) * .96);
        }
        foreach (var b in bits)
        {
            if (b == null || b.Age >= b.Life && b.Shown == 0) continue;
            double a = b.Life > 0 ? Math.Clamp(1 - b.Age / b.Life, 0, 1) : 0, k = b.Size / 20 * (.6 + .4 * a);
            Pose(b, k, k, b.Angle, a * a);
        }
    }
}

// Sparkles: small elegant pink glints (four-point and tiny five-point stars) that bloom and fade where the scene is free,
// on petals, hearts and flowers, behind flying butterflies (a trail of magic dust), round the heart swirl, and in bursts
// (signal "sparkle-burst": popped hearts, secrets).
sealed class SparklesLayer : SpriteLayer
{
    readonly Mote[] glints = new Mote[56];
    int ambient, cursor;
    public SparklesLayer(PinkStage stage) : base(stage) => stage.World.Signal += Signal;
    public override string Key => "sparkles";
    protected override void Reset() { base.Reset(); Array.Clear(glints); cursor = 0; }
    protected override void Seed(PinkWorld world)
    {
        for (int i = 0; i < glints.Length; i++) { var g = new Mote(); g.Visual = Sprite(PinkArt.Sparkles[0], out g.Transform); g.Visual.Opacity = 0; g.Shown = 0; Add(g.Visual); glints[i] = g; }
        ambient = Math.Min(30, stage.Count(24, 6));
        for (int i = 0; i < ambient; i++) { Ambient(glints[i], world); glints[i].Age = stage.Range(0, glints[i].Life); }
    }
    public override void Rebalance() { if (seeded) ambient = Math.Min(30, stage.Count(24, 6)); }
    void Look(Mote g, int kind) { if (g.Kind != kind || g.Shown < 0) { g.Kind = kind; Redraw(g.Visual, PinkArt.Sparkles[kind]); } }
    void Ambient(Mote g, PinkWorld world)
    {
        double r = stage.Next(); Point p;
        if (r < .45 && !stage.Free.IsEmpty) p = stage.RandomIn(stage.Free);
        else if (r < .72 && world.Things.Count > 0) { var t = world.Things[(int)(stage.Next() * world.Things.Count)]; p = t.Kind is "petal" or "flower" or "heart" or "ribbon" ? t.Position + new Vector(stage.Range(-10, 10), stage.Range(-10, 10)) : stage.RandomIn(new Rect(0, 40, world.Width, world.Height - 40)); }
        else p = stage.RandomIn(new Rect(0, 40, world.Width, world.Height - 40));
        double k = stage.Next(); Look(g, k < .5 ? 0 : k < .68 ? 1 : k < .84 ? 2 : 3);
        g.X = p.X; g.Y = p.Y; g.Vx = g.Vy = 0; g.Age = 0; g.Life = stage.Range(.8, 1.7); g.Size = stage.Range(9, 16); g.Angle = stage.Range(-20, 20); g.Spin = stage.Range(-30, 30); g.Phase = stage.Range(0, 6.3); g.Z = 1;
    }
    // Short-lived extra glints (trails, bursts, swirl) use the slots after the ambient ones, round-robin.
    Mote Extra() { int span = glints.Length - 30; var g = glints[30 + cursor]; cursor = (cursor + 1) % span; return g; }
    public void Burst(Point at, int count, double speed)
    {
        if (!Enabled || !seeded) return;
        for (int i = 0; i < count; i++)
        {
            var g = Extra(); double a = stage.Range(0, Math.PI * 2), v = stage.Range(.4, 1) * speed;
            Look(g, stage.Next() < .7 ? 0 : 3); g.X = at.X; g.Y = at.Y; g.Vx = Math.Cos(a) * v; g.Vy = Math.Sin(a) * v; g.Age = 0; g.Life = stage.Range(.55, 1.1); g.Size = stage.Range(5, 10); g.Angle = 0; g.Spin = stage.Range(-90, 90); g.Phase = 0; g.Z = 2;
        }
    }
    void Signal(string name, Point at, PinkThing? thing) { if (name == "sparkle-burst") Burst(at, 10, 85); }
    void Trail(Point at, double size)
    {
        var g = Extra(); Look(g, stage.Next() < .75 ? 0 : 2);
        g.X = at.X + stage.Range(-3, 3); g.Y = at.Y + stage.Range(-3, 3); g.Vx = stage.Range(-6, 6); g.Vy = stage.Range(4, 14); g.Age = 0; g.Life = stage.Range(.45, .8); g.Size = size; g.Angle = 0; g.Spin = 0; g.Phase = 0; g.Z = 2;
    }
    protected override void Step(double dt, PinkWorld world)
    {
        for (int i = 0; i < glints.Length; i++)
        {
            var g = glints[i];
            if (g.Age >= g.Life) { if (i < ambient) Ambient(g, world); continue; }
            g.Age += dt;
            var wind = world.Wind(new Point(g.X, g.Y), world.Time);
            double drag = g.Z > 1 ? 1 - 2.2 * dt : 1;
            g.Vx = g.Vx * drag + (g.Z > 1 ? 0 : (wind.X * .22 - g.Vx) * .1); g.Vy = g.Vy * drag + (g.Z > 1 ? 0 : (-4 - g.Vy) * .1);
            g.X += g.Vx * dt; g.Y += g.Vy * dt; g.Angle += g.Spin * dt;
        }
        // Magic dust behind flying butterflies, glints round the heart swirl.
        foreach (var t in world.Things)
            if (t.Kind == "butterfly" && !t.Landed && stage.Next() < dt * 2.6) Trail(t.Position, stage.Range(3.5, 6));
        if (stage.Swirl.Holding && stage.Next() < dt * 16) Trail(stage.Swirl.Slot((int)(stage.Next() * stage.Swirl.PerLayer * 2)), stage.Range(6, 10));
    }
    protected override void Show(PinkWorld world)
    {
        foreach (var g in glints)
        {
            if (g.Age >= g.Life) { if (g.Shown != 0) Pose(g, 0, 0, 0, 0); continue; }
            double a = g.Age / g.Life, env = Math.Pow(Math.Sin(Math.PI * a), 1.4), twinkle = 1 + .22 * Math.Sin(a * 19 + g.Phase);
            double k = g.Size / 20 * (.35 + .65 * env) * twinkle;
            Pose(g, k, k, g.Angle, env * (g.Z > 1 ? 1 : .9 * stage.CalmAlpha(g.X, g.Y)));
        }
    }
}

// Butterflies (full rate: wing beats need every frame): stylized pink butterflies wander with smooth turns, flap (with
// short glides), and now and then land and rest with slowly opening wings: on a flower when Flowers is on (riding it on
// the wind), otherwise on the ground line in a free spot. They flee from nearby dogs (and a dog's bark), shy away from a
// fast pointer, and flutter towards the hidden heart when it is found.
sealed class ButterfliesLayer : SpriteLayer
{
    enum Mode { Wander, Approach, Rest, Flee }
    sealed class Fly
    {
        public double X, Y, Vx, Vy, Heading, Flap, Open = 1, Size, Timer, Glide, Speed, Bob, Fade;
        public Mode Mode; public Point Target; public PinkThing? Perch; public Vector Offset; public bool Retire;
        public ContainerVisual Root = null!; public MatrixTransform Transform = null!; public ScaleTransform Left = null!, Right = null!; public double Shown = -1;
        public PinkThing Thing = null!; public int Variant;
    }
    readonly List<Fly> flies = [];
    public ButterfliesLayer(PinkStage stage) : base(stage) => stage.World.Signal += Signal;
    public override string Key => "butterflies";
    public override bool FullRate => true;
    protected override void Reset() { base.Reset(); flies.Clear(); }
    int Target => stage.Count(4.2, 2);
    protected override void Seed(PinkWorld world) { for (int i = 0; i < Target; i++) Create(world, inside: true); }
    public override void Rebalance()
    {
        if (!seeded) return;
        int live = flies.Count(f => !f.Retire), target = Target;
        for (int i = live; i < target; i++) Create(stage.World, inside: false);
        for (int i = flies.Count - 1; i >= 0 && live > target; i--) if (!flies[i].Retire) { flies[i].Retire = true; live--; Leave(flies[i], stage.World); }
    }
    void Create(PinkWorld world, bool inside)
    {
        var f = new Fly { Variant = flies.Count % PinkArt.Butterflies.Length, Size = stage.Range(.8, 1.1), Speed = stage.Range(38, 58), Flap = stage.Range(0, 6.3) };
        var art = PinkArt.Butterflies[f.Variant];
        f.Root = new ContainerVisual { Transform = f.Transform = new MatrixTransform() };
        DrawingVisual Part(Drawing d, Transform? t) { var v = new DrawingVisual { Transform = t }; using (var dc = v.RenderOpen()) dc.DrawDrawing(d); return v; }
        f.Root.Children.Add(Part(art.Left, f.Left = new ScaleTransform())); f.Root.Children.Add(Part(art.Right, f.Right = new ScaleTransform())); f.Root.Children.Add(Part(art.Body, null));
        Add(f.Root);
        if (inside) { var p = stage.RandomIn(Zone(world)); f.X = p.X; f.Y = p.Y; f.Fade = 1; }
        else { bool left = stage.Next() < .6; f.X = left ? -30 : world.Width + 30; f.Y = stage.Range(.15, .6) * world.Height; f.Fade = 1; }
        f.Heading = f.X < world.Width / 2 ? 90 : -90; f.Target = stage.RandomIn(Zone(world)); f.Timer = stage.Range(3, 6);
        f.Thing = new PinkThing { Kind = "butterfly", Owner = this, Size = 12 * f.Size, Position = new Point(f.X, f.Y) }; world.Things.Add(f.Thing);
        flies.Add(f);
    }
    // Where butterflies like to fly: the free area right of the content, the sky above it, sometimes anywhere.
    Rect Zone(PinkWorld world)
    {
        double r = stage.Next(), ground = world.GroundY > 0 ? world.GroundY : world.Height * .85;
        if (r < .5 && stage.Free.Width > 120 && stage.Free.Height > 120) return stage.Free;
        if (r < .7 && stage.Sky.Width > 120 && stage.Sky.Height > 60) return stage.Sky;
        return new Rect(40, 70, Math.Max(10, world.Width - 80), Math.Max(10, ground - 110));
    }
    void Leave(Fly f, PinkWorld world) { Unperch(f); f.Mode = Mode.Wander; f.Target = new Point(f.X < world.Width / 2 ? -80 : world.Width + 80, f.Y - 60); f.Timer = 30; }
    void Unperch(Fly f) { if (f.Perch != null && f.Perch.Claimed == this) f.Perch.Claimed = null; f.Perch = null; f.Thing.Landed = false; }
    void Signal(string name, Point at, PinkThing? thing)
    {
        if (!Enabled) return;
        if (name == "secret-heart")
            foreach (var f in flies) if (f.Mode != Mode.Flee && !f.Retire) { Unperch(f); f.Mode = Mode.Wander; f.Target = at + new Vector(stage.Range(-40, 40), stage.Range(-50, -10)); f.Timer = 4; }
        if (name == "bark")
            foreach (var f in flies) if ((new Point(f.X, f.Y) - at).Length < 230) Scare(f, at);
    }
    void Scare(Fly f, Point from)
    {
        Unperch(f); f.Mode = Mode.Flee; f.Timer = stage.Range(1.2, 1.9);
        var away = new Vector(f.X - from.X, f.Y - from.Y - 30); if (away.Length < 1) away = new Vector(0, -1); away.Normalize();
        f.Target = new Point(f.X + away.X * 220, f.Y + away.Y * 220 - 80);
    }
    protected override void Step(double dt, PinkWorld world)
    {
        if (dt <= 0) return;
        double ground = world.GroundY > 0 ? world.GroundY : world.Height * .85;
        bool flowers = world.Things.Exists(t => t.Kind == "flower");
        for (int i = flies.Count - 1; i >= 0; i--)
        {
            var f = flies[i];
            // Threats: a dog close by (or one that is chasing this butterfly), a fast pointer right next to it.
            // (a dog that claimed this butterfly is noticed from further away).
            if (f.Mode != Mode.Flee)
                foreach (var t in world.Things)
                    if (t.Kind == "dog") { double d2 = (t.Position - new Point(f.X, f.Y)).LengthSquared; if (d2 < 150 * 150 || f.Thing.Claimed != null && d2 < 260 * 260) { Scare(f, t.Position); break; } }
            if (world.Pointer is Point p && stage.PointerVelocity.LengthSquared > 220 * 220 && (p - new Point(f.X, f.Y)).LengthSquared < 65 * 65 && f.Mode != Mode.Flee) Scare(f, p);
            f.Timer -= dt;
            Vector desired; double flapRate = 9, tau = .35;
            switch (f.Mode)
            {
                case Mode.Flee:
                    desired = Toward(f, f.Target, 150); flapRate = 14; tau = .15;
                    if (f.Timer <= 0) { f.Mode = Mode.Wander; f.Target = stage.RandomIn(Zone(world)); f.Timer = stage.Range(3, 6); }
                    break;
                case Mode.Approach:
                    if (f.Perch is { } perch && (perch.Remove || perch.Claimed != this || !world.Things.Contains(perch))) { Unperch(f); f.Mode = Mode.Wander; f.Timer = 2; desired = default; break; }
                    var spot = (f.Perch?.Position ?? f.Target) + f.Offset; double dist = (spot - new Point(f.X, f.Y)).Length;
                    desired = Toward(f, spot, Math.Min(f.Speed, 25 + dist * 1.5)) + (f.Perch?.Velocity ?? default); flapRate = 8; tau = .25;
                    if (dist < 4) { f.Mode = Mode.Rest; f.Timer = stage.Range(3.5, 7.5); f.Thing.Landed = true; f.Vx = f.Vy = 0; }
                    else if (f.Timer <= 0) { Unperch(f); f.Mode = Mode.Wander; f.Timer = 2; }
                    break;
                case Mode.Rest:
                    desired = default; flapRate = .55;
                    if (f.Perch != null)
                    {
                        if (f.Perch.Remove || f.Perch.Claimed != this || !world.Things.Contains(f.Perch)) f.Timer = 0;
                        else { f.X = f.Perch.Position.X + f.Offset.X; f.Y = f.Perch.Position.Y + f.Offset.Y; }
                    }
                    if (f.Timer <= 0 || f.Retire) { Unperch(f); f.Mode = Mode.Wander; f.Target = new Point(f.X + stage.Range(-120, 120), f.Y - stage.Range(60, 140)); f.Timer = stage.Range(2, 4); f.Vy = -30; }
                    break;
                default:
                    desired = Toward(f, f.Target, f.Speed);
                    if (!f.Retire && ((f.Target - new Point(f.X, f.Y)).Length < 30 || f.Timer <= 0)) { f.Target = stage.RandomIn(Zone(world)); f.Timer = stage.Range(3, 6); }
                    if (!f.Retire && stage.Next() < dt * .06) Land(f, world, flowers, ground);
                    if (f.Retire && (f.X < -60 || f.X > world.Width + 60)) { Remove(f.Root); world.Things.Remove(f.Thing); flies.RemoveAt(i); continue; }
                    break;
            }
            if (f.Mode != Mode.Rest)
            {
                var wind = world.Wind(new Point(f.X, f.Y), world.Time);
                desired += new Vector(wind.X * .25, wind.Y * .25 + 7 * Math.Sin(world.Time * 1.7 + f.Speed));
                // Soft walls: stay out of the title bar and above the ground line; come back if blown off-screen.
                if (!f.Retire) { if (f.X < 25) desired.X += 70; if (f.X > world.Width - 25) desired.X -= 70; }
                if (f.Y < 60) desired.Y += 60; if (f.Y > ground - 18) desired.Y -= 80;
                double relax = 1 - Math.Exp(-dt / tau);
                f.Vx += (desired.X - f.Vx) * relax; f.Vy += (desired.Y - f.Vy) * relax;
                f.X += f.Vx * dt; f.Y += f.Vy * dt;
                double want = Math.Atan2(f.Vy, f.Vx) * 180 / Math.PI + 90, turn = ((want - f.Heading) % 360 + 540) % 360 - 180;
                f.Heading += Math.Clamp(turn, -260 * dt, 260 * dt);
            }
            else { double turn = (((f.Speed - 48) * 1.5 - f.Heading) % 360 + 540) % 360 - 180; f.Heading += Math.Clamp(turn, -90 * dt, 90 * dt); } // rests nearly upright
            // Wing beat with short glides (wings held open); a resting butterfly opens and closes them slowly.
            f.Glide -= dt;
            if (f.Mode == Mode.Wander && f.Glide < -stage.Range(1.5, 4)) f.Glide = stage.Range(.35, .8);
            bool gliding = f.Mode == Mode.Wander && f.Glide > 0;
            f.Flap += 2 * Math.PI * flapRate * dt;
            double open = gliding ? .95 : f.Mode == Mode.Rest ? .25 + .75 * (.5 + .5 * Math.Cos(f.Flap)) : .16 + .84 * Math.Pow(.5 + .5 * Math.Cos(f.Flap), .8);
            f.Open += (open - f.Open) * (1 - Math.Exp(-dt / (gliding || f.Mode == Mode.Rest ? .12 : .02)));
            f.Bob = f.Mode == Mode.Rest ? 0 : 1.6 * Math.Sin(f.Flap);
            f.Thing.Position = new Point(f.X, f.Y); f.Thing.Velocity = new Vector(f.Vx, f.Vy);
        }
    }
    static Vector Toward(Fly f, Point target, double speed) { var d = new Vector(target.X - f.X, target.Y - f.Y); return d.Length < 1e-3 ? default : d * (speed / d.Length); }
    void Land(Fly f, PinkWorld world, bool flowers, double ground)
    {
        PinkThing? best = null; double bestD = 380 * 380;
        if (flowers)
            foreach (var t in world.Things)
                if (t.Kind == "flower" && t.Claimed == null && !t.Remove && t.Position.Y > 80 && t.Position.Y < ground - 40)
                { double d = (t.Position - new Point(f.X, f.Y)).LengthSquared; if (d < bestD) { bestD = d; best = t; } }
        if (best != null) { f.Perch = best; best.Claimed = this; f.Offset = new Vector(stage.Range(-2, 2), -best.Size * .35); f.Mode = Mode.Approach; f.Timer = 6; return; }
        // No flower: rest on the ground line (the top edge of FRAME's bottom bar) where it is free.
        for (int tries = 0; tries < 4; tries++)
        {
            var area = stage.Free.IsEmpty ? new Rect(world.Width * .6, 0, world.Width * .35, 1) : stage.Free;
            double x = stage.Range(area.Left + 20, area.Right - 20), y = ground - 5 * f.Size;
            if (world.Free(new Rect(x - 12, y - 12, 24, 10))) { f.Perch = null; f.Target = new Point(x, y); f.Offset = default; f.Mode = Mode.Approach; f.Timer = 7; return; }
        }
    }
    protected override void Show(PinkWorld world)
    {
        foreach (var f in flies)
        {
            double k = f.Size * 1.35, r = f.Heading * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            f.Transform.Matrix = new Matrix(k * c, k * s, -k * s, k * c, f.X, f.Y + f.Bob);
            double open = Math.Max(.08, f.Open); f.Left.ScaleX = open; f.Right.ScaleX = open; f.Left.ScaleY = f.Right.ScaleY = .94 + .06 * open;
            double alpha = Math.Round(f.Fade * stage.CalmAlpha(f.X, f.Y), 2); if (alpha != f.Shown) { f.Root.Opacity = alpha; f.Shown = alpha; }
        }
    }
}

// Ribbons and bows: a few long satin ribbons, each with a loose bow at its head, carried across the scene by the wind. The
// head wanders on the wind; the tail follows it (follow-the-leader chain with a little sag), so waves travel down the
// ribbon; the satin twists (front/back colours, sheen on the front) and ends in a swallowtail. Drawn in OnRender from
// reusable StreamGeometries (three per ribbon), ~30 updates/s.
sealed class RibbonsLayer : FrameworkElement, IPinkLayer
{
    const int N = 22;
    sealed class Ribbon
    {
        public readonly double[] X = new double[N], Y = new double[N], W = new double[N];
        public double Vx, Vy, Length, Width, Twist, TwistRate, Phase, Wait, Bow, Age; public bool Live;
        public readonly StreamGeometry Front = new(), Back = new(), Sheen = new(), Edges = new();
        public readonly MatrixTransform BowPose = new();
        public PinkThing Thing = null!;
    }
    readonly PinkStage stage;
    readonly List<Ribbon> ribbons = [];
    bool enabled, seeded;
    public RibbonsLayer(PinkStage stage) { this.stage = stage; IsHitTestVisible = false; Visibility = Visibility.Collapsed; }
    string IPinkLayer.Effect => "ribbons";
    public bool FullRate => false;
    public FrameworkElement Visual => this;
    public bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; Visibility = value ? Visibility.Visible : Visibility.Collapsed; if (!value) { ribbons.Clear(); seeded = false; stage.World.Things.RemoveAll(t => t.Owner == this); InvalidateVisual(); } }
    }
    int Target => Math.Clamp((int)Math.Round(2.6 * stage.Load * Math.Sqrt(Math.Clamp(stage.Area, .65, 1.7))), 1, 4);
    public void Rebalance() { if (!seeded) return; while (ribbons.Count < Target) ribbons.Add(Spawn(new Ribbon(), stage.World, stage.Range(1, 6), inside: false)); }
    Ribbon Spawn(Ribbon r, PinkWorld world, double wait, bool inside)
    {
        double scale = Math.Sqrt(Math.Clamp(stage.Area, .65, 1.7));
        r.Length = stage.Range(180, 260) * scale; r.Width = stage.Range(12, 15.5); r.Twist = stage.Range(0, 6.3); r.TwistRate = stage.Range(1.4, 2.4); r.Phase = stage.Range(0, 6.3); r.Bow = stage.Range(30, 37);
        r.Wait = wait; r.Live = true; r.Age = 0;
        double x = inside ? stage.Range(.25, .85) * world.Width : -10, y = stage.Range(.18, .72) * world.Height, seg = r.Length / (N - 1);
        for (int i = 0; i < N; i++) { r.X[i] = x - i * seg * .92; r.Y[i] = y + 6 * Math.Sin(i * .5 + r.Phase); }
        var wind = world.Wind(new Point(x, y), world.Time); r.Vx = wind.X; r.Vy = 0;
        if (r.Thing != null) world.Things.Remove(r.Thing);
        r.Thing = new PinkThing { Kind = "ribbon", Owner = this, Size = r.Bow * .45, Position = new Point(x, y) }; world.Things.Add(r.Thing);
        return r;
    }
    public void Advance(double dt, PinkWorld world)
    {
        if (!enabled || world.Width <= 0) return;
        if (!seeded) Seed(world);
        double relax = 1 - Math.Exp(-dt / .6), t = world.Time;
        foreach (var r in ribbons)
        {
            if (r.Wait > 0) { r.Wait -= dt; continue; }
            r.Age += dt;
            var wind = world.Wind(new Point(r.X[0], r.Y[0]), t);
            double tx = wind.X * .95 + 12 * Math.Sin(.43 * t + r.Phase), ty = wind.Y * .6 - 3 + 24 * Math.Sin(.7 * t + r.Phase * 1.7);
            r.Vx += (tx - r.Vx) * relax; r.Vy += (ty - r.Vy) * relax;
            var puff = stage.Puff(r.X[0], r.Y[0]); r.Vx += puff.X * dt * 1.5; r.Vy += puff.Y * dt * 1.5;
            r.X[0] += r.Vx * dt; r.Y[0] += r.Vy * dt;
            double seg = r.Length / (N - 1);
            for (int i = 1; i < N; i++)
            {
                var w = world.Wind(new Point(r.X[i], r.Y[i]), t);
                r.X[i] += w.X * .35 * dt; r.Y[i] += (w.Y * .35 + 4) * dt;
                double dx = r.X[i] - r.X[i - 1], dy = r.Y[i] - r.Y[i - 1], d = Math.Sqrt(dx * dx + dy * dy);
                if (d > seg) { r.X[i] = r.X[i - 1] + dx / d * seg; r.Y[i] = r.Y[i - 1] + dy / d * seg; }
            }
            r.Twist += r.TwistRate * dt;
            r.Thing.Position = new Point(r.X[0], r.Y[0]); r.Thing.Velocity = new Vector(r.Vx, r.Vy);
            double tail = r.X[0]; for (int i = 1; i < N; i++) tail = Math.Min(tail, r.X[i]);
            if (tail > world.Width + 30 || r.Y[0] < -r.Length || r.Y[0] > world.Height + r.Length || r.Thing.Remove) Spawn(r, world, stage.Range(2, 7), inside: false);
        }
        InvalidateVisual();
    }
    void Seed(PinkWorld world)
    {
        seeded = true; ribbons.Clear(); int n = Target;
        for (int i = 0; i < n; i++) ribbons.Add(Spawn(new Ribbon(), world, 0, inside: true));
        // Spread the seeded ribbons so they are not stacked: let each settle into a natural curve for a moment.
        for (int k = 0; k < 40; k++) Advance(1 / 30d, world);
    }
    public void Present(PinkWorld world) { if (!enabled || world.Width <= 0) return; if (!seeded) Seed(world); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (!enabled) return;
        foreach (var r in ribbons)
        {
            if (r.Wait > 0 || !r.Live) continue;
            Build(r);
            double calm = stage.CalmAlpha(r.X[0], r.Y[0]), alpha = Math.Round((calm < 1 ? .42 + .53 * (calm - .62) / .38 : .95) * PinkStage.Ease(r.Age / .6 + (r.X[0] < 0 ? 1 : 0)), 2);
            dc.PushOpacity(alpha);
            dc.DrawGeometry(PinkArt.RibbonBack, null, r.Back); dc.DrawGeometry(PinkArt.RibbonFront, null, r.Front); dc.DrawGeometry(PinkArt.RibbonSheen, null, r.Sheen);
            dc.DrawGeometry(null, PinkArt.RibbonEdge, r.Edges);
            double dx = r.X[1] - r.X[0], dy = r.Y[1] - r.Y[0], a = Math.Atan2(-dx, dy), k = r.Bow / 26, c = Math.Cos(a), s = Math.Sin(a);
            r.BowPose.Matrix = new Matrix(k * c, k * s, -k * s, k * c, r.X[0], r.Y[0]);
            dc.PushTransform(r.BowPose); dc.DrawDrawing(PinkArt.Bow); dc.Pop();
            dc.Pop();
        }
    }
    // Twisted strip: width w_i = W·cos(twist − 0.33 i) (sign = which side faces us), mild taper, swallowtail end. Segments
    // whose width changes sign are split at the crossing so front and back meet in a point.
    static void Build(Ribbon r)
    {
        Span<double> nx = stackalloc double[N], ny = stackalloc double[N];
        for (int i = 0; i < N; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(N - 1, i + 1);
            double tx = r.X[b] - r.X[a], ty = r.Y[b] - r.Y[a], len = Math.Sqrt(tx * tx + ty * ty); if (len < 1e-6) { tx = 1; ty = 0; len = 1; }
            nx[i] = -ty / len; ny[i] = tx / len;
            r.W[i] = r.Width * Math.Cos(r.Twist - .33 * i) * (1 - .25 * i / (N - 1));
        }
        using (var front = r.Front.Open())
        using (var back = r.Back.Open())
        using (var sheen = r.Sheen.Open())
        {
            for (int i = 0; i < N - 1; i++)
            {
                double wa = r.W[i] / 2, wb = r.W[i + 1] / 2;
                Point la = new(r.X[i] + nx[i] * wa, r.Y[i] + ny[i] * wa), ra = new(r.X[i] - nx[i] * wa, r.Y[i] - ny[i] * wa);
                Point lb = new(r.X[i + 1] + nx[i + 1] * wb, r.Y[i + 1] + ny[i + 1] * wb), rb = new(r.X[i + 1] - nx[i + 1] * wb, r.Y[i + 1] - ny[i + 1] * wb);
                if (wa * wb >= 0)
                {
                    var ctx = wa + wb >= 0 ? front : back;
                    if (i == N - 2) // swallowtail: the end is notched inwards
                    {
                        double ex = r.X[i + 1] - r.X[i], ey = r.Y[i + 1] - r.Y[i], el = Math.Max(1e-6, Math.Sqrt(ex * ex + ey * ey)), depth = Math.Abs(wb) * 1.3;
                        Five(ctx, la, lb, new Point(r.X[i + 1] - ex / el * depth, r.Y[i + 1] - ey / el * depth), rb, ra);
                    }
                    else Quad(ctx, la, lb, rb, ra, null);
                    if (wa + wb > 0)
                    {
                        double sa = wa * .3, sb = wb * .3, oa = wa * .25, ob = wb * .25;
                        Quad(sheen, new(r.X[i] + nx[i] * (oa + sa), r.Y[i] + ny[i] * (oa + sa)), new(r.X[i + 1] + nx[i + 1] * (ob + sb), r.Y[i + 1] + ny[i + 1] * (ob + sb)),
                            new(r.X[i + 1] + nx[i + 1] * (ob - sb), r.Y[i + 1] + ny[i + 1] * (ob - sb)), new(r.X[i] + nx[i] * (oa - sa), r.Y[i] + ny[i] * (oa - sa)), null);
                    }
                }
                else
                {
                    double t = wa / (wa - wb); var mid = new Point(r.X[i] + (r.X[i + 1] - r.X[i]) * t, r.Y[i] + (r.Y[i + 1] - r.Y[i]) * t);
                    Tri(wa > 0 ? front : back, la, mid, ra); Tri(wb > 0 ? front : back, mid, lb, rb);
                }
            }
        }
        using var edges = r.Edges.Open();
        for (int side = -1; side <= 1; side += 2)
        {
            edges.BeginFigure(new Point(r.X[0] + side * nx[0] * r.W[0] / 2, r.Y[0] + side * ny[0] * r.W[0] / 2), false, false);
            for (int i = 1; i < N; i++) edges.LineTo(new Point(r.X[i] + side * nx[i] * r.W[i] / 2, r.Y[i] + side * ny[i] * r.W[i] / 2), true, true);
        }
    }
    static void Quad(StreamGeometryContext c, Point a, Point b, Point d, Point e, Point? f)
    {
        c.BeginFigure(a, true, true); c.LineTo(b, false, false); c.LineTo(d, false, false); c.LineTo(e, false, false);
        if (f is Point last) c.LineTo(last, false, false);
    }
    static void Five(StreamGeometryContext c, Point a, Point b, Point notch, Point d, Point e)
    {
        c.BeginFigure(a, true, true); c.LineTo(b, false, false); c.LineTo(notch, false, false); c.LineTo(d, false, false); c.LineTo(e, false, false);
    }
    static void Tri(StreamGeometryContext c, Point a, Point b, Point d) { c.BeginFigure(a, true, true); c.LineTo(b, false, false); c.LineTo(d, false, false); }
}
