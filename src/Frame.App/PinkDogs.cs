using System.Windows;
using System.Windows.Media;

namespace Frame.App;

// Pink mode effect "dogs" (0.6.0): three small illustrated dogs living on PinkScene's ground line. One IPinkLayer, advanced
// only by the scene clock (FullRate: every composition frame, dt <= 0.1 s, substepped at <= 1/60 s); no timers, storyboards
// or own rendering callbacks. Each dog = PinkDogRig (articulated vector rig, transforms only) + PinkDog (behaviour state
// machine, steering, ballistic jumps, procedural gait, spring-damper secondary motion). Deterministic: every random choice
// uses PinkWorld.Random, every motion is integrated from dt. Interactions use PinkWorld: reads "butterfly", "heart", "petal"
// things and the pointer; publishes one "dog" thing per dog; raises "heart-caught" (and "treat-eaten"); reacts to
// "secret-treat" and other "secret*" signals.
sealed class PinkDogsLayer : IPinkLayer
{
    sealed class Stage : FrameworkElement
    {
        readonly VisualCollection children;
        public Stage() { children = new VisualCollection(this); IsHitTestVisible = false; Focusable = false; }
        public void Add(Visual v) => children.Add(v);
        protected override int VisualChildrenCount => children.Count;
        protected override Visual GetVisualChild(int index) => children[index];
    }
    readonly Stage stage = new();
    readonly PinkDog[] dogs;
    PinkWorld? world; bool enabled = true, ready, published;
    readonly (string Name, Point At, PinkThing? Thing)[] queue = new (string, Point, PinkThing?)[8]; int queued;
    Point lastPointer; bool hadPointer; double playCd = -1, playCheck;
    internal PinkDog? Curious;
    internal bool PointerInBand; internal Point PointerAt; internal double PointerSpeed;
    internal bool TreatOpen; internal Point TreatAt; PinkThing? treatThing;
    internal double PlayLeft;
    // Diagnostics/test scenes only: seconds of calm before a dog naps (< 0: natural 35–70 s).
    internal double SleepAfter { get; set; } = -1;
    internal PinkDog[] Dogs => dogs;
    internal static long ProbeThink, ProbeStep, ProbePresent, ProbeApply;
    public string Effect => "dogs";
    public bool FullRate => true;
    public FrameworkElement Visual => stage;
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value; stage.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (!value && world != null && published) { foreach (var d in dogs) world.Things.Remove(d.Thing); published = false; }
        }
    }

    public PinkDogsLayer()
    {
        // Lanes give depth: Rosie walks a little further back, Mochi in front; drawn back to front.
        dogs = [new PinkDog(this, DogLook.Pack[1], -9, 1), new PinkDog(this, DogLook.Pack[2], -4.5, 2), new PinkDog(this, DogLook.Pack[0], 0, 0)];
        foreach (var d in dogs) stage.Add(d.Rig.Root);
    }

    public void Advance(double dt, PinkWorld w)
    {
        if (!enabled || !Ensure(w)) return;
        if (dt < 1e-5) { foreach (var d in dogs) d.Present(); return; } // a clock restart (dt = 0): pose only
        int n = Math.Max(1, (int)Math.Ceiling(dt * 60 - 1e-6)); double h = dt / n;
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++)
        {
            Think(h, w); long b1 = GC.GetAllocatedBytesForCurrentThread(); ProbeThink += b1 - b0;
            foreach (var d in dogs) d.Step(h);
            b0 = GC.GetAllocatedBytesForCurrentThread(); ProbeStep += b0 - b1;
        }
        foreach (var d in dogs) d.Present();
        ProbePresent += GC.GetAllocatedBytesForCurrentThread() - b0;
    }
    public void Present(PinkWorld w)
    {
        if (!enabled || !Ensure(w)) return;
        foreach (var d in dogs) d.Present(); // the last pose on the current ground line (no motion)
    }

    bool Ensure(PinkWorld w)
    {
        if (world != w) { if (world != null) world.Signal -= OnSignal; world = w; w.Signal += OnSignal; published = false; foreach (var d in dogs) d.Bind(w); }
        if (!ready)
        {
            if (w.Width < 200 || w.Height < 200) return false;
            ready = true;
            // Spread out over free ground; one sits, one stands, one is already wandering.
            double[] at = [.3, .55, .78]; PinkDog.Act[] start = [PinkDog.Act.Sit, PinkDog.Act.Walk, PinkDog.Act.Idle];
            for (int i = 0; i < dogs.Length; i++) dogs[i].Init(w, FreeX(w, w.Width * at[i] + (w.Random.NextDouble() - .5) * 80, dogs[i]), start[i]);
            if (playCd < 0) playCd = 14 + 10 * w.Random.NextDouble();
        }
        if (!published) { foreach (var d in dogs) w.Things.Add(d.Thing); published = true; }
        return true;
    }
    void OnSignal(string name, Point at, PinkThing? thing)
    {
        if (name is "heart-caught" or "treat-eaten" || !name.StartsWith("secret", StringComparison.Ordinal) || queued >= queue.Length) return;
        queue[queued++] = (name, at, thing);
    }

    // Layer-level coordination: pointer curiosity (one dog at a time, only near the ground band), play invitations and
    // secret signals. Runs once per substep before the dogs.
    void Think(double h, PinkWorld w)
    {
        for (int i = 0; i < queued; i++)
        {
            var (name, at, thing) = queue[i];
            if (name == "secret-treat") { TreatOpen = true; TreatAt = at; treatThing = thing; if (thing != null) thing.Claimed = this; foreach (var d in dogs) d.Notice(at, treat: true); }
            else { PinkDog? best = null; foreach (var d in dogs) if (d.Awake && (best == null || Math.Abs(d.X - at.X) < Math.Abs(best.X - at.X))) best = d; best?.Notice(at, treat: false); foreach (var d in dogs) if (d != best) d.Glance(at); }
            queue[i] = default;
        }
        queued = 0;
        // Pointer: in the band just above the ground (not over FRAME's controls), with its smoothed speed.
        var p = w.Pointer; PointerInBand = false;
        if (p is Point q)
        {
            if (hadPointer) PointerSpeed += ((q - lastPointer).Length / h - PointerSpeed) * Math.Min(1, h * 6);
            lastPointer = q; hadPointer = true; PointerAt = q;
            PointerInBand = q.Y >= w.GroundY - 175 && q.Y <= w.GroundY + 6 && q.X >= 0 && q.X <= w.Width;
            if (PointerInBand && Blocked(w, new Rect(q.X - 1, q.Y - 1, 2, 2))) PointerInBand = false;
        }
        else { hadPointer = false; PointerSpeed = 0; }
        if (PointerInBand)
        {
            foreach (var d in dogs) if (d.State == PinkDog.Act.Sleep && PointerSpeed > 35 && Math.Abs(d.X - PointerAt.X) < 150) d.WakeUp();
            if (Curious == null)
            {
                PinkDog? best = null;
                foreach (var d in dogs) if (d.CanAttend && Math.Abs(d.X - PointerAt.X) < 420 && (best == null || Math.Abs(d.X - PointerAt.X) < Math.Abs(best.X - PointerAt.X))) best = d;
                if (best != null) { Curious = best; best.StartCurious(); }
            }
        }
        // Play: now and then two calm, awake dogs start a chase game (play bow first).
        if (PlayLeft > 0) PlayLeft -= h;
        playCd -= h; playCheck -= h;
        if (playCd <= 0 && playCheck <= 0)
        {
            playCheck = 2;
            PinkDog? a = null, b = null; int k = w.Random.Next(dogs.Length);
            for (int i = 0; i < dogs.Length && b == null; i++)
            {
                var x = dogs[(k + i) % dogs.Length];
                if (!x.Playful) continue;
                if (a == null) a = x; else if (Math.Abs(x.X - a.X) < 520) b = x;
            }
            if (a != null && b != null && w.Random.NextDouble() < .45) { a.StartInvite(b); playCd = 35 + 35 * w.Random.NextDouble(); }
        }
    }
    internal void ForcePlay(int a, int b) { dogs[a].StartInvite(dogs[b]); playCd = 60; }
    internal void TreatEaten(PinkDog by)
    {
        if (!TreatOpen || world == null) return;
        TreatOpen = false;
        if (treatThing != null) { treatThing.Remove = true; treatThing.Claimed = null; }
        world.Raise("treat-eaten", TreatAt, treatThing); treatThing = null;
    }
    // Keepout test that ignores page-sized containers (a ScrollViewer or panel covering most of the window would otherwise
    // forbid every spot): only rectangles under 30 % of the window area count as FRAME controls/text.
    internal static bool Blocked(PinkWorld w, Rect area)
    {
        double big = w.Width * w.Height * .3;
        foreach (var r in w.Keepout) if (r.Width * r.Height < big && r.IntersectsWith(area)) return true;
        return false;
    }
    internal bool Targeted(PinkThing t, PinkDog except) { foreach (var d in dogs) if (d != except && d.Target == t) return true; return false; }
    // Nearest x in [x0 ± 360] where a dog may rest (not in a Keepout rectangle, not on top of another dog).
    internal double FreeX(PinkWorld w, double x0, PinkDog dog)
    {
        double lo = dog.MinX, hi = dog.MaxX;
        for (int i = 0; i < 24; i++)
        {
            double x = Math.Clamp(x0 + (i % 2 == 0 ? 1 : -1) * 30 * ((i + 1) / 2), lo, hi);
            if (dog.RestFree(x)) return x;
        }
        return Math.Clamp(x0, lo, hi);
    }
}

