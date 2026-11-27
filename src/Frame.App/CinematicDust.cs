using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

// Sand blowing across the ground under the Cinematic D stone (0.6.9). Grains live on a set of GROUND LEVELS (Lanes) laid out
// like the ground of the background art: the lower a level lies in the window, the nearer it is, so its grains are larger,
// blow faster and rise a little higher; the ground of each level has gentle ripples. Every grain has a mass (heavier = slower to
// pick up speed, falls faster, needs more wind to move) and one of two states:
//   - on the ground: rests, or rolls when the wind is above its threshold; in strong wind it is kicked up (entrainment);
//   - in the air: drag toward the local wind horizontally, terminal fall speed + gusts' lift vertically (lift fades as the grain
//     nears its own ceiling, so no common ceiling and no line), a damped bounce when it lands, losing speed on impact.
// Wind: own gust system (see Schedule/WindAt). Grains are small irregular diamonds with fixed axes in sand colours (dull, no
// glow). Drawn into three alpha buckets (one DrawGeometry each); arrays are preallocated. Advanced only by the layout's clock
// client while the Capture page is shown, so it costs nothing when FRAME is not on screen. Decorative only.
sealed class CinematicDust : FrameworkElement
{
    const int Count = 1100, Lanes = 8;
    const double LiftSpeed = 40, WindRef = 1.3; // DIP/s of a full gust's lift (nearest level); wind (units of DesertWind.Base) that carries sand fully
    const byte Ground = 0, Air = 1;
    struct Grain { public double X, Y0, Scale, Depth, Z, Vx, Vz, Age, Life, Mass, Size, Shine, ZMax, Phase; public byte Lane, Mode; public float Ra, Rb; }
    readonly Grain[] grains = new Grain[Count];
    readonly double[] laneY = new double[Lanes], lanePhase = new double[Lanes];
    uint seed = 0xC2B2AE35;
    double width, height, originX, originY, windowWidth = 1280, windowHeight = 800, pool = 1, halo = 1, lateral;
    double stoneX = 400, stoneY = 300, stoneR = 140, contactY = 440; // window coordinates (SetStone)
    bool seeded;

