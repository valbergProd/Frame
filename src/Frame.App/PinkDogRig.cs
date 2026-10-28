using System.Windows;
using System.Windows.Media;

namespace Frame.App;

// Pink mode dogs (0.6.0): the articulated vector dog. Every part is a frozen geometry drawn once into its own DrawingVisual;
// motion only updates one MatrixTransform per part (forward kinematics computed here, two-bone IK for the legs), so a step
// costs a few matrix products and ~25 transform writes per dog: no geometry rebuilds, no OnRender, no per-frame allocation.
// Style: rounded chibi proportions (big 3/4 head, bean body, stubby legs), soft gradients, one darker outline around each
// limb/body group (outline pass of the whole group first, then its fills, so joints merge seamlessly), big dark eyes with
// highlights, blush cheeks. Dog space: facing +x, y down, origin on the ground under the body, unscaled (Look.Scale and the
// facing flip are applied by the root matrix).
enum DogEars { Floppy, Long, Pointy }
enum DogTail { Short, Long, Plume }
enum DogAccessory { HeadBow, Collar, NeckBow }

sealed class DogLook
{
    public string Name = "";
    public Color Fur, Shade, Outline, Light, Far, Ear, EarInner, Accent, AccentDark;
    public Color Nose = Hex("#55203D"), Eye = Hex("#3A1631"), Blush = Hex("#FF6C9C");
    public double Scale = .9, Head = 1, Body = 1, Legs = 1;
    public DogEars Ears; public DogTail Tail; public DogAccessory Accessory; public bool Fluffy;
    public static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
    // Three distinct but cohesive dogs: a blush-pink puppy with a head bow, a rose/magenta long-eared one with a collar and a
    // pink-lavender fluffy spitz with a neck bow. No green anywhere.
    public static readonly DogLook[] Pack =
    [
        new() { Name = "Mochi", Fur = Hex("#FFD7E5"), Shade = Hex("#F2A9C5"), Outline = Hex("#C9688F"), Light = Hex("#FFF4F8"), Far = Hex("#E99BB9"),
                Ear = Hex("#F4A2C0"), EarInner = Hex("#FFC9DB"), Accent = Hex("#FF4F93"), AccentDark = Hex("#B9286A"),
                Scale = .82, Head = 1.08, Body = .94, Legs = .9, Ears = DogEars.Floppy, Tail = DogTail.Short, Accessory = DogAccessory.HeadBow },
        new() { Name = "Rosie", Fur = Hex("#F68CB8"), Shade = Hex("#D9598F"), Outline = Hex("#9C3164"), Light = Hex("#FFD5E6"), Far = Hex("#C64F82"),
                Ear = Hex("#C23F78"), EarInner = Hex("#E57EA8"), Accent = Hex("#FFF4F9"), AccentDark = Hex("#B03A70"),
                Scale = .9, Head = 1, Body = 1.1, Legs = 1.08, Ears = DogEars.Long, Tail = DogTail.Long, Accessory = DogAccessory.Collar },
        new() { Name = "Lumi", Fur = Hex("#F0D9F8"), Shade = Hex("#D2ACE6"), Outline = Hex("#9268AE"), Light = Hex("#FDF5FF"), Far = Hex("#C9A0DE"),
                Ear = Hex("#E6C4F3"), EarInner = Hex("#F8BFD8"), Accent = Hex("#FF7DB2"), AccentDark = Hex("#C04682"),
                Scale = .86, Head = 1.03, Body = 1, Legs = .95, Ears = DogEars.Pointy, Tail = DogTail.Plume, Accessory = DogAccessory.NeckBow, Fluffy = true },
    ];
}

// Everything the rig needs for one frame (written by PinkDog each step; dog space unless noted).
sealed class DogPose
{
    public double X, Ground, Lift, Face = 1, HeadFace = 1, SquashX = 1, SquashY = 1; // root: world x, feet line, lift (world DIP); facings -1..1
    public Vector Body; public double Pitch, Breath;                   // torso centre, pitch (deg, + = nose down), breath scale
    public Vector HeadOffset; public double HeadAngle;                  // head offset from its rest place on the neck; absolute angle
    public double EarNear, EarFar, Tail0, Tail1, Tail2;                 // deg (ears relative to head; tail 0 in torso space, 1/2 relative)
    public Point NH, FH, NF, FF;                                        // ankle targets: near/far hind, near/far front
    public double EyeOpen = 1, LookX, LookY, Happy, Sleep, Mouth, Tongue, Shadow = 1;
    public int Emote = -1; public double EmoteScale, EmoteOpacity;     // 0 heart, 1 !, 2 ?, 3 note (world, never mirrored)
    public readonly double[] Z = [-1, -1, -1];                          // ages of the sleeping Z's (s), < 0 = hidden
}