sealed class PinkDog
{
    public enum Act { Idle, Walk, Sniff, Sit, Scratch, Lie, Sleep, Wake, Chase, Rear, Watch, Heart, Happy, Curious, Seek, Eat, Invite, Bow, PlayRun, PlayChase }
    enum W { Sit, Lie, Curl, Bow, Rear, Sniff, Crouch, Scratch, Air }
    const double Gravity = 1500, MaxJump = 92;                     // DIP/s², highest jump (DIP): snappy cartoon arcs
    static readonly double[] Omega = [6.5, 4.2, 2.2, 8.5, 9, 7.5, 22, 11, 16]; // pose blend springs (rad/s, critically damped)
    readonly PinkDogsLayer layer;
    public readonly PinkDogRig Rig;
    readonly DogPose pose = new();
    public readonly PinkThing Thing;
    public readonly double Lane;
    readonly double s;
    readonly int id;
    PinkWorld w = null!; Random rng = new(1);
    // Locomotion
    public double X, Vx, Lift; public bool Air;
    double face = 1, headFace = 1, faceGoal = 1, turnU = 1, turnFrom = 1, turnDur = .3, liftV, prepT, prepH, prepVx, landT, squash, squashV, jumpCd;
    double phase, speed, gait, freq = 1, accel, lastVx;
    bool hasGoal; double goalX, goalSpeed, stopDist = 3; int wantFace;
    // Behaviour
    public Act State = Act.Idle; double actT, actDur = 2, calm, sleepAfter, senseT, limit;
    public PinkThing? Target; Point spot; PinkDog? mate; int leaps; bool treatSeek, pendingSeek; Point pendingAt;
    double chaseCd = 3, heartCd = 1, petalCd = 4, pointerCd, noRest, heartWait, twitchT = 5, idleLookT, yawnT = -1, tagCd, hopT;
    // Pose blending (current, velocity, target) and secondary motion
    readonly double[] wv = new double[9], wvel = new double[9], wt = new double[9];
    double headAng, headAngV, headTarget, lookW, lookWT, tilt, tiltT, lookX, lookY; Point? look;
    double earN, earNV, earF, earFV, perk, perkT, droop, droopT;
    double wagPh, wagAmp, wagAmpT = 8, wagFreq = 2, wagFreqT = 2, tailUp = .5, tailUpT = .5, tailIn, tailInV;
    double blinkT = 2, blinkAge = 9, eyeBase = 1, eyeBaseT = 1, happyT, sleepEyes, sleepEyesT;
    double breathPh, breathRate = .85, breathAmp = .016, mouth, mouthT, pant, chompT = -1, snapT = -1; PinkThing? snapAt;
    double zClock; readonly double[] zAge = [-1, -1, -1]; int zNext, emote = -1; double emoteAge;

    public PinkDog(PinkDogsLayer layer, DogLook look, double lane, int id)
    {
        this.layer = layer; Rig = new PinkDogRig(look); Lane = lane; s = look.Scale; this.id = id;
        Thing = new PinkThing { Kind = "dog", Owner = layer, Size = 28 * s };
    }
    public double MinX => 30 + 34 * s;
    public double MaxX => Math.Max(MinX + 1, w.Width - 30 - 34 * s);
    double Ground => w.GroundY + Lane;
    double MouthHeight => Ground - Rig.Mouth.Y - Lift; // mouth above the ground when standing (DIP)
    public bool Awake => State is not (Act.Sleep or Act.Wake);
    public bool CanAttend => pointerCd <= 0 && State is Act.Idle or Act.Walk or Act.Sniff or Act.Sit or Act.Lie or Act.Watch or Act.Scratch;
    public bool Playful => pant < .45 && State is Act.Idle or Act.Walk or Act.Sit or Act.Watch or Act.Sniff;
    public bool RestFree(double x) { var r = new Rect(x - 38 * s, Ground - 62 * s, 76 * s, 62 * s); if (PinkDogsLayer.Blocked(w, r)) return false; foreach (var d in layer.Dogs) if (d != this && Math.Abs(d.X - x) < 64 && d.State is Act.Sit or Act.Lie or Act.Sleep or Act.Idle) return false; return true; }
    double Rnd(double a, double b) => a + (b - a) * rng.NextDouble();

    public void Bind(PinkWorld world) { w = world; rng = world.Random; }
    public void Init(PinkWorld world, double x, Act start)
    {
        Bind(world); X = x; face = headFace = faceGoal = rng.NextDouble() < .5 ? -1 : 1;
        sleepAfter = layer.SleepAfter >= 0 ? layer.SleepAfter : Rnd(35, 70);
        phase = rng.NextDouble(); wagPh = Rnd(0, 6.3); breathPh = Rnd(0, 6.3); blinkT = Rnd(.4, 3); twitchT = Rnd(3, 8);
        headAng = 0; earN = Rig.Look.Ears == DogEars.Pointy ? -10 : 16; earF = Rig.Look.Ears == DogEars.Pointy ? 8 : -26;
        if (start == Act.Walk) Wander(Rnd(40, 55)); else Set(start, Rnd(2, 5));
        if (start == Act.Sit) { wv[(int)W.Sit] = wt[(int)W.Sit] = 1; }
        Present();
    }
    void Set(Act a, double dur) { State = a; actT = 0; actDur = dur; }
    // Diagnostics/test scenes only (scripted clips): walk/trot/run to x, sit, nap, jump (height x), idle.
    internal double Face => face;
    internal double Goal => hasGoal ? goalX : -1;
    internal DogPose Pose => pose;
    internal void Command(string cmd, double x = 0)
    {
        switch (cmd)
        {
            case "walk" or "trot" or "run": goalX = Math.Clamp(x, MinX, MaxX); goalSpeed = cmd == "walk" ? 45 : cmd == "trot" ? 115 : 225; stopDist = 3; Set(Act.Walk, 30); break;
            case "sit": Set(Act.Sit, x > 0 ? x : 6); break;
            case "nap": calm = sleepAfter + 1; Set(Act.Lie, x > 0 ? x : 2.5); break;
            case "jump": Jump(x, Vx); break;
            case "chase": chaseCd = 0; senseT = 0; break;
            default: Set(Act.Idle, x > 0 ? x : 2); break;
        }
    }