    public CinematicDust()
    {
        IsHitTestVisible = false;
    }
    double Next() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216d; }

    // The band (this element's own size) is set by CinematicScene.Place: width/height, its top-left in window coordinates and the
    // window size (depth of a level = how far down the window it lies). A change reseeds so grains fill it at once.
    public void Resize(double w, double h, double windowLeft, double windowTop, double windowW, double windowH)
    {
        if (Math.Abs(w - width) < .5 && Math.Abs(h - height) < .5 && Math.Abs(windowLeft - originX) < .5 && Math.Abs(windowTop - originY) < .5 && Math.Abs(windowH - windowHeight) < .5 && Math.Abs(windowW - windowWidth) < .5) return;
        width = w; height = h; originX = windowLeft; originY = windowTop; windowWidth = Math.Max(300, windowW); windowHeight = Math.Max(200, windowH); seeded = false;
    }
    // Stone geometry in window coordinates (same numbers CinematicScene.Place uses for its light layers).
    public void SetStone(double centreX, double centreY, double radius, double contact) { stoneX = centreX; stoneY = centreY; stoneR = radius; contactY = contact; }
    // The scene's light state: the pool on the ground, the halo/spill and the sideways shift of the pool (CinematicLight).
    public void SetLight(double poolLevel, double haloLevel, double lateralShift) { pool = Math.Clamp(poolLevel, 0, 1); halo = Math.Clamp(haloLevel, 0, 1); lateral = lateralShift; }
    public void Restart() { seeded = false; seed = 0xC2B2AE35; }

    // How lit the ground is at a window point, 0 (shadow) .. 1 (lit), from the SAME light layers CinematicScene draws on the
    // ground (pool + soft bounce + halo + spill; contact occlusion under the stone) and its vignette (darker to the left and
    // toward the bottom), so grains are as dark or as bright as the ground they lie on.
    static double Falloff(double d2) { if (d2 >= 1) return 0; double f = 1 - d2; return f * f; } // the scene's radial brush: (1 - o²)²
    double Illumination(double x, double y)
    {
        double r = stoneR, dx = x - stoneX, ex = x - (stoneX + .4 * r * lateral);
        double poolL = Falloff((ex * ex) / (2.9 * r * 2.9 * r) + ((y - (contactY - .04 * r)) * (y - (contactY - .04 * r))) / (.8 * r * .8 * r));
        double softL = Falloff((dx * dx) / (1.7 * r * 1.7 * r) + ((y - (contactY + .05 * r)) * (y - (contactY + .05 * r))) / (.35 * r * .35 * r));
        double dy = y - stoneY, haloL = Falloff((dx * dx + dy * dy) / (1.6 * r * 1.6 * r)), spillL = Falloff((dx * dx) / (3.3 * r * 3.3 * r) + (dy * dy) / (3.15 * r * 3.15 * r));
        double lit = .3 + .6 * pool * poolL + .35 * pool * softL + .4 * halo * haloL + .3 * halo * spillL;
        double oy = y - (contactY - .02 * r), occ = Falloff((dx * dx) / (.62 * r * .62 * r) + (oy * oy) / (.1 * r * .1 * r));
        // Vignette of the scene: radial around the sun (upper right), plus a left-edge gradient.
        double u = (x - .86 * windowWidth) / (1.05 * windowWidth), v = (y - .02 * windowHeight) / (1.2 * windowHeight), t = Math.Min(1, Math.Sqrt(u * u + v * v));
        double radial = t < .32 ? .15 * t / .32 : t < .66 ? .15 + .29 * (t - .32) / .34 : .44 + .22 * (t - .66) / .34;
        double fx = x / windowWidth, left = fx < .32 ? .38 - .29 * fx / .32 : fx < .55 ? .09 * (1 - (fx - .32) / .23) : 0;
        return Math.Clamp(lit * (1 - .8 * occ) * (1 - radial) * (1 - left), 0, 1);
    }

    // Gusts of the ground sand (own, so the dust can have strong short-lived gusts without touching the desert's wind). Each is a
    // front that sweeps rightward across the band (Front DIP/s) with a sin² envelope, a direction angle (mostly right, up to ~35°
    // either way, drifting while it lasts so grains curve) and a strength. 95 % of gusts blow right in varied paths; of the
    // rest, a quick sharp gust to the LEFT or a lull (shelter: the wind nearly drops) - 3 % and 2 %.
    struct Gust { public double Start, Length, Amp, Angle, Turn; public byte Kind; } // Kind 0 right, 1 left, 2 lull
    readonly Gust[] gusts = new Gust[5];
    double time, nextGust = .4;
    const double Front = 520, Top = 2.2;
    void Schedule()
    {
        while (time + 2 > nextGust)
        {
            int slot = 0; for (int i = 1; i < gusts.Length; i++) if (gusts[i].Start + gusts[i].Length < gusts[slot].Start + gusts[slot].Length) slot = i;
            ref var q = ref gusts[slot]; double roll = Next();
            q.Start = nextGust;
            if (roll < .95) { q.Kind = 0; q.Length = 1.4 + 2.8 * Next(); q.Amp = .35 + .65 * Next(); q.Angle = (Next() - .45) * 1.2; q.Turn = (Next() - .5) * .5; }
            else if (roll < .98) { q.Kind = 1; q.Length = .5 + .6 * Next(); q.Amp = .55 + .35 * Next(); q.Angle = (Next() - .5) * .5; q.Turn = (Next() - .5) * .6; }
            else { q.Kind = 2; q.Length = 1.5 + 1.8 * Next(); q.Amp = .8 + .15 * Next(); q.Angle = 0; q.Turn = 0; }
            nextGust += .5 + 2.2 * Next(); // gusts overlap, so the paths differ from grain to grain
        }
    }
    // Wind at a point as (vx, vy) in units of DesertWind.Base: calm base plus the gusts passing it, damped by lulls.
    (double X, double Y) WindAt(double x, double y, double calm)
    {
        double wx = calm, wy = calm * DesertWind.Slope, shelter = 0;
        for (int i = 0; i < gusts.Length; i++)
        {
            ref var q = ref gusts[i]; double s = (time - x / Front - q.Start) / q.Length;
            if (s <= 0 || s >= 1) continue;
            double e = Math.Sin(Math.PI * s); e *= e;
            if (q.Kind == 2) { shelter = Math.Max(shelter, q.Amp * e); continue; }
            double angle = q.Kind == 1 ? Math.PI + q.Angle + q.Turn * s * q.Length : q.Angle + q.Turn * s * q.Length + .22 * Math.Sin(.017 * y + .6 * time + q.Start);
            wx += Top * q.Amp * e * Math.Cos(angle); wy += Top * q.Amp * e * Math.Sin(angle);
        }
        double open = 1 - .93 * shelter; return (wx * open, wy * open);
    }

    // Ground levels: spread over the lower ~55 % of the band (the ground at and below the stone's base). Depth comes from the
    // level's position in the WINDOW (the background art's ground: ~54 % of the window height is the horizon, the nearest ground
    // is at the bottom), so lower = nearer = larger, faster and slightly higher-flying.
    void BuildLanes()
    {
        for (int k = 0; k < Lanes; k++)
        {
            double f = .42 + .53 * (k + .35 * (Next() - .5)) / (Lanes - 1);
            laneY[k] = height * Math.Clamp(f, .4, .96);
            lanePhase[k] = 6.28 * Next();
        }
    }
    // Height of a level's ground at x: gentle ripples (larger and slower for near levels).
    double GroundY(double x, in Grain g) => g.Y0 + g.Scale * (3.2 * Math.Sin(.012 * x + lanePhase[g.Lane]) + 1.6 * Math.Sin(.031 * x + 2.3 * lanePhase[g.Lane]));

    void Spawn(ref Grain g, bool anywhere)
    {
        g.Lane = (byte)(Next() * Lanes);
        // Each grain sits at its OWN depth around its level (so resting grains never line up on one ground curve).
        g.Y0 = Math.Clamp(laneY[g.Lane] + (Next() - .5) * height * .17, .38 * height, .97 * height);
        g.Depth = Math.Clamp((originY + g.Y0 - .54 * windowHeight) / (.25 * windowHeight), 0, 1); g.Scale = .6 + 1.0 * g.Depth; double s = g.Scale;
        g.X = anywhere ? width * Next() : -10 - 40 * Next();
        g.Mass = .5 + Math.Pow(Next(), 1.5); // 0.5 .. 1.5
        g.Size = (.19 + .19 * g.Mass) * s;     // drawn radius: heavier grains are a bit larger; near levels larger
        g.Shine = .6 + .4 * Next(); g.Phase = 6.28 * Next(); g.Ra = (float)(.8 + .4 * Next()); g.Rb = (float)(.8 + .4 * Next());
        g.ZMax = (45 + 70 * Next()) * s; g.Vx = g.Vz = 0;
        g.Life = 10 + 16 * Next(); g.Age = anywhere ? g.Life * Next() * .8 : 0;
        bool air = anywhere ? Next() < .4 : Next() < .5; g.Mode = air ? Air : Ground; g.Z = air ? g.ZMax * .6 * Next() : 0;
    }

    public void Advance(double dt, CaptureAtmosphere atmosphere)
    {
        if (width <= 0 || height <= 0) return;
        if (!seeded)
        {
            time = 0; nextGust = .4; Array.Clear(gusts); for (int i = 0; i < gusts.Length; i++) gusts[i].Start = -99; Schedule();
            BuildLanes(); for (int i = 0; i < Count; i++) Spawn(ref grains[i], true); seeded = true;
        }
        time += dt; Schedule();
        for (int i = 0; i < Count; i++)
        {
            ref var g = ref grains[i];
            double scale = g.Scale, near = .65 + .7 * g.Depth;
            double gy = GroundY(g.X, in g), strength = atmosphere.WindStrength(g.X);
            var (wx, wy) = WindAt(g.X, gy, .55 * strength);
            double windPx = DesertWind.Base * wx * near * (.9 + .25 * Math.Sin(g.Phase));      // what the air carries here (DIP/s)
            double carry = Math.Clamp(Math.Sqrt(wx * wx + wy * wy) / WindRef, 0, 1), up = Math.Clamp((carry - .55) / .45, 0, 1);
            g.Age += dt * (g.Mode == Ground ? .5 : 1);
            if (g.Mode == Air)
            {
                g.Vx += (windPx - g.Vx) * (1 - Math.Exp(-dt / (.2 + .35 * g.Mass)));          // heavier = slower to follow the wind
                double fall = (14 + 30 * g.Mass) * scale;                                      // terminal fall speed
                double lift = (LiftSpeed * up * Math.Max(0, 1 - g.Z / g.ZMax) + Math.Max(0, -wy) * DesertWind.Base * .1) * scale * (1.15 - .4 * g.Mass);
                g.Vz += ((lift - fall) - g.Vz) * (1 - Math.Exp(-dt / (.22 + .2 * g.Mass)));
                g.Z += g.Vz * dt; g.X += g.Vx * dt;
                if (g.Z <= 0)
                {
                    g.Z = 0;
                    if (g.Vz < -8 * scale) { g.Vz = -g.Vz * (.2 + .25 * Next()); g.Vx *= .65; g.Mode = g.Vz > 4 * scale ? Air : Ground; } // lands: a damped bounce, loses speed
                    else { g.Vz = 0; g.Mode = Ground; }
                }
            }
            else
            {
                // Rolls when the wind is above this grain's threshold, rests otherwise; strong wind kicks it back into the air.
                double threshold = .5 + .4 * g.Mass, roll = wx > threshold ? windPx * .32 * (wx - threshold) / (wx + .3) : 0;
                g.Vx += (roll - g.Vx) * (1 - Math.Exp(-dt / .5)); g.X += g.Vx * dt; g.Z = 0;
                double lifting = Math.Max(0, carry - .55) * 2.4 * (1.25 - .5 * g.Mass) + .02;
                if (Next() < lifting * dt) { g.Mode = Air; g.Vz = (20 + 34 * Next()) * scale * (1.15 - .4 * g.Mass); g.Z = .01; }
            }
            if (g.X > width + 12 || g.X < -70 || g.Age >= g.Life) Spawn(ref g, false);
        }
        Paint();
    }

    // Painting: all grains are splatted into one premultiplied BGRA bitmap (sub-pixel positions, so motion stays smooth at 60/s),
    // which WPF draws as a single image. That costs a small memory clear/copy per frame instead of rebuilding ~1000 vector
    // shapes (the geometry version cost ~70 % of a core at 60/s).
    uint[] pixels = [];
    WriteableBitmap? bitmap;
    double scaleX = 1, scaleY = 1;
    int pw, ph;
    void EnsureBitmap()
    {
        var dpi = VisualTreeHelper.GetDpi(this); int w = Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)), h = Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY));
        if (bitmap != null && w == pw && h == ph) return;
        pw = w; ph = h; scaleX = dpi.DpiScaleX; scaleY = dpi.DpiScaleY; pixels = new uint[w * h];
        bitmap = new WriteableBitmap(w, h, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32, null); InvalidateVisual();
    }
    static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    void Over(int x, int y, double r, double g, double b, double a) // source-over of a premultiplied colour onto the buffer
    {
        if ((uint)x >= (uint)pw || (uint)y >= (uint)ph || a <= .004) return;
        ref uint d = ref pixels[y * pw + x]; double ia = 1 - a;
        uint da = d >> 24, dr = (d >> 16) & 255, dg = (d >> 8) & 255, db = d & 255;
        uint na = (uint)Math.Min(255, a * 255 + da * ia + .5), nr = (uint)Math.Min(255, r * a + dr * ia + .5), ng = (uint)Math.Min(255, g * a + dg * ia + .5), nb = (uint)Math.Min(255, b * a + db * ia + .5);
        d = na << 24 | nr << 16 | ng << 8 | nb;
    }
    void Paint()
    {
        EnsureBitmap(); Array.Clear(pixels);
        for (int i = 0; i < Count; i++)
        {
            ref var g = ref grains[i];
            double gy = GroundY(g.X, in g), y = gy - g.Z;
            // Fade by the DRAWN position (top edge: y), the sides and the age; resting grains are a little dimmer.
            double edgeX = Ease(Math.Min(g.X, width - g.X) / (.14 * width)), edgeY = Ease((y - .03 * height) / (.2 * height));
            double alpha = g.Shine * Ease(g.Age / 1.2) * Ease((g.Life - g.Age) / 1.5) * edgeX * edgeY * (g.Mode == Ground ? .8 : 1) * (.8 + .2 * g.Depth);
            if (alpha < .03) continue;
            // Colour follows the ground it lies on. Measured on rendered frames (stone active and inactive), the ground's red channel
            // is ~ 20 + 250 x (lit - .07) (green ~ .52 R, blue ~ .25 R), so a grain is that ground colour made paler and more neutral
            // (x1.9 / x2.4 / x3.2): about 1.9x the ground's brightness, never a glow on black (dark ground -> dark, faint grain).
            double lit = Math.Clamp(Illumination(originX + g.X, originY + gy) + (g.Mode == Air ? .04 : 0), 0, 1);
            double gr = 20 + 250 * Math.Max(0, lit - .07), gg = .52 * gr + 3, gb = .25 * gr + 4;
            double cr = Math.Min(255, gr * 1.9), cg = Math.Min(255, gg * 2.4), cb = Math.Min(255, gb * 3.2);
            double a = Math.Min(1, alpha * 1.25 * (.3 + .7 * Math.Min(1, 3 * lit))), px = g.X * scaleX, py = y * scaleY, rad = g.Size * scaleX;
            if (rad < .75)
            {   // sub-pixel grain: energy spread bilinearly over four pixels
                double e = a * Math.Clamp(rad * rad * 3.2, .6, 1),  fx = px - .5, fy = py - .5; int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); double u = fx - x0, v = fy - y0;
                Over(x0, y0, cr, cg, cb, e * (1 - u) * (1 - v)); Over(x0 + 1, y0, cr, cg, cb, e * u * (1 - v)); Over(x0, y0 + 1, cr, cg, cb, e * (1 - u) * v); Over(x0 + 1, y0 + 1, cr, cg, cb, e * u * v);
            }
            else
            {   // larger (near) grain: a soft disc, coverage by distance
                int x0 = (int)Math.Floor(px - rad - 1), x1 = (int)Math.Ceiling(px + rad + 1), y0 = (int)Math.Floor(py - rad - 1), y1 = (int)Math.Ceiling(py + rad + 1);
                for (int yy = y0; yy <= y1; yy++) for (int xx = x0; xx <= x1; xx++)
                {
                    double dx = xx + .5 - px, dy = yy + .5 - py, cov = Math.Clamp(rad + .5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                    if (cov > 0) Over(xx, yy, cr, cg, cb, a * cov);
                }
            }
        }
        bitmap!.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, pw * 4, 0);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (bitmap != null) dc.DrawImage(bitmap, new Rect(0, 0, width, height));
    }
}