sealed class PinkDogRig
{
    // One bone/part transform. Writes are skipped when nothing moved; changed values go through two pre-boxed Matrix
    // objects used alternately (WPF keeps the one last set and compares old/new by value; only the other one is mutated),
    // so a transform write allocates nothing (a plain MatrixTransform.Matrix write boxes 72 bytes).
    sealed class Part
    {
        public readonly MatrixTransform T = new();
        readonly object boxA = Matrix.Identity, boxB = Matrix.Identity;
        Matrix last; bool set, flip;
        public void Set(in Matrix m)
        {
            if (set && Math.Abs(m.M11 - last.M11) + Math.Abs(m.M12 - last.M12) + Math.Abs(m.M21 - last.M21) + Math.Abs(m.M22 - last.M22) < 1e-4
                && Math.Abs(m.OffsetX - last.OffsetX) + Math.Abs(m.OffsetY - last.OffsetY) < 2e-3) return;
            last = m; set = true; flip = !flip;
            var box = flip ? boxA : boxB; System.Runtime.CompilerServices.Unsafe.Unbox<Matrix>(box) = m;
            long q = GC.GetAllocatedBytesForCurrentThread(); T.SetValue(MatrixTransform.MatrixProperty, box); Sets++; SetBytes += GC.GetAllocatedBytesForCurrentThread() - q;
        }
    }
    public readonly ContainerVisual Root = new();
    internal static long Sets, SetBytes;
    public readonly DogLook Look;
    // Skeleton (unscaled). Torso-local joints; HeadRest = neck → head centre; Ankle = ankle height above the ground.
    public readonly double RestY, Ankle = 2.9, L1h, L2h, L1f, L2f, HeadScale;
    public readonly Point HipN, HipF, ShoulderN, ShoulderF, Neck, TailBase;
    public readonly Vector HeadRest;
    readonly double[] tailLen;
    static readonly Point MouthAt = new(15.6, 5.2), TongueAt = new(15.2, 6.8), NoseAt = new(16, 2.4);
    readonly Part shadow = new(), fhU = new(), fhL = new(), ffU = new(), ffL = new(), tail0 = new(), tail1 = new(), tail2 = new(), earF = new(), torso = new(),
        nhU = new(), nhL = new(), nfU = new(), nfL = new(), head = new(), eyes = new(), arcs = new(), mouth = new(), tongue = new(), earN = new(), acc = new(), emote = new();
    readonly Part[] zs = [new(), new(), new()];
    readonly DrawingVisual mouthV, tongueV, eyesV, sleepV, happyV;
    readonly DrawingVisual[] zV = new DrawingVisual[3], emoteV = new DrawingVisual[4];
    // Outputs of the last Apply (world DIP): mouth (catching), head centre/top (emotes, look), head centre in dog space.
    public Point Mouth, HeadCenter, HeadTop, HeadDog, NoseWorld;