    // ---------------------------------------------------------------- behaviour
    public void Step(double h)
    {
        rng = w.Random; actT += h; Timers(h);
        hasGoal = false; wantFace = 0; look = null; lookWT = 0; tiltT = 0; perkT = 0; droopT = 0; eyeBaseT = 1; sleepEyesT = 0;
        for (int i = 0; i < wt.Length; i++) wt[i] = 0;
        wagAmpT = 9; wagFreqT = 2; tailUpT = .55; mouthT = 0;
        if ((senseT -= h) <= 0) { senseT = .2; Sense(); }
        Behave(h);
        Locomotion(h);
        Secondary(h);
    }
    void Timers(double h)
    {
        chaseCd -= h; heartCd -= h; petalCd -= h; pointerCd -= h; noRest -= h; jumpCd -= h; tagCd -= h; happyT -= h; hopT -= h;
        if (State is Act.Idle or Act.Sit or Act.Lie or Act.Sniff or Act.Watch or Act.Scratch || State == Act.Walk && goalSpeed < 70) calm += h; else if (State != Act.Sleep) calm = 0;
        pant = Math.Clamp(pant + (speed > 150 ? .22 : -.045) * h, 0, 1);
    }
    // Things worth reacting to, checked 5 times a second (hearts > butterflies > petals); pointer/treats come from the layer.
    void Sense()
    {
        if (Target != null && (Target.Remove || !w.Things.Contains(Target))) { if (State is Act.Chase or Act.Rear or Act.Heart) GiveUp(); Target = null; }
        if (!Awake) { if (State == Act.Sleep) foreach (var t in w.Things) if (t.Kind == "butterfly" && (t.Position - Rig.HeadCenter).Length < 70) twitchT = Math.Min(twitchT, .05); return; }
        bool busy = State is Act.Heart or Act.Seek or Act.Eat or Act.Invite or Act.Bow or Act.PlayRun or Act.PlayChase or Act.Happy;
        if (!busy && heartCd <= 0 && FindHeart() is { } heart) { StartHeart(heart); return; }
        if (busy || State is Act.Chase or Act.Rear) return;
        if (chaseCd <= 0 && State != Act.Curious && FindButterfly() is { } b)
        {
            chaseCd = 1.5;
            if (rng.NextDouble() < .7) StartChase(b); else { Target = b; Watch(Rnd(1.5, 3)); Emote(1); }
            return;
        }
        if (petalCd <= 0 && snapT < 0 && !Air)
            foreach (var t in w.Things)
                if (t.Kind == "petal" && t.Velocity.Y > -5 && Math.Abs(t.Position.X - Rig.Mouth.X) < 70 && t.Position.Y > Rig.Mouth.Y - 60 && t.Position.Y < Rig.Mouth.Y + 4 && (t.Position.X - X) * face > -10)
                {
                    petalCd = Rnd(2.5, 6);
                    if (rng.NextDouble() < .55) { snapT = 0; snapAt = t; if (t.Position.Y < Rig.Mouth.Y - 22 && rng.NextDouble() < .5 && State is Act.Idle or Act.Watch or Act.Walk) Jump(Math.Min(30, Rig.Mouth.Y - t.Position.Y), Vx * .5); }
                    break;
                }
    }
    PinkThing? FindHeart()
    {
        PinkThing? best = null; double bd = 1e9;
        foreach (var t in w.Things)
        {
            if (t.Kind != "heart" || t.Remove || (t.Claimed != null && t.Claimed != layer) || layer.Targeted(t, this)) continue;
            double hb = Ground - t.Position.Y, dx = Math.Abs(t.Position.X - X);
            if (hb < 6 || hb > 230 || dx > 420 || t.Position.X < MinX - 20 || t.Position.X > MaxX + 20) continue;
            if (dx + hb < bd) { bd = dx + hb; best = t; }
        }
        return best;
    }
    PinkThing? FindButterfly()
    {
        PinkThing? best = null; double bd = 1e9;
        foreach (var t in w.Things)
        {
            if (t.Kind != "butterfly" || t.Remove || (t.Claimed != null && t.Claimed != layer) || layer.Targeted(t, this)) continue;
            double hb = Ground - t.Position.Y, dx = Math.Abs(t.Position.X - X);
            if (hb < -5 || hb > 190 || dx > 380 || t.Position.X < MinX - 30 || t.Position.X > MaxX + 30) continue;
            if (dx + hb * 1.5 < bd) { bd = dx + hb * 1.5; best = t; }
        }
        return best;
    }

