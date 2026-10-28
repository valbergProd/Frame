using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Frame.App;

// Desert Drift atmosphere (0.5.5): a wind-swept desert at dusk, drawn natively (no video, shader or capture analysis).
// One clock: CompositionTarget.Rendering while FRAME is on screen, frame-time integration (dt from RenderingTime,
// about 30 updates/s: every second frame (frames 17–20 ms apart; a 25 ms minimum step), the rate the motion was reviewed at (0.5.5 integration: 55/s cost ~31 % of
// a core and 4.4 % of an RTX 4090 while visible, about twice as much for no visible gain); a stall never fast-forwards:
// dt <= 0.1 s). The subscription is removed the
// moment FRAME is hidden, minimized, in the tray, cloaked, the session is locked or the display is off, and while
// Windows' "Show animations" is off; nothing advances then and it resumes where it stopped.
static class Drift
{
    // Smooth value in about -1..1; periods 10–48 s with irrational ratios, so the sum never lines up into an obvious loop.
    public static double Noise(double t, double seed) => Math.Sin(t * .61 + seed * 1.7) * .5 + Math.Sin(t * .277 + seed * 4.3) * .3 + Math.Sin(t * .1313 + seed * 7.9) * .2;
    // Slow shared "wind" in roughly 0.55–1.45 (kept for compatibility; DesertWind is the atmosphere's wind model).
    public static double Gust(double t) => 1 + .45 * Noise(t * .35, 2.2);
}

// Lightweight wind model shared by every grain and the dune light: one prevailing direction (left to right, slightly
// settling), a slowly wandering mean strength, deterministic gusts (smooth sin² bumps 4.5–8 s long, every 8–23 s) whose
// front crosses the window from the left at 620 DIP/s, spatially correlated turbulence (curl of a three-wave stream
// function, divergence-free swirls that drift with the flow) and wind streams (meandering bands of denser dust).
sealed class DesertWind
{
    public const double Base = 46, Front = 620, Slope = .045;
    readonly double[] start = [-99, -99, -99, -99], length = [1, 1, 1, 1], amp = [0, 0, 0, 0];
    double nextGust = 2.5;
    uint seed = 0x9E3779B9;
    public double Time { get; private set; }
    public double Travel { get; private set; } // integrated mean wind (DIP): eddies and streams ride on it
    public double Mean { get; private set; } = 1;
    double Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216d; }
    public void Advance(double dt)
    {
        Time += dt;
        Mean = 1 + .16 * Drift.Noise(Time * .05, 3.1) + .06 * Drift.Noise(Time * .21, 8.4);
        Travel += Base * Mean * dt;
        while (Time + 4 > nextGust)
        {
            int slot = 0; for (int i = 1; i < 4; i++) if (start[i] + length[i] < start[slot] + length[slot]) slot = i;
            start[slot] = nextGust; length[slot] = 4.5 + 3.5 * Next(); amp[slot] = .45 + .6 * Next();
            nextGust += 8 + 15 * Next();
        }
    }
    public double Gust(double x)
    {
        double g = 0;
        for (int i = 0; i < 4; i++) { double s = (Time - x / Front - start[i]) / length[i]; if (s > 0 && s < 1) { double b = Math.Sin(Math.PI * s); g += amp[i] * b * b; } }
        return g;
    }
    public double Strength(double x) => Mean + Gust(x);
    // u = dψ/dy, v = -dψ/dx of ψ = Σ a·sin(kx·X + ky·y + ωt + φ), X = x - 0.5·Travel (eddies drift slower than the grains,
    // so grains pass through them and meander): a slow swell (~600 DIP, 30 s), meanders (~250 DIP, 11 s), small swirls
    // (~140 DIP, 7 s). Vertical part damped (air near the ground). Shared by every grain: neighbours move alike.
    public (double U, double V) Turbulence(double x, double y)
    {
        double X = x - .5 * Travel, t = Time;
        double c1 = Math.Cos(.0105 * X + .0124 * y + .21 * t + .7), c2 = Math.Cos(-.0245 * X + .0262 * y - .57 * t + 2.3), c3 = Math.Cos(.046 * X - .041 * y + .9 * t + 4.1);
        double u = 700 * .0124 * c1 + 330 * .0262 * c2 - 120 * .041 * c3;
        double v = -(700 * .0105 * c1 - 330 * .0245 * c2 + 120 * .046 * c3);
        return (u, .7 * v);
    }
    // 0..1: thin meandering bands (about three across the window height) that drift downstream; dust is denser in them.
    public double Stream(double x, double y)
    {
        double X = x - .72 * Travel;
        double band = Math.Sin(.0185 * y + 1.5 * Math.Sin(.0042 * X + .6) + .9 * Math.Sin(.0019 * X - .004 * y + 2));
        double s = (band + 1) * .5; s = s * s * (3 - 2 * s); s *= s;
        return s * (.55 + .45 * Math.Sin(.0057 * X + .008 * y + 1.3));
    }
}