    public PinkDogRig(DogLook look)
    {
        Look = look; HeadScale = look.Head; double bl = look.Body, lk = look.Legs;
        L1h = 9 * lk; L2h = 8.5 * lk; L1f = 8.6 * lk; L2f = 8.6 * lk;
        HipN = new(-10 * bl, 4); HipF = new(-12 * bl, 2.6); ShoulderN = new(9.5 * bl, 4); ShoulderF = new(7 * bl, 2.6);
        Neck = new(12 * bl, -6); TailBase = new(-16.5 * bl, -5.5); HeadRest = new Vector(8, -11.5) * look.Head;
        RestY = -(Ankle + .93 * (L1h + L2h) + 4);
        tailLen = look.Tail switch { DogTail.Long => [6, 5.6, 5], DogTail.Plume => [4.4, 4.8, 4.6], _ => [4, 3.8, 3.4] };

        // Brushes and pens (frozen). Gradients follow each part's own box: light from the upper left, soft shade below.
        var outline = Solid(look.Outline); double w = 1.35;
        Pen Edge(double scale = 1) => Frozen(new Pen(outline, 2 * w / scale) { LineJoin = PenLineJoin.Round });
        var fur = Round(look.Fur, look.Shade); var far = Round(Mix(look.Far, look.Fur, .25), look.Far); var light = Solid(look.Light);
        var ear = Round(Mix(look.Ear, look.Fur, .3), look.Ear); var earInner = Solid(look.EarInner);
        var blush = new RadialGradientBrush(Color.FromArgb(150, look.Blush.R, look.Blush.G, look.Blush.B), Color.FromArgb(0, look.Blush.R, look.Blush.G, look.Blush.B)); blush.Freeze();
        var gloss = new RadialGradientBrush(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)); gloss.Freeze();
        var noseB = Round(Mix(look.Nose, Colors.White, .25), look.Nose); var eyeB = Round(Mix(look.Eye, look.Outline, .35), look.Eye);
        var belly = new RadialGradientBrush(look.Light, Color.FromArgb(0, look.Light.R, look.Light.G, look.Light.B)) { GradientOrigin = new Point(.5, .62), Center = new Point(.5, .6) }; belly.GradientStops.Insert(1, new GradientStop(look.Light, .45)); belly.Freeze();
        var white = Solid(Colors.White); var line = Frozen(new Pen(Solid(look.Nose), .95) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        var toe = Frozen(new Pen(Solid(look.Outline, .55), .7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        // Shapes. Legs: capsules along +y from the joint; paws are part of the lower segment (ankle = segment end).
        var hindU = U(E(-.4, .8, 5.6, 5.4), Capsule(L1h, 4.3, 3.3)); var hindL = U(Capsule(L2h, 3.2, 2.9), E(1.3, L2h + .2, 3.9, 2.7));
        var frontU = Capsule(L1f, 3.9, 3.2); var frontL = U(Capsule(L2f, 3.1, 2.9), E(1.5, L2f + .2, 3.9, 2.7));
        var torsoG = Scaled(G("M -2,-11 C 8,-12.2 15.5,-9 16.5,-2 C 17.5,5.5 12,11 4,11 C -2,11.8 -10,11.8 -14,10 C -19.5,7.5 -20,-2.5 -17,-7 C -14,-11 -8,-10.6 -2,-11 Z"), bl);
        Geometry? ruff = look.Fluffy ? Scaled(G("M 7,-8.5 C 11,-10.5 16,-8 17,-4.5 C 19.6,-3.4 19.8,.4 17.8,1.6 C 19.4,3.8 17.6,7.2 14.8,6.8 C 14.4,9.6 10.8,10.8 8.6,9 C 6.4,10.6 3.2,9.6 3.4,7 Z"), bl) : null;
        Geometry? rumpFluff = look.Fluffy ? Scaled(G("M -12,-10.4 C -15.4,-12.6 -19.6,-10 -18.6,-6.6 C -21.4,-5.6 -21.6,-1.4 -19.4,0 C -21.2,2.4 -19.2,6 -16.2,5.4 Z"), bl) : null;
        var torsoShape = ruff == null ? torsoG : U(torsoG, ruff, rumpFluff!);
        var cranium = E(0, 0, 15, 13.5); var muzzle = E(11, 5, 7.6, 5.6);
        var headShape = look.Fluffy ? U(cranium, muzzle, E(-12.2, 6.6, 4.2, 4), E(-8.6, 10.2, 4.2, 3.8), E(-13.6, 1.8, 3.4, 3.6)) : U(cranium, muzzle);
        var (earShape, earIn, earNPivot, earFPivot) = look.Ears switch
        {
            DogEars.Long => (G("M -4.2,-.5 C -1.5,-3.6 4.2,-3.2 5.2,.6 C 6.6,6 6,15 1.4,17.6 C -3.6,19.6 -7.2,13.5 -6.8,6.5 C -6.6,3.2 -5.6,.8 -4.2,-.5 Z"),
                G("M -2.6,1.8 C -.6,.4 2.8,.8 3.4,3 C 4.2,7 3.6,12.6 .8,14.6 C -2.2,16 -4.6,11.6 -4.4,7 C -4.2,4.6 -3.6,2.8 -2.6,1.8 Z"), new Point(-7.5, -9.5), new Point(6, -12.2)),
            DogEars.Pointy => (G("M -4.8,1.5 C -4.6,-4 -2.4,-10.6 .4,-13.2 C 2.8,-10.6 5.4,-4.5 5.6,1.5 Z"),
                G("M -2.4,.6 C -2.2,-3.6 -.9,-7.8 .5,-9.8 C 2,-7.8 3.3,-3.6 3.4,.6 Z"), new Point(-6.4, -9.6), new Point(5.8, -11.4)),
            _ => (G("M -4,-1 C -2,-4.6 4,-4.6 5,-1 C 6.2,3.5 5.6,10.6 3,14.2 C 1,16.8 -3.6,16.6 -5,13.2 C -6.4,9.6 -6,3 -4,-1 Z"),
                G("M -2.4,1.4 C -.8,-.4 2.6,-.2 3,1.6 C 3.8,4.8 3.4,9.8 1.6,12.2 C .2,13.8 -2.6,13.6 -3.4,11.4 C -4.2,8.8 -3.8,3.6 -2.4,1.4 Z"), new Point(-7.8, -10.6), new Point(6.6, -13.4)),
        };
        earNAt = earNPivot; earFAt = earFPivot;
        double[] tr = look.Tail switch { DogTail.Long => [2.6, 2.3, 1.9, 1.4], DogTail.Plume => [3, 3.9, 4.4, 3.6], _ => [3.1, 2.9, 2.5, 1.9] };
        Geometry Seg(int i) => look.Tail == DogTail.Plume
            ? U(E(tailLen[i] / 2, 0, tailLen[i] / 2 + tr[i + 1] * .55, tr[i + 1]), E(0, 0, tr[i], tr[i]))
            : G(Inv($"M 0,{-tr[i]} L {tailLen[i]},{-tr[i + 1]} A {tr[i + 1]},{tr[i + 1]} 0 0 1 {tailLen[i]},{tr[i + 1]} L 0,{tr[i]} A {tr[i]},{tr[i]} 0 0 1 0,{-tr[i]} Z"));

        // Draw order (back to front). Each group: outline pass for all its parts, then their fills, so the group reads as one
        // silhouette (seamless knees, tail and head+muzzle) while the near limbs still get a contour over the body.
        Add(shadow, dc => dc.DrawEllipse(Shadow(look.Outline), null, new Point(0, 0), 25, 4.2));
        Group(Edge(), (fhU, hindU, far, null), (fhL, hindL, far, null), (ffU, frontU, far, null), (ffL, frontL, far, null));
        Group(Edge(), (tail0, Seg(0), fur, null), (tail1, Seg(1), fur, null), (tail2, Seg(2), fur, look.Tail == DogTail.Plume ? dc => dc.DrawEllipse(light, null, new Point(tailLen[2] * .55, -.6), tr[3] * .7, tr[3] * .55) : null));
        Group(Edge(look.Head), (earF, earShape, far, null));
        Group(Edge(), (torso, torsoShape, fur, dc =>
        {
            dc.PushClip(torsoG); dc.DrawEllipse(belly, null, new Point(1.5 * bl, 9.5), 13 * bl, 5.5); dc.DrawEllipse(gloss, null, new Point(-3 * bl, -6.5), 11 * bl, 4); dc.Pop();
            if (ruff != null) dc.DrawGeometry(Round(look.Light, Mix(look.Light, look.Fur, .6)), null, ruff);
        }));
        Group(Edge(), (nhU, hindU, fur, null), (nhL, hindL, fur, dc => Toes(dc, toe, L2h)));
        Group(Edge(), (nfU, frontU, fur, null), (nfL, frontL, fur, dc => Toes(dc, toe, L2f)));
        Group(Edge(look.Head), (head, headShape, fur, dc =>
        {
            dc.DrawEllipse(gloss, null, new Point(-3, -7.5), 9, 4.6);
            dc.DrawEllipse(light, null, new Point(11, 5), 7.6, 5.6); dc.DrawEllipse(Solid(Colors.White, .45), null, new Point(10.4, 3.2), 4.6, 2.2);
            if (look.Fluffy) dc.DrawGeometry(Round(look.Light, look.Fur), null, U(E(-12.2, 6.6, 4.2, 4), E(-8.6, 10.2, 4.2, 3.8)));
            dc.DrawEllipse(blush, null, new Point(-4.6, 6.4), 4.2, 2.6); dc.DrawEllipse(blush, null, new Point(5.6, 8), 2.8, 1.8);
            dc.DrawGeometry(noseB, null, G("M 13.2,1.2 C 13.9,.1 18.1,.1 18.8,1.2 C 19.2,2.5 17.2,4.8 16,4.8 C 14.8,4.8 12.8,2.5 13.2,1.2 Z"));
            dc.DrawEllipse(Solid(Colors.White, .75), null, new Point(15, 1.2), 1.1, .6);
            dc.DrawGeometry(null, line, G("M 16,4.8 L 16,6.1 M 13,6.2 Q 14.5,7.9 16,6.1 Q 17.5,7.9 19,6.2"));
        }));
        eyesV = Add(eyes, dc =>
        {
            dc.DrawEllipse(eyeB, null, new Point(-3.4, -1.6), 3.15, 3.95); dc.DrawEllipse(eyeB, null, new Point(8.3, -2.2), 2.55, 3.6);
            dc.DrawEllipse(white, null, new Point(-4.5, -3), 1.35, 1.35); dc.DrawEllipse(white, null, new Point(-2.3, .3), .6, .6);
            dc.DrawEllipse(white, null, new Point(7.4, -3.5), 1.1, 1.1); dc.DrawEllipse(white, null, new Point(9.1, -.6), .5, .5);
        });
        var arcPen = Frozen(new Pen(Solid(look.Eye), 1.25) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        sleepV = Add(arcs, dc => dc.DrawGeometry(null, arcPen, G("M -6.4,-1.4 Q -3.4,1.4 -.4,-1.4 M 6,-2 Q 8.3,.3 10.6,-2")), 0);
        happyV = Add(arcs, dc => dc.DrawGeometry(null, arcPen, G("M -6.4,-.2 Q -3.4,-3.6 -.4,-.2 M 6,-.8 Q 8.3,-3.8 10.6,-.8")), 0);
        mouthV = Add(mouth, dc =>
        {
            dc.DrawGeometry(Solid(Hex("#9A2F5E")), Frozen(new Pen(Solid(look.Nose), .8) { LineJoin = PenLineJoin.Round }), G("M -3.2,0 L 3.2,0 C 3.2,3.4 1.7,5 0,5 C -1.7,5 -3.2,3.4 -3.2,0 Z"));
            dc.DrawEllipse(Solid(Hex("#FF86AC")), null, new Point(0, 3.5), 2.1, 1.3);
        }, 0);
        tongueV = Add(tongue, dc =>
        {
            dc.DrawGeometry(Solid(Hex("#FF7FA8")), Frozen(new Pen(Solid(Hex("#C94B7A")), .8) { LineJoin = PenLineJoin.Round }), G("M -2.3,-.6 L 2.3,-.6 C 2.7,2.6 2.4,5.8 0,6 C -2.4,5.8 -2.7,2.6 -2.3,-.6 Z"));
            dc.DrawGeometry(null, Frozen(new Pen(Solid(Hex("#D9638F")), .6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), G("M 0,.6 L 0,3.2"));
        }, 0);
        // Accessory: collar / neck bow under the chin (before the near ear); head bow on top of everything.
        var accFill = Round(Mix(look.Accent, Colors.White, .35), look.Accent); var accEdge = Frozen(new Pen(Solid(look.AccentDark), 2 * .9) { LineJoin = PenLineJoin.Round });
        var bow = G("M 0,0 C -2.2,-3.8 -7.4,-3.8 -7.1,.4 C -6.8,3.9 -2.6,3 0,0 Z M 0,0 C 2.2,-3.8 7.4,-3.8 7.1,.4 C 6.8,3.9 2.6,3 0,0 Z M -.6,.6 L -2.8,5 L -.7,4.3 Z M .6,.6 L 2.8,5 L .7,4.3 Z");
        void Accessory()
        {
            if (look.Accessory == DogAccessory.HeadBow) Group(accEdge, (acc, bow, accFill, dc => dc.DrawEllipse(Solid(look.AccentDark), null, new Point(0, .2), 1.8, 1.6)));
            else
            {
                var collar = G("M -9.6,8.6 Q -1.5,15.8 7.6,11.4").GetWidenedPathGeometry(new Pen(Brushes.Black, look.Accessory == DogAccessory.Collar ? 3 : 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }); collar.Freeze();
                Geometry extra = look.Accessory == DogAccessory.Collar ? G("M -1.2,14.4 C -1.9,13 -4.4,13.3 -4.4,15.2 C -4.4,16.8 -2.3,17.9 -1.2,19 C -.1,17.9 2,16.8 2,15.2 C 2,13.3 -.5,13 -1.2,14.4 Z")
                    : Shifted(bow, -1.6, 13.4, .72);
                Group(accEdge, (acc, U(collar, extra), accFill, dc =>
                {
                    if (look.Accessory == DogAccessory.Collar) dc.DrawGeometry(Round(Hex("#FF8DBA"), Hex("#E2457F")), null, extra);
                    else dc.DrawEllipse(Solid(look.AccentDark), null, new Point(-1.6, 13.5), 1.3, 1.15);
                }));
            }
        }
        if (look.Accessory != DogAccessory.HeadBow) Accessory();
        Group(Edge(look.Head), (earN, earShape, ear, dc => dc.DrawGeometry(earInner, null, earIn)));
        if (look.Accessory == DogAccessory.HeadBow) Accessory();
        // Sleep Z's and emotes: world space, never mirrored (a mirrored "z" or "?" would read wrong).
        var zGeo = G("M -2.6,-3 L 2.6,-3 L -2.6,3 L 2.6,3").GetWidenedPathGeometry(new Pen(Brushes.Black, 1.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }); zGeo.Freeze();
        var halo = Frozen(new Pen(Solid(Colors.White, .9), 2.4) { LineJoin = PenLineJoin.Round });
        for (int i = 0; i < 3; i++) zV[i] = Add(zs[i], dc => { dc.DrawGeometry(white, halo, zGeo); dc.DrawGeometry(Solid(look.Outline), null, zGeo); }, 0);
        var glyphs = new Geometry[]
        {
            G("M 0,-1.8 C -1.4,-5 -6.4,-4.6 -6.4,-.8 C -6.4,2.6 -2.4,4.8 0,7 C 2.4,4.8 6.4,2.6 6.4,-.8 C 6.4,-4.6 1.4,-5 0,-1.8 Z"),
            U(G("M -1.7,-7.4 L 1.7,-7.4 L .9,2 L -.9,2 Z"), E(0, 5, 1.6, 1.6)),
            U(Widen(G("M -3.2,-3.4 C -3.2,-7.8 3.6,-7.9 3.6,-3.6 C 3.6,-.9 0,-.5 0,2.2"), 2.3), E(0, 5.4, 1.5, 1.5)),
            U(Widen(G("M 2.6,-6.4 L 2.6,3 M 2.6,-6.4 C 4.8,-5.4 6.2,-3.8 5.8,-1.4"), 1.6), E(.4, 3.4, 2.5, 1.9)),
        };
        var glyphFill = new Brush[] { Round(Hex("#FF7FB0"), Hex("#F0397F")), Solid(look.AccentDark), Solid(look.AccentDark), Solid(look.AccentDark) };
        for (int i = 0; i < 4; i++) { int k = i; emoteV[i] = Add(emote, dc => dc.DrawGeometry(glyphFill[k], Frozen(new Pen(Solid(Colors.White), 2.2) { LineJoin = PenLineJoin.Round }), glyphs[k]), 0); }
    }
    Point earNAt, earFAt;

    void Group(Pen edge, params (Part part, Geometry shape, Brush fill, Action<DrawingContext>? details)[] items)
    {
        foreach (var (part, shape, _, _) in items) Add(part, dc => dc.DrawGeometry(edge.Brush, edge, shape));
        foreach (var (part, shape, fill, details) in items) Add(part, dc => { dc.DrawGeometry(fill, null, shape); details?.Invoke(dc); });
    }
    DrawingVisual Add(Part part, Action<DrawingContext> draw, double opacity = 1)
    {
        var v = new DrawingVisual { Transform = part.T, Opacity = opacity };
        using (var dc = v.RenderOpen()) draw(dc);
        Root.Children.Add(v); return v;
    }
    static void Toes(DrawingContext dc, Pen pen, double l) { dc.DrawLine(pen, new Point(3.2, l - .2), new Point(3.4, l + 1.6)); dc.DrawLine(pen, new Point(1.2, l + .2), new Point(1.3, l + 2)); }

    // Forward kinematics for one frame. Writes only transforms that changed (a sleeping dog touches a handful).
    public void Apply(DogPose p)
    {
        double s = Look.Scale, face = p.Face;
        var root = Matrix.Identity; root.Scale(s * face * p.SquashX, s * p.SquashY); root.Translate(p.X, p.Ground - p.Lift);
        var body = Matrix.Identity; body.Rotate(p.Pitch); body.Translate(p.Body.X, p.Body.Y);       // dog space
        var bodyW = body; bodyW.Append(root);
        var m = Matrix.Identity; m.Scale(1 + p.Breath * .35, 1 + p.Breath); m.Append(bodyW); torso.Set(m);
        Leg(nhU, nhL, body.Transform(HipN), p.NH, L1h, L2h, 1, root); Leg(fhU, fhL, body.Transform(HipF), p.FH, L1h, L2h, 1, root);
        Leg(nfU, nfL, body.Transform(ShoulderN), p.NF, L1f, L2f, -1, root); Leg(ffU, ffL, body.Transform(ShoulderF), p.FF, L1f, L2f, -1, root);
        var t = Matrix.Identity; t.Rotate(p.Tail0); t.Translate(TailBase.X, TailBase.Y); t.Append(bodyW); tail0.Set(t);
        var t1 = Matrix.Identity; t1.Rotate(p.Tail1); t1.Translate(tailLen[0], 0); t1.Append(t); tail1.Set(t1);
        var t2 = Matrix.Identity; t2.Rotate(p.Tail2); t2.Translate(tailLen[1], 0); t2.Append(t1); tail2.Set(t2);
        // Head: hangs from the neck, but its angle is absolute (it stays level while the body pitches: head stabilisation).
        double a = p.HeadAngle * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
        var neck = body.Transform(Neck);
        HeadDog = new Point(neck.X + HeadRest.X * ca - HeadRest.Y * sa + p.HeadOffset.X, neck.Y + HeadRest.X * sa + HeadRest.Y * ca + p.HeadOffset.Y);
        // The head mirrors on its own facing (it leads a turn: the dog looks round first, the body follows).
        var hc = root.Transform(HeadDog);
        var h = Matrix.Identity; h.Scale(HeadScale, HeadScale); h.Rotate(p.HeadAngle); h.Scale(s * p.HeadFace * p.SquashX, s * p.SquashY); h.Translate(hc.X, hc.Y); head.Set(h);
        Mouth = h.Transform(MouthAt); HeadCenter = h.Transform(new Point(0, 0)); NoseWorld = h.Transform(NoseAt);
        HeadTop = new Point(HeadCenter.X, HeadCenter.Y - 15 * HeadScale * s);
        var e = Matrix.Identity; e.Translate(0, 1.9); e.Scale(1, Math.Max(.06, p.EyeOpen)); e.Translate(p.LookX, -1.9 + p.LookY); e.Append(h); eyes.Set(e);
        var ar = Matrix.Identity; ar.Translate(p.LookX * .5, 0); ar.Append(h); arcs.Set(ar);
        Show(eyesV, p.Sleep > .5 || p.Happy > .5 ? 0 : 1); Show(sleepV, p.Sleep > .5 ? 1 : 0); Show(happyV, p.Happy > .5 && p.Sleep <= .5 ? 1 : 0);
        var mo = Matrix.Identity; mo.Scale(.75 + .25 * p.Mouth, Math.Max(.05, p.Mouth)); mo.Translate(MouthAt.X, MouthAt.Y); mo.Append(h); mouth.Set(mo); Show(mouthV, p.Mouth > .04 ? 1 : 0);
        var to = Matrix.Identity; to.Scale(1, Math.Max(.05, p.Tongue)); to.Translate(TongueAt.X, TongueAt.Y + 1.2 * p.Mouth); to.Append(h); tongue.Set(to); Show(tongueV, p.Tongue > .04 ? 1 : 0);
        var en = Matrix.Identity; en.Rotate(p.EarNear); en.Translate(earNAt.X, earNAt.Y); en.Append(h); earN.Set(en);
        var ef = Matrix.Identity; ef.Rotate(p.EarFar); ef.Translate(earFAt.X, earFAt.Y); ef.Append(h); earF.Set(ef);
        var ac = Matrix.Identity;
        if (Look.Accessory == DogAccessory.HeadBow) { ac.Scale(.72, .72); ac.Rotate(-16); ac.Translate(-1.5, -12.6); }
        ac.Append(h); acc.Set(ac);
        var sh = Matrix.Identity; double k = p.Shadow; sh.Scale(s * k * (Look.Body * .9 + .1), s * k); sh.Translate(p.X + face * 2 * s, p.Ground + .5); shadow.Set(sh);
        // Z's drift up and to the front of the sleeping head, growing and fading.
        for (int i = 0; i < 3; i++)
        {
            double age = p.Z[i];
            if (age < 0) { Show(zV[i], 0); continue; }
            var z = Matrix.Identity; double zs2 = .55 + .35 * Math.Min(1, age / 1.6);
            z.Scale(zs2, zs2); z.Translate(HeadTop.X + Math.Sign(p.HeadFace) * (8 + 7 * age) + 2.2 * Math.Sin(age * 3.1), HeadTop.Y - 2 - 13 * age);
            this.zs[i].Set(z); Show(zV[i], Math.Min(1, age * 3) * Math.Clamp((2.6 - age) / .9, 0, 1) * .9);
        }
        for (int i = 0; i < 4; i++) Show(emoteV[i], i == p.Emote ? p.EmoteOpacity : 0);
        if (p.Emote >= 0)
        {
            var em = Matrix.Identity; em.Scale(p.EmoteScale, p.EmoteScale); em.Translate(HeadTop.X + Math.Sign(p.HeadFace) * 4, HeadTop.Y - 11 - 4 * p.EmoteScale); emote.Set(em);
        }
    }

    // Two-bone IK in dog space. Segments are drawn along +y from their joint; bend +1 puts the middle joint behind the
    // hip→ankle line (hind hock), -1 in front (front wrist). Out-of-reach targets are clamped (the leg straightens).
    void Leg(Part upper, Part lower, Point hip, Point foot, double a, double b, double bend, in Matrix root)
    {
        double dx = foot.X - hip.X, dy = foot.Y - hip.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-6) { dx = 0; dy = 1; d = 1; }
        double dc = Math.Clamp(d, Math.Abs(a - b) + .8, (a + b) * .998), baseA = Math.Atan2(dy, dx);
        double t1 = baseA + bend * Math.Acos(Math.Clamp((a * a + dc * dc - b * b) / (2 * a * dc), -1, 1));
        double kx = hip.X + a * Math.Cos(t1), ky = hip.Y + a * Math.Sin(t1);
        double ex = hip.X + dx / d * dc, ey = hip.Y + dy / d * dc, t2 = Math.Atan2(ey - ky, ex - kx);
        var u = Matrix.Identity; u.Rotate(t1 * 180 / Math.PI - 90); u.Translate(hip.X, hip.Y); u.Append(root); upper.Set(u);
        var l = Matrix.Identity; l.Rotate(t2 * 180 / Math.PI - 90); l.Translate(kx, ky); l.Append(root); lower.Set(l);
    }

    static void Show(DrawingVisual v, double o) { if (Math.Abs(v.Opacity - o) > .004) v.Opacity = o; }
    static Color Hex(string h) => DogLook.Hex(h);
    static string Inv(FormattableString f) => FormattableString.Invariant(f);
    static Geometry G(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }
    static Geometry E(double x, double y, double rx, double ry) { var g = new EllipseGeometry(new Point(x, y), rx, ry); g.Freeze(); return g; }
    static Geometry U(params Geometry[] parts) { var g = new GeometryGroup { FillRule = FillRule.Nonzero }; foreach (var p in parts) g.Children.Add(p); g.Freeze(); return g; }
    static Geometry Capsule(double len, double r0, double r1) => G(Inv($"M {-r0},0 A {r0},{r0} 0 0 1 {r0},0 L {r1},{len} A {r1},{r1} 0 0 1 {-r1},{len} Z"));
    static Geometry Scaled(Geometry g, double sx) { if (sx == 1) return g; var c = g.Clone(); c.Transform = new ScaleTransform(sx, 1); c.Freeze(); return c; }
    static Geometry Shifted(Geometry g, double x, double y, double k) { var c = g.Clone(); var tg = new TransformGroup(); tg.Children.Add(new ScaleTransform(k, k)); tg.Children.Add(new TranslateTransform(x, y)); c.Transform = tg; c.Freeze(); return c; }
    static Geometry Widen(Geometry g, double width) { var w = g.GetWidenedPathGeometry(new Pen(Brushes.Black, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }); w.Freeze(); return w; }
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static SolidColorBrush Solid(Color c, double a = 1) => Frozen(new SolidColorBrush(c) { Opacity = a });
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Brush Round(Color light, Color dark)
    {
        var b = new RadialGradientBrush { GradientOrigin = new Point(.36, .26), Center = new Point(.42, .36), RadiusX = .8, RadiusY = .8 };
        b.GradientStops.Add(new GradientStop(Mix(light, Colors.White, .18), 0)); b.GradientStops.Add(new GradientStop(light, .42)); b.GradientStops.Add(new GradientStop(dark, 1));
        return Frozen(b);
    }
    static Brush Shadow(Color c)
    {
        var b = new RadialGradientBrush(); b.GradientStops.Add(new GradientStop(Color.FromArgb(70, c.R, c.G, c.B), 0)); b.GradientStops.Add(new GradientStop(Color.FromArgb(46, c.R, c.G, c.B), .55)); b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        return Frozen(b);
    }
}