    void Behave(double h)
    {
        switch (State)
        {
            case Act.Idle:
                Calm(); LookAround(h);
                if (noRest <= 0 && actT > .4 && !RestFree(X)) // not here (over FRAME's controls/another dog): walk to a free spot
                {
                    double fx = layer.FreeX(w, X, this);
                    if (!RestFree(fx)) noRest = 8; else { Wander(Rnd(45, 60)); goalX = fx; break; }
                }
                if (actT > actDur) NextIdle();
                break;
            case Act.Walk:
                Calm(); hasGoal = true; if (goalSpeed > 160) { tailUpT = .7; wagAmpT = 6; } else LookAround(h);
                if (Math.Abs(goalX - X) < 4 && Math.Abs(Vx) < 10)
                {
                    if (goalSpeed > 160 && rng.NextDouble() < .45) { Wander(Rnd(200, 240), far: true); break; } // zoomies: and back again
                    Set(Act.Idle, Rnd(1.5, 4));
                }
                break;
            case Act.Sniff:
                Calm(); wt[(int)W.Sniff] = 1; wagAmpT = 5; tailUpT = .4; if (actT > actDur) Set(Act.Idle, Rnd(1, 2.5));
                break;
            case Act.Sit:
                Calm(); wt[(int)W.Sit] = 1; LookAround(h); wagAmpT = 6; wagFreqT = 1.6;
                if (actT > actDur)
                {
                    if (calm > sleepAfter * .6 && RestFree(X) && rng.NextDouble() < .6) Set(Act.Lie, Rnd(3, 5));
                    else if (rng.NextDouble() < .3) Set(Act.Scratch, Rnd(1.3, 2));
                    else Set(Act.Idle, Rnd(.8, 2));
                }
                break;
            case Act.Scratch:
                wt[(int)W.Sit] = 1; wt[(int)W.Scratch] = actT > .25 && actT < actDur - .2 ? 1 : 0; eyeBaseT = .45; wagAmpT = 3;
                if (actT > actDur) Set(Act.Sit, Rnd(1.5, 3.5));
                break;
            case Act.Lie:
                wt[(int)W.Lie] = 1; wagAmpT = 0; tailUpT = .2; droopT = .6; eyeBaseT = calm > sleepAfter ? .5 : .85; LookAround(h);
                if (yawnT < 0 && actT > .9 && actT < 1) yawnT = 0;
                if (actT > actDur) { if (calm > sleepAfter && RestFree(X)) Set(Act.Sleep, Rnd(45, 90)); else Set(Act.Idle, Rnd(1, 2)); }
                break;
            case Act.Sleep:
                wt[(int)W.Lie] = 1; wt[(int)W.Curl] = 1; wagAmpT = 0; tailUpT = 0; droopT = 1; sleepEyesT = 1; eyeBaseT = 0;
                if (actT > actDur) WakeUp();
                break;
            case Act.Wake:
                // Lift the head and blink awake, stand up into a stretch (play-bow shape) with a yawn, then carry on.
                bool lying = actT < 1.1;
                wt[(int)W.Lie] = lying ? 1 : 0; wt[(int)W.Bow] = !lying && actT < 2.2 ? 1 : 0; eyeBaseT = Math.Min(1, .25 + actT * .6); droopT = lying ? .5 : 0;
                wagAmpT = actT > 1.6 ? 10 : 0; tailUpT = .45;
                if (yawnT < 0 && actT > 1.25 && actT < 1.3) yawnT = 0;
                if (actT > 2.6) { if (pendingSeek) { pendingSeek = false; Seek(pendingAt, treatSeek); } else Set(Act.Idle, Rnd(1, 3)); }
                break;
            case Act.Chase: Chase(); break;
            case Act.Rear:
                wt[(int)W.Rear] = 1; perkT = 1; wagAmpT = 16; wagFreqT = 4.5; tailUpT = .9; if (Target != null) look = Target.Position; lookWT = 1;
                if (actT > actDur) { if (Target != null) Set(Act.Chase, limit - actT); else Set(Act.Idle, 1); }
                break;
            case Act.Watch:
                if (Target != null && !Target.Remove) look = Target.Position; else if (spot != default) look = spot;
                lookWT = 1; perkT = .7; wagAmpT = 12; wagFreqT = 3; tailUpT = .7; if (actDur > 2.5 && actT > .6) wt[(int)W.Sit] = 1;
                if (look is Point lp && Math.Abs(lp.X - X) > 12) wantFace = Math.Sign(lp.X - X);
                if (actT > .9 && actT < 1.6) tiltT = id % 2 == 0 ? -10 : 10;
                if (actT > actDur) { Target = null; Set(Act.Idle, Rnd(1, 2.5)); }
                break;
            case Act.Heart: HeartAct(h); break;
            case Act.Happy:
                happyT = Math.Max(happyT, .1); wagAmpT = 26; wagFreqT = 6.5; tailUpT = 1; perkT = 1;
                if (actT > .35 && hopT < -10 && !Air) { Jump(16, 0); hopT = 0; }
                if (actT > actDur) { hopT = -20; Set(Act.Idle, Rnd(1.5, 3)); }
                break;
            case Act.Curious: CuriousAct(h); break;
            case Act.Seek:
                hasGoal = true; perkT = 1; tailUpT = .8; wagAmpT = 14; wagFreqT = 4; look = new Point(spot.X, Math.Max(spot.Y, Ground - 90)); lookWT = .6;
                if (Math.Abs(goalX - X) < 6 && Math.Abs(Vx) < 15)
                {
                    if (treatSeek && layer.TreatOpen && Math.Abs(layer.TreatAt.X - X) < 40) { Set(Act.Eat, 2.6); layer.TreatEaten(this); }
                    else { Set(Act.Sniff, Rnd(1, 1.6)); if (!treatSeek) { Emote(2); } }
                }
                if (actT > 10) Set(Act.Idle, 1);
                break;
            case Act.Eat:
                wt[(int)W.Sniff] = actT < 1.5 ? 1 : 0; wagAmpT = 18; wagFreqT = 5.5; tailUpT = .9;
                if (actT > .45 && actT < 1.45) mouthT = .5 + .5 * Math.Sin((actT - .45) * Math.PI * 6 - Math.PI / 2) * .9; // chomp, chomp, chomp
                if (actT > 1.5 && emote != 0) { Emote(0); happyT = 1.3; }
                if (actT > 1.75 && hopT < -10 && !Air) { Jump(14, 0); hopT = 0; }
                if (actT > actDur) { hopT = -20; Set(Act.Idle, Rnd(1.5, 3)); }
                break;
            case Act.Invite:
                if (mate == null || !mate.Awake || mate.State is Act.Heart or Act.Chase or Act.Seek or Act.Eat) { Set(Act.Idle, 1); break; }
                double side = Math.Sign(X - mate.X); if (side == 0) side = -face;
                goalX = Math.Clamp(mate.X + side * 66 * s, MinX, MaxX); goalSpeed = Math.Abs(goalX - X) > 120 ? 150 : 90; stopDist = 4; hasGoal = true; look = mate.Rig.HeadCenter; lookWT = .8; tailUpT = .8; wagAmpT = 14; wagFreqT = 4;
                if (Math.Abs(goalX - X) < 8 && Math.Abs(Vx) < 12 || actT > 8) { wantFace = Math.Sign(mate.X - X); Set(Act.Bow, 1.15); Emote(3); }
                break;
            case Act.Bow:
                if (mate != null) { wantFace = Math.Sign(mate.X - X); look = mate.Rig.HeadCenter; lookWT = 1; }
                wt[(int)W.Bow] = 1; wagAmpT = 24; wagFreqT = 6.5; tailUpT = 1; perkT = 1;
                if (actT > actDur && mate != null)
                {
                    if (mate.State != Act.Bow && mate.State != Act.PlayChase && mate.State != Act.PlayRun) { mate.mate = this; mate.Set(Act.Bow, .7); mate.Emote(3); Set(Act.Bow, .75); layer.PlayLeft = Rnd(7, 10); }
                    else { Set(Act.PlayRun, 99); RunAway(); mate.Set(Act.PlayChase, 99); mate.mate = this; }
                }
                break;
            case Act.PlayRun:
            case Act.PlayChase: Play(); break;
        }
    }
    void Calm() { wagAmpT = 8; wagFreqT = 2; tailUpT = .55; if (pant > .35) mouthT = .55; }
    void LookAround(double h)
    {
        if ((idleLookT -= h) <= 0)
        {
            idleLookT = Rnd(1.4, 3.4);
            spot = rng.NextDouble() < .35 ? default : new Point(X + face * Rnd(-60, 260), Ground - Rnd(0, 200));
            if (rng.NextDouble() < .12) tilt = (rng.NextDouble() < .5 ? -1 : 1) * 9;
        }
        // Pointer nearby in the ground band: everyone looks (only the curious dog walks over).
        if (layer.PointerInBand && Math.Abs(layer.PointerAt.X - X) < 320) { look = layer.PointerAt; lookWT = .8; return; }
        if (spot != default) { look = spot; lookWT = .55; }
    }
    void NextIdle()
    {
        if (calm > sleepAfter && RestFree(X)) { Set(Act.Lie, Rnd(3, 4.5)); return; }
        double r = rng.NextDouble();
        if (r < .34) Wander(Rnd(38, 55));
        else if (r < .5) Wander(Rnd(95, 125));
        else if (r < .56) Wander(Rnd(200, 240), far: true);
        else if (r < .68) Set(Act.Sniff, Rnd(1.2, 2.4));
        else if (r < .84) Set(Act.Sit, Rnd(4, 9));
        else if (r < .92) { wantFace = -(int)faceGoal; StartTurn(); Set(Act.Idle, Rnd(1.5, 3)); }
        else Set(Act.Idle, Rnd(1.5, 3.5));
    }
    void Wander(double speed, bool free = false, bool far = false)
    {
        double best = X;
        for (int i = 0; i < 10; i++)
        {
            double tx = far ? (X < (MinX + MaxX) / 2 ? Rnd(MaxX - 160, MaxX) : Rnd(MinX, MinX + 160)) : Rnd(MinX, MaxX);
            if (!far && Math.Abs(tx - X) < 110 && i < 8) continue;
            if (Math.Abs(tx - X) > 520 && speed < 70 && i < 8) continue;
            best = tx; if (RestFree(tx) || far && i > 2) break;
        }
        if (free) best = layer.FreeX(w, best, this);
        goalX = best; goalSpeed = speed; stopDist = 3; Set(Act.Walk, 30);
    }
    void Watch(double dur) { Set(Act.Watch, dur); }
    void Emote(int kind) { emote = kind; emoteAge = 0; }
    void GiveUp()
    {
        if (Target != null && Target.Claimed == layer && !layer.Targeted(Target, this)) Target.Claimed = null;
        bool chase = State is Act.Chase or Act.Rear; Watch(Rnd(2.2, 3.5)); if (chase) { chaseCd = Rnd(8, 16); Emote(2); } else heartCd = Rnd(3, 6);
    }