// Sand and dust (full window, beneath the UI): bounded particle counts, every grain driven by the same DesertWind.
// Kinds: 0 far dust (fine, dim, slow parallax), 1 midground wisps (follow the wind streams), 2 foreground grains (larger,
// faster, a little heavier, settle), 3 surface grains on the lit dunes: they creep along the contours (DesertTerrain.Flow),
// hop in short low arcs with a damped bounce when the wind picks up (saltation) and lift off the glowing crests as
// spindrift in gusts. Grains relax toward the local wind with a size-dependent response time; no independent jitter.
// Look: tiny irregular kites (four per-grain radii), stretched along their velocity (short exposure blur), warm mineral
// tones, lit by the art (AirLight/Glow): dust is visible where the dusk light is and fades in the dark. Grains leave the
// window off-screen and re-enter off-screen, or fade out/in slowly (never pop). Rendering: grains are bucketed by tone and
// by alpha level (9 levels, size-compensated so intensity stays continuous) into reusable StreamGeometries, one
// DrawGeometry per bucket; arrays are preallocated, so a frame allocates only the geometry streams.
public sealed class SandDriftLayer : FrameworkElement
{
    struct Grain
    {
        public double X, Y, Vx, Vy, Z, Vz, Age, Life, Size, Shine;
        public float A, B, C, D;
        public byte Kind, Mode, Tone;
    }
    static readonly int[] Count = [140, 260, 70, 440];
    static readonly double[] Parallax = [.42, .74, 1.3, 1], Tau = [.3, .45, .8, .55], SizeMin = [.45, .55, .9, .65], SizeMax = [.85, 1.05, 1.6, 1.25];
    static readonly double[] AlphaBase = [.32, .42, .46, .7], Exposure = [.022, .036, .05, .06], Settle = [0, 1.5, 6, 2], Turbulent = [.75, 1, 1.15, 1], LightFloor = [.16, .12, .14, 0];
    const int Tones = 4, Levels = 9;
    static readonly double[] Level = Enumerable.Range(0, Levels).Select(k => .03 * Math.Pow(1.45, k)).ToArray();
    static readonly Color[] DarkTones = [Color.FromRgb(186, 128, 84), Color.FromRgb(222, 160, 102), Color.FromRgb(255, 160, 84), Color.FromRgb(255, 198, 138)];
    static readonly Color[] LightTones = [Color.FromRgb(120, 88, 62), Color.FromRgb(138, 98, 62), Color.FromRgb(160, 88, 42), Color.FromRgb(150, 104, 62)];
    readonly Grain[] grains = new Grain[Count.Sum()];
    // Midground wisps: long, very faint veils of lit dust (soft horizontal ellipses) carried by the same wind and streams.
    struct Wisp { public double X, Y, Vx, Vy, Age, Life, Length, Height, Shine; }
    readonly Wisp[] wisps = new Wisp[46];
    RadialGradientBrush? wispBrush;
    readonly StreamGeometry[] geometry = new StreamGeometry[Tones * Levels];
    readonly int[] bucket = new int[Count.Sum()], fill = new int[Tones * Levels + 1], cursor = new int[Tones * Levels], order = new int[Count.Sum()];
    readonly double[] shape = new double[Count.Sum() * 8];
    Brush[] brushes = [];
    // Two tiled dust sheets (far: fine, slow; mid: coarser, faster) moved by the wind and seen through the AirLight mask.
    readonly TranslateTransform farSheet = new(), midSheet = new();
    readonly ImageBrush?[] sheets = new ImageBrush?[2];
    ImageBrush? airMask;
    Size maskSize;
    double farTravel, midTravel;
    bool? light;
    bool seeded;
    uint seed = 0x6D2B79F5;
    public int Updates { get; private set; }
    public SandDriftLayer()
    {
        for (int i = 0; i < geometry.Length; i++) geometry[i] = new StreamGeometry { FillRule = FillRule.Nonzero };
        Theme(false);
    }
    double Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216d; }
    internal void Restart() { seeded = false; seed = 0x6D2B79F5; farTravel = midTravel = 0; }
    public void Theme(bool lightTheme)
    {
        if (light == lightTheme) return;
        light = lightTheme; var tones = lightTheme ? LightTones : DarkTones; var list = new Brush[Tones * Levels];
        for (int t = 0; t < Tones; t++) for (int k = 0; k < Levels; k++) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Min(1, Level[k])), tones[t].R, tones[t].G, tones[t].B)); b.Freeze(); list[t * Levels + k] = b; }
        var veil = lightTheme ? Color.FromRgb(124, 88, 60) : Color.FromRgb(236, 146, 80);
        var wisp = new RadialGradientBrush();
        for (int k = 0; k <= 8; k++) { double r = k / 8d, f = (1 - r * r) * (1 - r * r); wisp.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * f), veil.R, veil.G, veil.B), r)); }
        wisp.Freeze(); wispBrush = wisp;
        brushes = list; sheets[0] = sheets[1] = null; InvalidateVisual();
    }
    void BuildSheets()
    {
        if (sheets[0] != null || !DesertTerrain.Ready) return;
        var tone = light == true ? LightTones[0] : DarkTones[0];
        sheets[0] = new ImageBrush(DesertTerrain.DustSheet(0x1234567, 512, 3000, 4, tone)) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 512, 512), Stretch = Stretch.Fill, Transform = farSheet, Opacity = light == true ? .25 : .5 };
        sheets[1] = new ImageBrush(DesertTerrain.DustSheet(0x7654321, 512, 900, 6, tone)) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 720, 720), Stretch = Stretch.Fill, Transform = midSheet, Opacity = light == true ? .2 : .4 };
        airMask ??= new ImageBrush(DesertTerrain.AirMask) { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute };
    }

    void Seed(double w, double h, DesertWind wind)
    {
        int i = 0;
        for (byte kind = 0; kind < 4; kind++)
            for (int n = 0; n < Count[kind]; n++, i++)
            {
                ref var g = ref grains[i]; g.Kind = kind;
                g.A = (float)(.75 + .5 * Next()); g.B = (float)(.7 + .5 * Next()); g.C = (float)(.7 + .5 * Next()); g.D = (float)(.75 + .5 * Next());
                if (kind < 3) { SpawnAir(ref g, w, h, wind, edge: false); g.Age = g.Life * Next(); }
                else { SpawnSurface(ref g, w, h); g.Age = g.Life * Next(); }
            }
        for (int n = 0; n < wisps.Length; n++) { SpawnWisp(ref wisps[n], w, h, wind, edge: false); wisps[n].Age = wisps[n].Life * Next(); }
        seeded = true;
    }
    void SpawnWisp(ref Wisp v, double w, double h, DesertWind wind, bool edge)
    {
        v.Length = 70 + 150 * Next(); v.Height = 7 + 16 * Next(); v.Shine = .55 + .45 * Next();
        v.X = edge ? -v.Length - 120 * Next() : w * Next(); v.Y = h * Next();
        for (int tries = 0; tries < 6 && !edge; tries++) // prefer the lit air (where a veil can be seen at all)
        {
            var img = DesertTerrain.ImageRect(w, h); double lit = DesertTerrain.AirLight((v.X - img.X) / img.Width, (v.Y - img.Y) / img.Height);
            if (Next() < .2 + .8 * lit) break; v.X = w * Next(); v.Y = h * Next();
        }
        v.Life = 7 + 9 * Next(); v.Age = edge ? 2.5 : 0; v.Vx = .68 * DesertWind.Base * wind.Strength(v.X); v.Vy = 0;
    }
    void SpawnAir(ref Grain g, double w, double h, DesertWind wind, bool edge)
    {
        int k = g.Kind;
        g.X = edge ? -6 - 34 * Next() : w * Next(); g.Y = h * Next();
        g.Size = SizeMin[k] + (SizeMax[k] - SizeMin[k]) * Math.Pow(Next(), 1.6); g.Shine = .55 + .45 * Next();
        g.Tone = (byte)(k == 2 ? (Next() < .6 ? 1 : 0) : Next() < (k == 0 ? .3 : .5) ? 1 : 0);
        g.Life = 9 + 16 * Next(); g.Age = edge ? 1.6 : 0; // edge entries are already off-screen: no fade-in needed
        g.Vx = Parallax[k] * DesertWind.Base * wind.Strength(g.X); g.Vy = 0; g.Mode = 0; g.Z = g.Vz = 0;
    }
    void SpawnSurface(ref Grain g, double w, double h)
    {
        var img = DesertTerrain.ImageRect(w, h);
        var (u, v) = DesertTerrain.SpawnPoint(Next(), Next(), Next());
        g.X = img.X + u * img.Width; g.Y = img.Y + v * img.Height; g.Mode = 0; g.Z = g.Vz = g.Vx = g.Vy = 0;
        g.Size = SizeMin[3] + (SizeMax[3] - SizeMin[3]) * Next(); g.Shine = .55 + .45 * Next();
        double glow = DesertTerrain.Glow(u, v);
        g.Tone = (byte)(glow > .55 ? (Next() < .7 ? 2 : 3) : glow > .25 ? (Next() < .5 ? 2 : 1) : 1);
        g.Life = 2.5 + 3.5 * Next(); g.Age = 0;
    }

    internal void Advance(double dt, DesertWind wind)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        DesertTerrain.Ensure();
        if (!seeded) Seed(w, h, wind);
        Updates++;
        var img = DesertTerrain.ImageRect(w, h);
        Span<double> relax = stackalloc double[4];
        for (int k = 0; k < 4; k++) relax[k] = 1 - Math.Exp(-dt / Tau[k]);
        for (int i = 0; i < grains.Length; i++)
        {
            ref var g = ref grains[i];
            g.Age += dt;
            if (g.Kind < 3 || g.Mode == 2)
            {
                int k = g.Kind; double s = wind.Strength(g.X); var (tu, tv) = wind.Turbulence(g.X, g.Y);
                double turbulence = Turbulent[k] * (.75 + .45 * Math.Max(0, s - 1)), p = Parallax[k];
                double lift = g.Mode == 2 ? -7 * Math.Max(0, 1 - g.Age / g.Life) : 0;
                double tx = p * (DesertWind.Base * s + tu * turbulence), ty = p * (DesertWind.Base * s * DesertWind.Slope + tv * turbulence) + Settle[k] + lift;
                g.Vx += (tx - g.Vx) * relax[k]; g.Vy += (ty - g.Vy) * relax[k];
                g.X += g.Vx * dt; g.Y += g.Vy * dt;
                bool outside = g.X > w + 30 || g.X < -90 || g.Y < -30 || g.Y > h + 30;
                if (g.Kind < 3) { if (outside) SpawnAir(ref g, w, h, wind, edge: true); else if (g.Age >= g.Life) SpawnAir(ref g, w, h, wind, edge: false); }
                else if (outside || g.Age >= g.Life) SpawnSurface(ref g, w, h);
                continue;
            }
            // Surface grain: creeps along the dune contour; hops (saltation) and lifts off crests when the wind rises.
            double u = (g.X - img.X) / img.Width, v = (g.Y - img.Y) / img.Height;
            var (fx, fy) = DesertTerrain.Flow(u, v);
            double strength = wind.Strength(g.X), depth = .7 + .6 * Math.Clamp(g.Y / h, 0, 1), q = Math.Clamp((strength - .92) / .7, 0, 1);
            if (g.Mode == 0)
            {
                double speed = DesertWind.Base * strength * .2 * depth;
                g.Vx = fx * speed; g.Vy = fy * speed; g.X += g.Vx * dt; g.Y += g.Vy * dt;
                if (Next() < (.05 + .75 * q * q) * dt) { g.Mode = 1; g.Vz = 14 + 24 * Next(); g.Z = .01; }
                else if (Next() < DesertTerrain.Glow(u, v) * (.03 + 1.1 * q * q) * dt)
                {
                    g.Mode = 2; g.Vx = speed * 2 + DesertWind.Base * strength * .3; g.Vy = fy * speed * 2 - (8 + 14 * Next());
                    g.Life = Math.Min(g.Life, g.Age + 1.4 + 1.8 * Next());
                }
            }
            else
            {
                double speed = DesertWind.Base * strength * .55 * depth;
                g.Vx = fx * speed; g.Vy = fy * speed; g.X += g.Vx * dt; g.Y += g.Vy * dt;
                g.Z += g.Vz * dt; g.Vz -= 150 * dt;
                if (g.Z <= 0 && g.Vz < 0) { g.Z = 0; if (g.Vz < -11) g.Vz *= -.42; else { g.Mode = 0; g.Vz = 0; } }
            }
            if (g.X > w + 30 || g.X < -90 || g.Y < -30 || g.Y > h + 30 || g.Age >= g.Life) SpawnSurface(ref g, w, h);
        }
        double wispRelax = 1 - Math.Exp(-dt / .9);
        for (int n = 0; n < wisps.Length; n++)
        {
            ref var v = ref wisps[n]; v.Age += dt;
            double s = wind.Strength(v.X); var (tu, tv) = wind.Turbulence(v.X, v.Y);
            v.Vx += (.68 * (DesertWind.Base * s + .9 * tu) - v.Vx) * wispRelax; v.Vy += (.68 * (DesertWind.Base * s * DesertWind.Slope + .9 * tv) - v.Vy) * wispRelax;
            v.X += v.Vx * dt; v.Y += v.Vy * dt;
            if (v.X - v.Length > w + 10 || v.Y < -60 || v.Y > h + 60) SpawnWisp(ref v, w, h, wind, edge: true);
            else if (v.Age >= v.Life) SpawnWisp(ref v, w, h, wind, edge: false);
        }
        // Dust sheets: rigid drift at their depth's share of the wind at mid-window (gusts speed them up together), a slight
        // settling slope and a slow vertical sway; wrapped by the tile size so the numbers stay small (tiles are seamless).
        double drift = DesertWind.Base * wind.Strength(w * .5) * dt;
        farTravel = (farTravel + drift * .36) % (512 * 64); midTravel = (midTravel + drift * .55) % (720 * 64);
        farSheet.X = farTravel % 512; farSheet.Y = (farTravel * DesertWind.Slope + 9 * Drift.Noise(wind.Time * .07, 4.4)) % 512;
        midSheet.X = midTravel % 720; midSheet.Y = (midTravel * DesertWind.Slope + 14 * Drift.Noise(wind.Time * .09, 7.7)) % 720;
        Emit(img, w, h, wind);
        InvalidateVisual();
    }

    static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    DesertWind? windNow;
    void Emit(Rect img, double w, double h, DesertWind wind)
    {
        windNow = wind;
        Array.Clear(fill);
        double themeAlpha = light == true ? .55 : 1;
        for (int i = 0; i < grains.Length; i++)
        {
            ref var g = ref grains[i];
            int k = g.Kind; bool surface = k == 3;
            double fadeIn = surface ? .5 : 1.6, fadeOut = surface ? .7 : 1.8;
            double fade = Ease(g.Age / fadeIn) * Ease((g.Life - g.Age) / fadeOut);
            double x = g.X, y = g.Y - g.Z, u = (x - img.X) / img.Width, v = (y - img.Y) / img.Height;
            double lit = surface && g.Mode != 2 ? DesertTerrain.Glow(u, v) : Math.Max(DesertTerrain.AirLight(u, v), surface ? .6 * DesertTerrain.Glow(u, v) : 0);
            double alpha = AlphaBase[k] * g.Shine * fade * (LightFloor[k] + (1 - LightFloor[k]) * lit) * themeAlpha;
            if (k == 1) alpha *= .25 + .75 * wind.Stream(x, y);
            else if (k == 0) alpha *= .6 + .4 * wind.Stream(x, y);
            if (surface) alpha *= g.Mode == 1 ? 1.25 : g.Mode == 2 ? 1 : .8;
            double vx = g.Vx, vy = g.Vy - (g.Mode == 1 ? g.Vz : 0), speed = Math.Sqrt(vx * vx + vy * vy);
            double blur = speed * Exposure[k], size = g.Size;
            alpha *= Math.Pow(size / (size + .5 * blur), .6);
            if (alpha < .012 || x < -20 || x > w + 20 || y < -20 || y > h + 20) { bucket[i] = -1; continue; }
            int level = 0; while (level < Levels - 1 && Level[level] < alpha) level++;
            size *= Math.Min(1.35, Math.Sqrt(alpha / Level[level])); // coverage-preserving: quantized alpha, continuous intensity
            double ex = speed > 1e-3 ? vx / speed : 1, ey = speed > 1e-3 ? vy / speed : 0;
            double half = size * g.A + .5 * blur, wide = size * g.B * (blur > size ? .8 : 1);
            int s = i * 8;
            shape[s] = x + ex * half; shape[s + 1] = y + ey * half;
            shape[s + 2] = x - ey * wide; shape[s + 3] = y + ex * wide;
            shape[s + 4] = x - ex * half * g.C; shape[s + 5] = y - ey * half * g.C;
            shape[s + 6] = x + ey * wide * g.D; shape[s + 7] = y - ex * wide * g.D;
            bucket[i] = g.Tone * Levels + level; fill[bucket[i] + 1]++;
        }
        for (int b = 1; b < fill.Length; b++) fill[b] += fill[b - 1];
        Array.Copy(fill, cursor, cursor.Length); // fill keeps the bucket starts
        for (int i = 0; i < grains.Length; i++) if (bucket[i] >= 0) order[cursor[bucket[i]]++] = i;
        for (int b = 0; b < geometry.Length; b++)
        {
            using var ctx = geometry[b].Open();
            for (int n = fill[b]; n < fill[b + 1]; n++)
            {
                int s = order[n] * 8;
                ctx.BeginFigure(new Point(shape[s], shape[s + 1]), true, true);
                ctx.LineTo(new Point(shape[s + 2], shape[s + 3]), false, false);
                ctx.LineTo(new Point(shape[s + 4], shape[s + 5]), false, false);
                ctx.LineTo(new Point(shape[s + 6], shape[s + 7]), false, false);
            }
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!seeded || brushes.Length == 0) return;
        BuildSheets();
        if (sheets[0] != null && airMask != null)
        {
            var size = RenderSize; if (size != maskSize) { maskSize = size; airMask.Viewport = DesertTerrain.ImageRect(size.Width, size.Height); }
            var all = new Rect(size);
            dc.PushOpacityMask(airMask); dc.DrawRectangle(sheets[0], null, all); dc.DrawRectangle(sheets[1], null, all); dc.Pop();
        }
        if (wispBrush != null && windNow != null)
        {
            var img = DesertTerrain.ImageRect(ActualWidth, ActualHeight); double themeAlpha = light == true ? .5 : 1;
            for (int n = 0; n < wisps.Length; n++)
            {
                ref var v = ref wisps[n];
                double fade = Ease(v.Age / 2.5) * Ease((v.Life - v.Age) / 2.5), lit = DesertTerrain.AirLight((v.X - img.X) / img.Width, (v.Y - img.Y) / img.Height);
                double alpha = .11 * v.Shine * fade * (.12 + .88 * lit) * (.35 + .65 * windNow.Stream(v.X, v.Y)) * themeAlpha;
                if (alpha < .004) continue;
                double stretch = 1 + .3 * Math.Clamp(windNow.Gust(v.X), 0, 1); // gusts draw the veils out
                dc.PushOpacity(alpha); dc.DrawEllipse(wispBrush, null, new Point(v.X, v.Y), v.Length * stretch, v.Height); dc.Pop();
            }
        }
        for (int b = 0; b < geometry.Length; b++) if (fill[b + 1] > fill[b]) dc.DrawGeometry(brushes[b], null, geometry[b]);
    }
}

// Light on the dunes: the crest light of the artwork (DesertTerrain.CrestFar/CrestNear, pale gold, only where the art
// glows) seen through a tileable mask of fine sand streaks in soft gust patches that drifts downwind, so granular
// highlights stream along the glowing ridges and the lit haze like light catching blowing sand. Far and near dunes have their own mask and speed (parallax:
// independent movement per dune layer); intensity swells a little with the gusts. Placement equals the backdrop's
// UniformToFill brush (same box: Margin -4); opacity follows DesertBackdrop in XAML (dark .88, light .10). Per frame
// only two brush transforms and two brush opacities change: no layout, no OnRender, nothing reshaped.
public sealed class DuneFlowLayer : FrameworkElement
{
    const double TileW = 760, TileH = 380;
    readonly TranslateTransform farDrift = new(), nearDrift = new();
    readonly ImageBrush?[] crest = new ImageBrush?[2];
    readonly ImageBrush?[] mask = new ImageBrush?[2];
    double far, near;
    public int Updates { get; private set; }
    internal void Restart() => far = near = 0;
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); InvalidateVisual(); }
    void Build()
    {
        if (crest[0] != null || !DesertTerrain.Ready) return;
        var images = new[] { DesertTerrain.CrestFar!, DesertTerrain.CrestNear! }; var drifts = new[] { farDrift, nearDrift };
        for (int i = 0; i < 2; i++)
        {
            double scale = i == 0 ? .82 : 1;
            crest[i] = new ImageBrush(images[i]) { Stretch = Stretch.Fill, Opacity = .8 };
            mask[i] = new ImageBrush(DesertTerrain.DriftMask) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, i * 150, TileW * scale, TileH * scale), Stretch = Stretch.Fill, Transform = drifts[i] };
        }
    }
    internal void Advance(double dt, DesertWind wind)
    {
        DesertTerrain.Ensure();
        if (crest[0] == null) { Build(); InvalidateVisual(); }
        Updates++;
        double centre = ActualWidth * .5, s = wind.Strength(centre), gust = Math.Clamp(wind.Gust(centre), 0, 1);
        far = (far + dt * DesertWind.Base * s * .42) % (TileW * .82); near = (near + dt * DesertWind.Base * s * .78) % TileW;
        farDrift.X = far; farDrift.Y = 12 * Drift.Noise(wind.Time * .05, 1.3);
        nearDrift.X = near; nearDrift.Y = 18 * Drift.Noise(wind.Time * .06, 6.1);
        crest[0]!.Opacity = .72 + .28 * gust; crest[1]!.Opacity = .78 + .22 * gust;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (crest[0] == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        var img = DesertTerrain.ImageRect(ActualWidth, ActualHeight);
        for (int i = 0; i < 2; i++) { dc.PushOpacityMask(mask[i]); dc.DrawRectangle(crest[i], null, img); dc.Pop(); }
    }
}