    void StartChase(PinkThing b) { Target = b; b.Claimed = layer; leaps = 0; limit = Rnd(6, 9); Set(Act.Chase, limit); Emote(1); calm = 0; if (State == Act.Sleep) WakeUp(); }
    void Chase()
    {
        var b = Target; if (b == null) { Set(Act.Idle, 1); return; }
        double hb = Ground - b.Position.Y, dx = b.Position.X - X;
        look = b.Position; lookWT = 1; perkT = 1; tailUpT = .85; wagAmpT = 14; wagFreqT = 5;
        hasGoal = true; goalX = Math.Clamp(b.Position.X + b.Velocity.X * .35, MinX, MaxX); goalSpeed = Math.Abs(dx) > 120 ? 215 : 150; stopDist = 6;
        if (b.Landed && hb < 40) // stalk a resting butterfly, then pounce
        {
            goalSpeed = Math.Abs(dx) > 110 ? 110 : 34; wt[(int)W.Crouch] = .55; tailUpT = .4; wagAmpT = 4;
            if (!Air && prepT <= 0 && jumpCd <= 0 && Math.Abs(dx) < 60 && dx * faceGoal > 0) { Jump(24, Math.Clamp(dx / .3, -200, 200)); jumpCd = 1.2; leaps++; }
        }
        else if (!Air && prepT <= 0 && jumpCd <= 0 && turnU >= 1 && Math.Abs(dx) < 85 && dx * faceGoal > 0 && hb > 16 && hb < MouthHeight + MaxJump)
        {
            double jh = Math.Clamp(hb - MouthHeight + 6, 12, MaxJump), ta = Math.Sqrt(2 * jh / Gravity);
            Jump(jh, Math.Clamp((b.Position.X + b.Velocity.X * ta - X) / ta, -260, 260)); jumpCd = Rnd(.7, 1.3); leaps++;
        }
        else if (!Air && prepT <= 0 && Math.Abs(dx) < 36 && hb > MouthHeight - 8 && hb < MouthHeight + 40 && actT > 1 && rng.NextDouble() < .012) { Set(Act.Rear, Rnd(1, 1.4)); return; }
        if (!Air && prepT <= 0 && (actT > actDur || hb > 250 || leaps >= 3 && jumpCd < .4)) GiveUp();
    }
    void StartHeart(PinkThing t) { Target = t; t.Claimed = layer; Set(Act.Heart, 10); calm = 0; perk = 1; }
    void HeartAct(double h)
    {
        var t = Target; if (t == null) { Set(Act.Idle, 1); return; }
        var hp = t.Position; double hb = Ground - hp.Y, mh = MouthHeight;
        look = hp; lookWT = 1; perkT = 1; tailUpT = .9; wagAmpT = 18; wagFreqT = 5;
        double jh = Math.Clamp(hb - mh + 4, 10, MaxJump), ta = Math.Sqrt(2 * jh / Gravity), px = hp.X + t.Velocity.X * (ta + .12);
        hasGoal = true; goalX = Math.Clamp(px - face * 4, MinX, MaxX); goalSpeed = Math.Abs(goalX - X) > 150 ? 230 : 140; stopDist = 2;
        if ((Rig.Mouth - hp).Length < t.Size + 9 * s + 2) // caught (in the air or a boop from the ground)
        {
            w.Raise("heart-caught", hp, t); t.Claimed = null; Target = null; Emote(0); happyT = 1.6; calm = 0; heartCd = Rnd(2, 4); mouth = 1; chompT = 0;
            Set(Act.Happy, Rnd(1.6, 2.2)); return;
        }
        bool under = Math.Abs(px - X) < 22; heartWait = under ? heartWait + h : 0;
        if (!Air && prepT <= 0 && jumpCd <= 0 && under && hb > mh + 4 && hb < mh + MaxJump)
        { Jump(jh, Math.Clamp((px - X) / ta, -180, 180)); jumpCd = .9; }
        else if (!Air && prepT <= 0 && jumpCd <= 0 && under && heartWait > 1.2 && hb < mh + MaxJump + 45) { Jump(MaxJump * Rnd(.55, .8), 0); jumpCd = Rnd(1.4, 2.2); } // can't reach yet: hopeful hops
        else if (!Air && hb <= mh + 4 && hb > mh - 22 && Math.Abs(hp.X - Rig.Mouth.X) < 30) { mouthT = .7; }
        if (!Air && prepT <= 0 && (actT > actDur || hb > 300 || hb < -4)) GiveUp();
    }
    public void StartCurious() { Set(Act.Curious, 99); Emote(2); calm = 0; perk = 1; }
    void CuriousAct(double h)
    {
        var p = layer.PointerAt;
        if (!layer.PointerInBand || layer.Curious != this) { if (actT > 1.2 && (!layer.PointerInBand || layer.Curious != this)) LoseInterest(Rnd(4, 8)); else { look = p; lookWT = .7; } return; }
        double side = Math.Sign(X - p.X); if (side == 0) side = -faceGoal;
        double tx = Math.Clamp(p.X + side * 46 * s / .9, MinX, MaxX), dist = Math.Abs(tx - X);
        look = p; lookWT = 1; perkT = 1; tailUpT = .9; wagAmpT = 20; wagFreqT = 4.2; wantFace = Math.Sign(p.X - X);
        bool settled = dist < 26 && layer.PointerSpeed < 160;
        if (!settled || Math.Abs(Vx) > 25) { hasGoal = true; goalX = tx; goalSpeed = dist > 140 ? 135 : 75; stopDist = 4; limit = 0; }
        else
        {
            limit += h; if (limit > .35) wt[(int)W.Sit] = 1;
            if (limit > 1.2 && ((int)(limit / 2.2) % 2 == 1)) tiltT = id % 2 == 0 ? -11 : 11; // the curious head tilt
            if (limit > 3.2 && limit - h <= 3.2) Emote(0);
        }
        if (actT > 18) LoseInterest(22);
    }
    void LoseInterest(double cd) { if (layer.Curious == this) layer.Curious = null; pointerCd = cd; Set(Act.Idle, Rnd(1.5, 3)); }
    public void Notice(Point at, bool treat)
    {
        if (State is Act.Eat) return;
        if (!Awake) { if (State == Act.Sleep) WakeUp(); pendingSeek = true; pendingAt = at; treatSeek = treat; return; }
        Seek(at, treat);
    }
    void Seek(Point at, bool treat)
    {
        if (layer.Curious == this) layer.Curious = null;
        if (Target != null && Target.Claimed == layer && !layer.Targeted(Target, this)) Target.Claimed = null;
        Target = null; treatSeek = treat; spot = at; goalX = Math.Clamp(at.X - face * 10 * s, MinX, MaxX); goalSpeed = treat ? 230 : 125; stopDist = 3;
        Set(Act.Seek, 12); Emote(treat ? 1 : 2); calm = 0;
    }
    public void Glance(Point at) { if (Awake && State is Act.Idle or Act.Sit or Act.Walk or Act.Sniff) { spot = at; Watch(Rnd(1.5, 2.5)); } }
    public void WakeUp() { if (State != Act.Sleep) return; Set(Act.Wake, 3); calm = 0; sleepAfter = layer.SleepAfter >= 0 ? layer.SleepAfter + 30 : Rnd(50, 90); }
    public void StartInvite(PinkDog other) { mate = other; Set(Act.Invite, 99); calm = 0; }
    void RunAway()
    {
        if (mate == null) return;
        double dir = X >= mate.X ? 1 : -1; if (dir > 0 && MaxX - X < 200 || dir < 0 && X - MinX < 200) dir = -dir;
        goalX = Math.Clamp(X + dir * Rnd(230, 380), MinX, MaxX); goalSpeed = Rnd(195, 220); stopDist = 3;
    }
    void Play()
    {
        if (mate == null || layer.PlayLeft <= 0 || mate.State is not (Act.PlayRun or Act.PlayChase))
        {
            if (!Air && prepT <= 0) { mate = null; Set(rng.NextDouble() < .5 ? Act.Sit : Act.Idle, Rnd(3, 5)); }
            return;
        }
        perkT = 1; tailUpT = 1; wagAmpT = 20; wagFreqT = 5.5; hasGoal = true; look = mate.Rig.HeadCenter; lookWT = .6;
        if (State == Act.PlayRun)
        {
            if (Math.Abs(goalX - X) < 8) RunAway();
        }
        else
        {
            goalX = Math.Clamp(mate.X - Math.Sign(mate.X - X) * 6, MinX, MaxX); goalSpeed = 228; stopDist = 0;
            double d = Math.Abs(mate.X - X);
            if (d < 70 && d > 40 && !Air && jumpCd <= 0 && (mate.X - X) * faceGoal > 0 && rng.NextDouble() < .03) { Jump(Rnd(16, 26), Vx * 1.05); jumpCd = 1.2; }
            if (d < 28 * s / .9 && tagCd <= 0) // tag: roles swap
            {
                tagCd = mate.tagCd = 1.2; Set(Act.PlayRun, 99); RunAway(); mate.Set(Act.PlayChase, 99); mate.Emote(3);
                if (!Air) Jump(14, Vx);
            }
        }
    }