public sealed class RecordingGlow : FrameworkElement
{
    // Soft halo: thirty-two abutting 1px rounded rings with precomputed frozen pens fade quadratically to zero inside the
    // template's 32px spread, so the light hugs the button with no rectangular clip edge. No blur pass or per-frame allocation.
    public const double Spread = 32;
    static readonly Pen[] Rings = MakeRings();
    static Pen[] MakeRings()
    {
        var rings = new Pen[32];
        for (int k = 0; k < rings.Length; k++)
        {
            double fade = 1 - (k + .5) / rings.Length;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(108 * fade * fade), 245, 160, 78)), 1.05);
            pen.Freeze(); rings[k] = pen;
        }
        return rings;
    }
    // Sixteen stops follow a (1 - r^2)^2 falloff, so 8-bit alpha steps stay tiny and the rim reaches exactly zero (no banding or edges).
    static readonly RadialGradientBrush Light = MakeLight(130, 245, 169, 83);
    static readonly RadialGradientBrush Ember = MakeLight(70, 242, 124, 57);
    static RadialGradientBrush MakeLight(double peak, byte r, byte g, byte b)
    {
        var brush = new RadialGradientBrush();
        for (int s = 0; s <= 15; s++)
        {
            double offset = s / 15d, falloff = 1 - offset * offset;
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(peak * falloff * falloff), r, g, b), offset));
        }
        brush.Freeze(); return brush;
    }
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(RecordingGlow), new PropertyMetadata(false, (d, _) => { var glow = (RecordingGlow)d; if (!glow.MotionEnabled) glow.Snap(); }));
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool MotionEnabled { get; set; }
    public double Level { get; private set; }
    double phase;
    public void Snap() { double next = Active ? 1 : 0; if (Level == next) return; Level = next; InvalidateVisual(); }
    public void Advance(double seconds, double time)
    {
        phase = time;
        double target = Active ? 1 : 0;
        // Rises in ~0.4 s, settles out over ~0.6 s: light fading rather than switching off.
        Level += Math.Clamp(target - Level, -seconds * 1.6, seconds * 2.5);
        InvalidateVisual(); // includes the final transparent frame after release
    }
    double intensity = 1;
    public double Intensity { get => intensity; set { if (Math.Abs(value - intensity) < .002) return; intensity = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Level <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        double w = ActualWidth, h = ActualHeight;
        dc.PushOpacity(intensity * Level * Level * (3 - 2 * Level));
        // Halo breathes outward by up to ~8% (still inside Spread) and leans gently, so it never pulses mechanically.
        var face = new Rect(Spread, Spread, Math.Max(0, w - 2 * Spread), Math.Max(0, h - 2 * Spread));
        double step = .9 + .06 * Drift.Noise(phase * .5, 13);
        dc.PushTransform(new TranslateTransform(1.2 * Drift.Noise(phase * .31, 17), .8 * Drift.Noise(phase * .27, 19)));
        for (int k = 0; k < Rings.Length; k++)
        {
            double d = (k + .5) * step;
            var ring = face; ring.Inflate(d, d);
            dc.DrawRoundedRectangle(null, Rings[k], ring, d + 1, d + 1);
        }
        dc.Pop();
        // Every ellipse stays inside the glow surface at all phases, so the gradient is never clipped into a visible edge.
        // An ember undertone drifts beneath four warm pools that wander, breathe and trade brightness independently.
        dc.DrawEllipse(Ember, null, new Point(w * (.5 + .06 * Drift.Noise(phase * .23, 21)), h * .5), w * .4, h * .48);
        for (int i = 0; i < 4; i++)
        {
            double t = phase + i * 11.3;
            double center = .27 + i * .153 + .025 * Drift.Noise(t * .42, i);
            double y = .5 + .02 * Drift.Noise(t * .37, i + 3);
            double breathe = 1 + .08 * Drift.Noise(t * .55, i + 7);
            dc.PushOpacity(.8 + .2 * Drift.Noise(t * .48, i + 11));
            dc.DrawEllipse(Light, null, new Point(w * center, h * y), w * .22 * breathe, h * .44 * (2 - breathe));
            dc.Pop();
        }
        dc.Pop();
    }
}

// Owns the one decorative clock and its lifecycle. Motion runs only while FRAME is actually on screen (visible, not
// minimized/tray, not cloaked on another virtual desktop, session unlocked, display on), Enable(true) and Windows'
// "Show animations" is on (SPI_GETCLIENTAREAANIMATION, followed live). Otherwise: CompositionTarget.Rendering is
// unsubscribed (no callbacks, no simulation, no re-rendering), the particle and dune-light layers are collapsed, and the
// stone/glows show their exact static state (active = lit stone with a steady glow). On return the clock restarts with
// dt = 0 (no catch-up), so every layer continues where it stopped; a Tag change while stopped snaps the stone.
sealed class CaptureAtmosphere : IDisposable
{
    // Outer halo of the Shadow stone, 13 % below the 0.5.4 level so the energy stays inside the stone (the stone art
    // has no baked glow: alpha is 0 outside, ~250 inside; only this WPF halo is reduced).
    const double HaloLevel = .87, MinStep = 1d / 40, MaxStep = .1;
    readonly Window window;
    readonly SandDriftLayer sand;
    readonly DuneFlowLayer dunes;
    readonly Button[] buttons;
    readonly ObsidianStone[] stones;
    readonly DecorativeVisibility visibility;
    DesertWind wind = new();
    readonly DependencyPropertyDescriptor? tag = DependencyPropertyDescriptor.FromProperty(FrameworkElement.TagProperty, typeof(Button));
    bool enabled, disposed, subscribed, manual, restart;
    TimeSpan last;
    double phase;
    public bool Running => subscribed;
    public bool Cloaked => visibility.Cloaked;
    public bool ReducedMotion => visibility.ReducedMotion;
    public bool Obscured => visibility.Obscured;
    public event Action? VisibilityUpdated;
    public int Updates { get; private set; }   // simulation steps (about 30/s while on screen)
    public int Frames { get; private set; }    // raw CompositionTarget.Rendering callbacks while subscribed
    public double LargestStep { get; private set; } // largest clock step since ResetLargestStep (a resume must not catch up)
    public void ResetLargestStep() => LargestStep = 0;
    public bool SandMoving => Running;
    public DesertWaves? Waves { get; set; } // lower dunes; advanced only by this clock, i.e. only while FRAME is visible
    public ObsidianStone? Stone => stones.Length > 0 ? stones[0] : null;
    public int SandUpdates => sand.Updates;
    public int DuneUpdates => dunes.Updates;
    public int StoneUpdates => stones.Sum(s => s.Updates);
    // 0.6.0 visual modes. Desert: the sand, dune light and waves of Original GUI / Cinematic D (off in the other modes: not
    // advanced, collapsed). Clients: other decorative work (Cinematic D stone and light, Pink/Emo scenes) on the same clock and
    // the same visibility rules; the clock subscribes only while something visible wants frames.
    readonly List<IDecorativeClient> clients = [];
    bool desert = true;
    public bool Desert { get => desert; set { if (desert == value) return; desert = value; Reconcile(); } }
    public IReadOnlyList<IDecorativeClient> Clients => clients;
    public int ClientUpdates { get; private set; }
    public void Attach(IDecorativeClient client) { if (!clients.Contains(client)) { clients.Add(client); Reconcile(); } }
    public void Detach(IDecorativeClient client) { if (clients.Remove(client)) Reconcile(); }
    // Re-evaluate after a client's WantsFrames changed (e.g. Pink effects toggled).
    public void Refresh() => Reconcile();
    public CaptureAtmosphere(Window window, SandDriftLayer sand, DuneFlowLayer dunes, params Button[] buttons)
    {
        this.window = window; this.sand = sand; this.dunes = dunes; this.buttons = buttons;
        stones = buttons.Where(b => b.Style == Application.Current.TryFindResource("ObsidianAction")).Select(b => new ObsidianStone(b, HaloLevel)).ToArray();
        visibility = new(window); visibility.Changed += VisibilityChanged;
        window.IsVisibleChanged += WindowVisibleChanged; window.StateChanged += StateChanged; window.Closed += Closed;
        foreach (var button in buttons) tag?.AddValueChanged(button, TagChanged);
    }
    public void Enable(bool value) { enabled = value; ApplyTheme(LightTheme()); Reconcile(); }
    // Particle tones follow the theme (dark: warm mineral light; light: umber specks at lower alpha). Enable() also syncs it
    // from the theme's Background resource, so SettingsView.ApplyTheme (which ends with Enable) needs no extra call.
    public void ApplyTheme(bool light) => sand.Theme(light);
    static bool LightTheme() => Application.Current?.TryFindResource("Background") is SolidColorBrush { Color: var c } && c.R + c.G + c.B > 384;
    void VisibilityChanged() { Reconcile(); VisibilityUpdated?.Invoke(); }
    void WindowVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Reconcile();
    void StateChanged(object? sender, EventArgs e) => Reconcile();
    void Closed(object? sender, EventArgs e) => Dispose();
    void TagChanged(object? sender, EventArgs e) { if (!subscribed && !manual) Present(); }
    bool OnScreen => !disposed && window.IsVisible && window.WindowState != WindowState.Minimized && !visibility.Cloaked && !visibility.Obscured;
    bool Still => !enabled || visibility.ReducedMotion;
    bool StonesShown => stones.Any(s => s.Shown);
    bool Wanted => desert || StonesShown || clients.Any(c => c.WantsFrames);
    // 0.6.0: FRAME's window is actually on screen (visible, not minimized/tray, not cloaked, unlocked, display on). Raised on
    // every change, so mode timers (Emo quotes) can pause while hidden and resume without catching up.
    public bool IsOnScreen => OnScreen;
    public event Action? OnScreenChanged;
    bool lastOnScreen;
    void Reconcile()
    {
        if (disposed) return;
        if (OnScreen != lastOnScreen) { lastOnScreen = OnScreen; OnScreenChanged?.Invoke(); }
        bool motion = !manual && OnScreen && !Still && Wanted;
        sand.Visibility = dunes.Visibility = desert && (motion || manual) ? Visibility.Visible : Visibility.Collapsed;
        if (motion && !subscribed) { CompositionTarget.Rendering += Frame; subscribed = true; restart = true; }
        else if (!motion && subscribed) { CompositionTarget.Rendering -= Frame; subscribed = false; }
        if (!subscribed && !manual) Present();
        foreach (var button in buttons)
            if (button.Template?.FindName("Glow", button) is RecordingGlow glow)
            {
                glow.MotionEnabled = subscribed || manual;
                if (Still) { glow.Intensity = 1; glow.Snap(); }
            }
    }
    // Static presentation while the clock is stopped: exact state for the current Tag (motion look if only hidden, so the
    // stone continues seamlessly when shown again; steady glow when motion is off).
    void Present() { foreach (var stone in stones) stone.Present(Still); foreach (var client in clients) client.Present(); }
    void Frame(object? sender, EventArgs e)
    {
        Frames++;
        var now = e is System.Windows.Media.RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (restart) { restart = false; last = lastFull = now; Step(0); StepFull(0); return; }
        double full = (now - lastFull).TotalSeconds;
        if (full > 1d / 240) { lastFull = now; StepFull(Math.Min(full, MaxStep)); } // full-rate clients: every composition frame
        double dt = (now - last).TotalSeconds;
        if (dt < MinStep) return; // repeated callbacks in one frame, or the frame in between: about 30 updates/s
        last = now; Step(Math.Min(dt, MaxStep));
    }
    TimeSpan lastFull;
    void StepFull(double dt)
    {
        for (int i = 0; i < clients.Count; i++) if (clients[i].FullRate && clients[i].WantsFrames) { clients[i].Advance(dt); ClientUpdates++; }
    }
    // Diagnostics (--render-animation): the same clock stepped deterministically, independent of the screen.
    // Starts from a fixed state (wind, grains, dune light, waves, stone angle), so clips are reproducible and comparable.
    public void BeginManualClock() { manual = true; wind = new(); phase = 0; sand.Restart(); dunes.Restart(); Waves?.Restart(); foreach (var stone in stones) stone.Restart(); Reconcile(); }
    // Shared wind of the desert (direction/strength), for layers that should move with the sand (Cinematic D light, dust).
    public double WindMean => wind.Mean;
    public double WindStrength(double x) => wind.Strength(x);
    public double Phase => phase;
    public void ManualStep(double seconds) { if (manual) { Step(seconds); StepFull(seconds); } }
    void Step(double dt)
    {
        phase += dt; Updates++; LargestStep = Math.Max(LargestStep, dt);
        wind.Advance(dt);
        if (desert)
        {
            if (sand.IsVisible) sand.Advance(dt, wind);
            if (dunes.IsVisible) dunes.Advance(dt, wind);
            Waves?.Advance(dt);
        }
        foreach (var stone in stones) if (stone.Shown) stone.Advance(dt); // Original GUI's stone only while its page is shown
        for (int i = 0; i < clients.Count; i++) if (!clients[i].FullRate && clients[i].WantsFrames) { clients[i].Advance(dt); ClientUpdates++; }
        foreach (var button in buttons)
            if (button.IsVisible && button.Template?.FindName("Glow", button) is RecordingGlow glow && (glow.Active || glow.Level > 0))
            {
                glow.MotionEnabled = true;
                // Clear breathing pulse (~2.6 s, roughly 30–100%) with a slow irregular swell so breaths differ; eased, never flashing.
                double breath = .5 - .5 * Math.Cos(phase * 2 * Math.PI / 2.6);
                glow.Intensity = Math.Clamp(.3 + .64 * breath * breath * (3 - 2 * breath) + .06 * Drift.Noise(phase * .9, 5), .25, 1);
                glow.Advance(dt, phase);
            }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (subscribed) { CompositionTarget.Rendering -= Frame; subscribed = false; }
        foreach (var button in buttons) { tag?.RemoveValueChanged(button, TagChanged); if (button.Template?.FindName("Glow", button) is RecordingGlow glow) glow.Intensity = 1; }
        sand.Visibility = dunes.Visibility = Visibility.Collapsed;
        window.IsVisibleChanged -= WindowVisibleChanged; window.StateChanged -= StateChanged; window.Closed -= Closed;
        visibility.Changed -= VisibilityChanged; visibility.Dispose(); VisibilityUpdated = null; OnScreenChanged = null;
    }
}