    // ---------------------------------------------------------------- locomotion
    void StartTurn() { if (turnU < 1) return; turnFrom = face; faceGoal = -faceGoal; turnU = 0; turnDur = State is Act.Chase or Act.PlayChase or Act.PlayRun or Act.Heart ? .2 : .3; }
    void Jump(double height, double vx) { if (Air || prepT > 0) return; prepT = height > 20 ? .15 : .1; prepH = height; prepVx = vx; }
    void Locomotion(double h)
    {
        double desired = 0;
        if (hasGoal && prepT <= 0)
        {
            double dx = goalX - X, dist = Math.Abs(dx), dec = goalSpeed > 160 ? 420 : goalSpeed > 90 ? 300 : 160;
            // A small overshoot of a resting goal is accepted (no turning back for a few DIP).
            if (dx * faceGoal < 0 && dist < 30 && speed < 60 && turnU >= 1 && State is Act.Walk or Act.Seek or Act.Invite or Act.Curious) { goalX = X; dist = 0; }
            // Arrival easing: brake on a v² = 2·a·d profile, started early enough for the velocity response lag.
            if (dist > stopDist) desired = Math.Sign(dx) * Math.Min(goalSpeed, Math.Sqrt(2 * dec * .7 * Math.Max(0, dist - stopDist * .5 - speed * .12)));
        }
        int need = desired > .5 ? 1 : desired < -.5 ? -1 : wantFace;
        if (turnU < 1) desired = 0;
        else if (need != 0 && need != Math.Sign(faceGoal))
        {
            desired = 0; // brake first, then turn
            if (Math.Abs(Vx) < (State is Act.Chase or Act.PlayChase or Act.PlayRun or Act.Heart ? 70 : 24) && !Air && prepT <= 0) StartTurn();
        }
        // Turn: the head leads (mirrors over the first ~60 %), the body follows through a narrow (never empty) profile.
        if (turnU < 1)
        {
            turnU = Math.Min(1, turnU + h / turnDur);
            double eh = Smooth(turnU * 1.65), eb = Smooth((turnU - .18) / .82);
            headFace = turnFrom * Math.Cos(Math.PI * eh); face = turnFrom * Math.Cos(Math.PI * eb);
            if (Math.Abs(face) < .12) face = .12 * (eb < .5 ? turnFrom : -turnFrom);
        }
        else face = headFace = faceGoal;
        // Speed: eased approach to the desired speed with acceleration limits (gentle starts, firmer braking, skids in play).
        double sp = goalSpeed, acc = sp > 160 ? 430 : sp > 90 ? 260 : 120, dcc = sp > 160 ? 560 : sp > 90 ? 380 : 220;
        if (prepT > 0)
        {
            prepT -= h; wt[(int)W.Crouch] = 1; Vx *= 1 - Math.Min(1, h * 6);
            if (prepT <= 0) { Air = true; liftV = Math.Sqrt(2 * Gravity * prepH); Vx = prepVx; squashV -= 1.4; }
        }
        else if (!Air)
        {
            bool faster = Math.Abs(desired) > Math.Abs(Vx) && Math.Sign(desired) == Math.Sign(Vx == 0 ? desired : Vx);
            double lim = faster ? acc : dcc;
            Vx += Math.Clamp((desired - Vx) * Math.Min(1, h * (faster ? 4.5 : 8)), -lim * h, lim * h);
        }
        else Vx *= 1 - .12 * h;
        if (Air)
        {
            liftV -= Gravity * h; Lift += liftV * h;
            if (Lift <= 0 && liftV < 0) { squashV += Math.Min(3.2, -liftV * .0042); Lift = 0; Air = false; landT = .17; Vx *= Math.Abs(desired) > 50 ? .85 : .45; }
        }
        X += Vx * h;
        if (X < MinX || X > MaxX) { X = Math.Clamp(X, MinX, MaxX); Vx = 0; }
        accel += ((Vx - lastVx) / h * Math.Sign(faceGoal) - accel) * Math.Min(1, h * 10); lastVx = Vx;
        speed = Math.Abs(Vx);
        // Gait: walk → trot → bound blended continuously by speed; cadence rises with speed (small dogs patter).
        double g = Smooth((speed - 55) / 55) + Smooth((speed - 140) / 50);
        gait += (g - gait) * Math.Min(1, h * 5);
        freq = (1.05 + 1.9 * Math.Sqrt(speed / 60)) / Math.Sqrt(s / .88);
        phase = (phase + freq * h) % 1;
        landT -= h; if (landT > 0) wt[(int)W.Crouch] = Math.Max(wt[(int)W.Crouch], landT / .17 * .8);
        squashV += (-260 * squash - 15 * squashV) * h; squash = Math.Clamp(squash + squashV * h, -.35, .4);
    }

    // ---------------------------------------------------------------- secondary motion
    void Secondary(double h)
    {
        for (int i = 0; i < wv.Length; i++)
        {
            double om = Omega[i], target = i == (int)W.Air ? (Air ? 1 : 0) : wt[i];
            wvel[i] += (om * om * (target - wv[i]) - 2 * om * wvel[i]) * h; wv[i] += wvel[i] * h;
        }
        // Look (critically damped head) and eyes.
        lookW += (lookWT - lookW) * Math.Min(1, h * 5); tilt += (tiltT - tilt) * Math.Min(1, h * 6);
        double la = 0, ex = 0, ey = 0;
        if (look is Point lp)
        {
            double fx = Math.Sign(face == 0 ? faceGoal : face), hx = Rig.HeadCenter.X, hy = Rig.HeadCenter.Y;
            double dx = (lp.X - hx) * fx, dy = lp.Y - hy;
            la = Math.Clamp(Math.Atan2(dy, Math.Max(dx, 18)) * 180 / Math.PI, -42, 30) * .75;
            ex = Math.Clamp(dx / 70, -.6, 1) * 1.1; ey = Math.Clamp(dy / 70, -1, 1) * .9;
            if (dx < -30 && State is Act.Idle or Act.Sit or Act.Lie && turnU >= 1 && rng.NextDouble() < h * .8) { spot = default; if (State != Act.Lie) StartTurn(); }
        }
        headTarget = la * lookW + tilt;
        headAngV += (14 * 14 * (headTarget - headAng) - 2 * 14 * headAngV) * h; headAng += headAngV * h;
        lookX += (ex * lookW - lookX) * Math.Min(1, h * 9); lookY += (ey * lookW - lookY) * Math.Min(1, h * 9);
        // Ears: damped springs pushed by the body's acceleration (floppy ears swing and bounce, pointy ones flick).
        perk += (perkT - perk) * Math.Min(1, h * 5); droop += (droopT - droop) * Math.Min(1, h * 2);
        var ears = Rig.Look.Ears; bool pointy = ears == DogEars.Pointy;
        double nT = pointy ? -10 + 7 * perk - 12 * droop : (ears == DogEars.Long ? 10 : 20) - 9 * perk + 10 * droop, fT = pointy ? 8 - 4 * perk + 10 * droop : (ears == DogEars.Long ? -26 : -38) - 6 * perk + 8 * droop;
        double om2 = pointy ? 24 : 10, z = pointy ? .3 : .22, push = (pointy ? .025 : .09) * accel + (pointy ? 0 : .045) * bodyAccY;
        if ((twitchT -= h) <= 0) { twitchT = Rnd(3, 9); earNV += (pointy ? -260 : -180) * (rng.NextDouble() < .5 ? 1 : .6); if (rng.NextDouble() < .5) earFV += 160; }
        earNV += (om2 * om2 * (nT - earN) - 2 * z * om2 * earNV + push * om2) * h; earN = Math.Clamp(earN + earNV * h, -45, 85);
        earFV += (om2 * om2 * (fT - earF) - 2 * z * om2 * earFV + push * om2 * .8) * h; earF = Math.Clamp(earF + earFV * h, -60, 40);
        // Tail: mood sets height, wag amplitude and frequency (eased, phase-continuous); a spring adds inertia.
        wagAmp += (wagAmpT - wagAmp) * Math.Min(1, h * 3); wagFreq += (wagFreqT - wagFreq) * Math.Min(1, h * 3); tailUp += (tailUpT - tailUp) * Math.Min(1, h * 3);
        wagPh += 2 * Math.PI * wagFreq * h; if (wagPh > 1000) wagPh -= 2 * Math.PI * 100;
        tailInV += (-120 * tailIn - 9 * tailInV - bodyAccY * .25) * h; tailIn = Math.Clamp(tailIn + tailInV * h, -30, 30);
        // Eyes: blinks at random intervals (sometimes double), drowsy lids, sleep arcs.
        eyeBase += (eyeBaseT - eyeBase) * Math.Min(1, h * 3); sleepEyes += (sleepEyesT - sleepEyes) * Math.Min(1, h * 2.5);
        if ((blinkT -= h) <= 0 && Awake) { blinkAge = 0; blinkT = rng.NextDouble() < .18 ? .3 : Rnd(2.2, 5.5) * (eyeBase < .7 ? 1.6 : 1); }
        blinkAge += h;
        // Breathing: slow when asleep, panting after running.
        bool panting = pant > .35 && speed < 60 && Awake && State != Act.Eat;
        double rT = State == Act.Sleep ? .3 : State == Act.Lie ? .45 : panting ? 3.1 : .85, aT = State == Act.Sleep ? .036 : State == Act.Lie ? .024 : panting ? .026 : .016;
        breathRate += (rT - breathRate) * Math.Min(1, h * 2); breathAmp += (aT - breathAmp) * Math.Min(1, h * 2); breathPh += 2 * Math.PI * breathRate * h; if (breathPh > 1000) breathPh -= 2 * Math.PI * 100;
        if (panting) mouthT = Math.Max(mouthT, .5 + .12 * Math.Sin(breathPh));
        if (yawnT >= 0) { yawnT += h; mouthT = Math.Max(mouthT, Math.Sin(Math.PI * Math.Min(1, yawnT / 1.5))); if (yawnT > 1.5) yawnT = -1; }
        if (chompT >= 0) { chompT += h; if (chompT > .18) chompT = -1; }
        // Petal snap: eyes on the petal, mouth opens as it comes close, then a quick chomp.
        if (snapT >= 0)
        {
            snapT += h; var t = snapAt;
            if (t == null || t.Remove || snapT > 1.6) snapT = -1;
            else
            {
                look = t.Position; lookW = Math.Max(lookW, .9); double d = (t.Position - Rig.Mouth).Length;
                if (snapT < 10) { mouthT = Math.Max(mouthT, d < 45 ? .75 : .2); if (d < 11 + 4 * s) { snapT = 10; chompT = 0; } }
                else if (snapT > 10.25) snapT = -1;
            }
        }
        if (chompT >= 0) mouthT = 0;
        mouth += (mouthT - mouth) * Math.Min(1, h * (mouthT < mouth ? 22 : 12));
        // Sleep Z's and emotes.
        if (State == Act.Sleep && actT > 2.5 && (zClock -= h) <= 0) { zClock = 1.25; zAge[zNext] = 0; zNext = (zNext + 1) % 3; }
        for (int i = 0; i < 3; i++) if (zAge[i] >= 0 && (zAge[i] += h) > 2.6) zAge[i] = -1;
        if (emote >= 0 && (emoteAge += h) > 1.6) emote = -1;
    }
    double bodyAccY, lastBodyY, lastBodyV;

    // ---------------------------------------------------------------- pose
    public void Present()
    {
        var R = Rig; var p = pose;
        double sp = speed, amp = Math.Clamp(sp / 45, 0, 1), g = gait, ph = phase * 2 * Math.PI;
        // Locomotion pose: body bob (two beats per cycle at walk/trot, one big beat when bounding), pitch, accel lean.
        double bob = amp * L3(.7 * Math.Cos(2 * ph), 1.1 * Math.Cos(2 * ph + .6), 2.6 * Math.Sin(ph + 1.2), g);
        double pitch0 = amp * L3(0, .8 * Math.Sin(2 * ph), 7 * Math.Sin(ph + .3), g) + Math.Clamp(accel * .011, -6, 6);
        double ws = C01(wv[(int)W.Sit]), wl = C01(wv[(int)W.Lie]), wb = C01(wv[(int)W.Bow]), wr = C01(wv[(int)W.Rear]), wn = C01(wv[(int)W.Sniff]);
        double sum = ws + wl + wb + wr + wn; if (sum > 1) { ws /= sum; wl /= sum; wb /= sum; wr /= sum; wn /= sum; sum = 1; }
        double w0 = 1 - sum;
        // Each full pose: torso centre + pitch (from an anchor joint placed on/near the ground), head offset/angle, feet.
        Vector C = w0 * new Vector(0, R.RestY + bob); double pitch = w0 * pitch0; Vector ho = w0 * new Vector(0, -.55 * bob); double ha = 0;
        double a0 = -R.Ankle;
        Point nh = Mul(GaitFoot(0, 0, R.HipN.X + .5), w0), fh = Mul(GaitFoot(L3(.5, .5, .1, g), 1, R.HipF.X - 1), w0);
        Point nf = Mul(GaitFoot(L3(.25, .5, .55, g), 2, R.ShoulderN.X + .8), w0), ff = Mul(GaitFoot(L3(.75, 1, .65, g), 3, R.ShoulderF.X + 1.6), w0);
        if (ws > 0)
        {
            var c = Anchor(R.HipN, new Point(R.HipN.X + 1.5, -7.2), -30); C += ws * c; pitch += ws * -30; ho += ws * new Vector(-1.5, -2); ha += ws * -4;
            var sh = BodyPoint(R.ShoulderN, c, -30); nh = Add(nh, ws, R.HipN.X + 9, a0); fh = Add(fh, ws, R.HipF.X + 8, a0); nf = Add(nf, ws, sh.X + 1.2, a0); ff = Add(ff, ws, sh.X - .8, a0);
        }
        if (wl > 0)
        {
            var c = new Vector(.5, -11.6); C += wl * c; ho += wl * new Vector(3, 8.5); ha += wl * 4;
            nh = Add(nh, wl, R.HipN.X + 6.5, a0); fh = Add(fh, wl, R.HipF.X + 6, a0); nf = Add(nf, wl, R.ShoulderN.X + 13.5, a0); ff = Add(ff, wl, R.ShoulderF.X + 13, a0);
        }
        if (wb > 0)
        {
            var c = Anchor(R.ShoulderN, new Point(R.ShoulderN.X + 1, -7.4), 24); C += wb * c; pitch += wb * 24; ho += wb * new Vector(3, 6); ha += wb * -16;
            var hp = BodyPoint(R.HipN, c, 24); nh = Add(nh, wb, hp.X + .5, a0); fh = Add(fh, wb, hp.X - 1, a0); nf = Add(nf, wb, R.ShoulderN.X + 13, a0); ff = Add(ff, wb, R.ShoulderF.X + 13.5, a0);
        }
        if (wr > 0)
        {
            var c = Anchor(R.HipN, new Point(R.HipN.X + 3, -15.5), -55); C += wr * c; pitch += wr * -55; ho += wr * new Vector(-2, -1); ha += wr * -18;
            var sh = BodyPoint(R.ShoulderN, c, -55); double bat = actT * 2 * Math.PI * 2.6;
            nh = Add(nh, wr, R.HipN.X + 5, a0); fh = Add(fh, wr, R.HipF.X + 4, a0);
            nf = Add(nf, wr, sh.X + 7 + 3.5 * Math.Sin(bat), sh.Y + 9 + 3 * Math.Cos(bat)); ff = Add(ff, wr, sh.X + 6 + 3.5 * Math.Sin(bat + 2.4), sh.Y + 9 + 3 * Math.Cos(bat + 2.4));
        }
        if (wn > 0)
        {
            var c = new Vector(0, R.RestY + 1.5); C += wn * c; pitch += wn * 9; ho += wn * new Vector(4, 12 + .5 * Math.Sin(actT * 2 * Math.PI * 7)); ha += wn * 28;
            nh = Add(nh, wn, R.HipN.X + .5, a0); fh = Add(fh, wn, R.HipF.X - 1, a0); nf = Add(nf, wn, R.ShoulderN.X + 1.5, a0); ff = Add(ff, wn, R.ShoulderF.X + 2.3, a0);
        }
        // Modifiers: curl (asleep), scratch (sitting), crouch (anticipation, landing, stalking), airborne.
        double curl = C01(wv[(int)W.Curl]) * wl, scr = C01(wv[(int)W.Scratch]) * ws, cr = C01(wv[(int)W.Crouch]), air = C01(wv[(int)W.Air]);
        C += new Vector(-.5 * curl, .6 * curl + 5.5 * cr); pitch += 2 * curl + 3 * cr; ho += new Vector(-2.5 * curl - 1.5 * scr, 5 * curl + 1.5 * scr + 1.5 * cr); ha += 14 * curl + 14 * scr;
        if (scr > 0) { double k = actT * 2 * Math.PI * 8.5; nh = Lerp(nh, new Point(R.HipN.X + 9 + 2.2 * Math.Sin(k), -17 + 2.6 * Math.Cos(k)), scr); }
        if (air > 0)
        {
            double up = Math.Clamp(liftV / 300, -1, 1) * .5 + .5, ap = -Math.Clamp(liftV * .028, -13, 15);
            pitch += (ap - pitch) * air;
            var sN = BodyPoint(R.ShoulderN, C, pitch); var hN = BodyPoint(R.HipN, C, pitch); var sF = BodyPoint(R.ShoulderF, C, pitch); var hF = BodyPoint(R.HipF, C, pitch);
            nf = Lerp(nf, new Point(sN.X + L(4, 8, up), sN.Y + L(15, 9.5, up)), air); ff = Lerp(ff, new Point(sF.X + L(5, 6.5, up), sF.Y + L(14.5, 10.5, up)), air);
            nh = Lerp(nh, new Point(hN.X + L(-1.5, -8, up), hN.Y + L(14, 11.5, up)), air); fh = Lerp(fh, new Point(hF.X + L(-2.5, -6.5, up), hF.Y + L(13.5, 12.5, up)), air);
        }
        // Vertical body acceleration (for ear/tail inertia), measured on the torso centre including jumps.
        double by = -Lift / s + C.Y, bv = (by - lastBodyY) * 60; bodyAccY += ((bv - lastBodyV) * 60 - bodyAccY) * .35; lastBodyY = by; lastBodyV = bv;
        // Head: stabilised against the bob, nods with the gait, eased look/tilt on top.
        double nod = amp * L3(1.6 * Math.Sin(2 * ph + .5), 2 * Math.Sin(2 * ph + 1.1), 4 * Math.Sin(ph + 2), g) * (1 - sum);
        p.X = X; p.Ground = Ground; p.Lift = Lift + (turnU < 1 ? 2.5 * Math.Sin(Math.PI * turnU) : 0); p.Face = face; p.HeadFace = headFace;
        p.SquashY = 1 - squash * .55 + (turnU < 1 ? .04 * Math.Sin(Math.PI * turnU) : 0); p.SquashX = 1 + squash * .4;
        p.Body = C; p.Pitch = pitch; p.Breath = breathAmp * Math.Sin(breathPh);
        p.HeadOffset = ho + new Vector(snapT >= 10 || chompT >= 0 ? 1.8 : 0, 0); p.HeadAngle = pitch * .3 + ha + headAng + nod;
        p.NH = nh; p.FH = fh; p.NF = nf; p.FF = ff;
        // Tail angles (torso space): height by mood, streams back when running, lies down when sitting, curls when asleep.
        var tail = R.Look.Tail; double wag = wagAmp * Math.Sin(wagPh);
        double baseA = L(168, 232, tailUp); baseA += (196 - baseA) * Math.Min(1, sp / 190); if (tail == DogTail.Plume) baseA += 16;
        baseA += (204 - baseA) * ws; baseA += (176 - baseA) * wl; baseA += (148 - baseA) * curl;
        double r1 = tail == DogTail.Plume ? 26 : tail == DogTail.Long ? -8 : 20, r2 = tail == DogTail.Plume ? 30 : tail == DogTail.Long ? -10 : 26;
        r1 += (40 - r1) * curl; r2 += (44 - r2) * curl;
        p.Tail0 = baseA + wag + tailIn; p.Tail1 = r1 + wagAmp * .75 * Math.Sin(wagPh - 1) + tailIn * .5; p.Tail2 = r2 + wagAmp * .65 * Math.Sin(wagPh - 2) + tailIn * .3;
        p.EarNear = earN; p.EarFar = earF;
        double blink = blinkAge < .15 ? Math.Sin(Math.PI * blinkAge / .15) : 0;
        p.EyeOpen = Math.Max(0, eyeBase * (1 - blink) * (yawnT >= 0 ? 1 - .7 * Math.Sin(Math.PI * Math.Min(1, yawnT / 1.5)) : 1));
        p.Sleep = sleepEyes > .5 || eyeBase < .12 ? 1 : 0; p.Happy = happyT > 0 ? 1 : 0; p.LookX = lookX; p.LookY = lookY;
        p.Mouth = C01(mouth); p.Tongue = pant > .35 && mouth > .3 && Awake ? C01((mouth - .3) * 3) : 0;
        p.Shadow = Math.Clamp(1 - Lift / 140, .45, 1);
        p.Emote = emote; p.EmoteScale = emote < 0 ? 0 : emoteAge < .3 ? BackOut(emoteAge / .3) : 1; p.EmoteOpacity = emote < 0 ? 0 : Math.Min(1, emoteAge / .08) * Math.Clamp((1.6 - emoteAge) / .35, 0, 1);
        for (int i = 0; i < 3; i++) p.Z[i] = zAge[i];
        long al0 = GC.GetAllocatedBytesForCurrentThread(); R.Apply(p); PinkDogsLayer.ProbeApply += GC.GetAllocatedBytesForCurrentThread() - al0;
        Thing.Position = new Point(X + Math.Sign(face == 0 ? faceGoal : face) * 4 * s, Ground - Lift - 26 * s); Thing.Velocity = new Vector(Vx, -liftV * (Air ? 1 : 0)); Thing.Landed = State == Act.Sleep;
    }
    // Ankle target of one leg in the gait cycle (dog space): stance slides back at ground speed (no foot sliding),
    // swing lifts and eases forward. Stride shrinks to nothing as the dog stops, so feet settle under the body.
    Point GaitFoot(double offset, int leg, double restX)
    {
        double p = (phase + offset) % 1, duty = L3(.62, .5, .38, gait), S = Math.Min(24, speed / s * duty / Math.Max(.5, freq));
        double lift = Math.Min(1, speed / 25) * L3(3, 4.2, 6, gait) * (leg >= 2 ? 1 : .9);
        if (p < duty) return new Point(restX + S * (.5 - p / duty), -Rig.Ankle);
        double u = (p - duty) / (1 - duty), e = u * u * (3 - 2 * u);
        return new Point(restX + S * (e - .5), -Rig.Ankle - lift * Math.Sin(Math.PI * u));
    }
    // Torso centre that puts a torso-local joint at a dog-space point for a given pitch.
    static Vector Anchor(Point local, Point at, double pitch) { double a = pitch * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a); return new Vector(at.X - (local.X * c - local.Y * sn), at.Y - (local.X * sn + local.Y * c)); }
    static Point BodyPoint(Point local, Vector centre, double pitch) { double a = pitch * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a); return new Point(local.X * c - local.Y * sn + centre.X, local.X * sn + local.Y * c + centre.Y); }
    static Point Mul(Point p, double k) => new(p.X * k, p.Y * k);
    static Point Add(Point p, double k, double x, double y) => new(p.X + k * x, p.Y + k * y);
    static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    static double L(double a, double b, double t) => a + (b - a) * t;
    static double L3(double a, double b, double c, double g) => g < 1 ? a + (b - a) * g : b + (c - b) * Math.Min(1, g - 1);
    static double C01(double v) => Math.Clamp(v, 0, 1);
    static double Smooth(double v) { v = C01(v); return v * v * (3 - 2 * v); }
    static double BackOut(double t) { double c1 = 1.70158, u = t - 1; return 1 + (c1 + 1) * u * u * u + c1 * u * u; }
}
