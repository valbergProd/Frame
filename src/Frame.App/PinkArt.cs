using System.Windows;
using System.Windows.Media;

namespace Frame.App;

// Pink mode art (0.6.0): every illustrated shape of the scene, the effect icons and the Simply Pink background, drawn as
// vector paths in a cute sticker style (soft gradient fill, a slightly darker rose outline, a white gloss or vein). All
// resources are built once and frozen; sprites are centred on (0,0) at a nominal size of about 20 DIP (layers scale them).
// Palette: blush, rose, magenta, pastel pink and pink-lavender; red only for the occasional red flower; no greens.
static class PinkArt
{
    public static readonly Color Neon = Color.FromRgb(0xFF, 0x2E, 0x97);
    public static readonly SolidColorBrush NeonBrush = Solid(0xFFFF2E97), Plum = Solid(0xFF6E2550), Muted = Solid(0xFF8E3F68), Clear = Solid(0x00FFFFFF);

    public static SolidColorBrush Solid(uint argb) { var b = new SolidColorBrush(C(argb)); b.Freeze(); return b; }
    public static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    static Pen Pen(uint argb, double width) { var p = new Pen(Solid(argb), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }
    static Geometry G(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }
    static Geometry Ellipse(double x, double y, double rx, double ry, double angle = 0)
    {
        var e = new EllipseGeometry(new Point(x, y), rx, ry); if (angle != 0) e.Transform = new RotateTransform(angle, x, y);
        e.Freeze(); return e;
    }
    static LinearGradientBrush Linear(Point a, Point b, params (double At, uint Argb)[] stops)
    {
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = a, EndPoint = b };
        foreach (var (at, argb) in stops) brush.GradientStops.Add(new GradientStop(C(argb), at));
        brush.Freeze(); return brush;
    }
    static RadialGradientBrush Radial(Point centre, double radius, params (double At, uint Argb)[] stops)
    {
        var brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = centre, GradientOrigin = centre, RadiusX = radius, RadiusY = radius };
        foreach (var (at, argb) in stops) brush.GradientStops.Add(new GradientStop(C(argb), at));
        brush.Freeze(); return brush;
    }
    static GeometryDrawing Part(Brush? fill, Pen? pen, Geometry geometry) => new(fill, pen, geometry);
    static DrawingGroup Group(params Drawing[] parts) { var g = new DrawingGroup(); foreach (var p in parts) g.Children.Add(p); g.Freeze(); return g; }
    static DrawingGroup Mirrored(DrawingGroup source) { var g = new DrawingGroup { Transform = new ScaleTransform(-1, 1) }; g.Children.Add(source); g.Freeze(); return g; }
    static Geometry Transformed(Geometry g, Transform t) { var c = g.Clone(); c.Transform = t; c.Freeze(); return c; }

    // Sakura petal: obovate with the notched tip of a cherry petal, a vein and a one-sided sheen (so a flip shows the back).
    static readonly Geometry PetalShape = G("M 0,10 C -3.6,8.2 -7.6,3.2 -7.1,-2.8 C -6.8,-7.4 -4.2,-10.3 -1.9,-10 L 0,-7.3 L 1.9,-10 C 4.2,-10.3 6.8,-7.4 7.1,-2.8 C 7.6,3.2 3.6,8.2 0,10 Z");
    static readonly Geometry PetalVein = G("M 0,8.4 C -0.5,4 -0.4,0 0,-4.4"), PetalSheen = G("M -4.6,-5.6 C -5.5,-2.2 -4.9,1.4 -3.1,4.2");
    public static readonly Drawing[] Sakura = [Petal(0xFFFFF4F8, 0xFFFFCFE2, 0xFFF28CB8, 0xFFD3679D), Petal(0xFFFFE9F1, 0xFFFFB6D2, 0xFFEB6FA6, 0xFFC4588E), Petal(0xFFFFFFFF, 0xFFFFDDEB, 0xFFF6A9C9, 0xFFD97AA9), Petal(0xFFFFD6E8, 0xFFFF94C1, 0xFFE24F93, 0xFFB8447E)];
    static Drawing Petal(uint top, uint middle, uint bottom, uint edge) => Group(
        Part(Linear(new(0, -10), new(0, 10), (0, top), (.5, middle), (1, bottom)), Pen(edge, .85), PetalShape),
        Part(null, Pen(0x88E0709F, .55), PetalVein), Part(null, Pen(0x99FFFFFF, .9), PetalSheen));

    // Flowers: five-petal blossoms (pink, hot pink, pink-lavender, red), loose rose petals (pink, red) and small roses seen
    // from above (pink, red). Index: 0-2 pink blossoms, 3 red blossom, 4 pink petal, 5 red petal, 6 pink rose, 7 red rose.
    public static readonly Drawing[] Flowers = [Blossom(0xFFFFE6F0, 0xFFFFB3D1, 0xFFE68AB3, 0xFFFFE08A), Blossom(0xFFFFD0E6, 0xFFFF6AAE, 0xFFD2408A, 0xFFFFE08A), Blossom(0xFFFBE6FF, 0xFFE9A2F0, 0xFFBF6DC9, 0xFFFFE6A6), Blossom(0xFFFF9AA6, 0xFFEE2F4A, 0xFFA8172E, 0xFFFFD27A),
        RosePetal(0xFFFFE1EE, 0xFFFF8DBE, 0xFFD9558F), RosePetal(0xFFFF8A98, 0xFFE3283F, 0xFF9E1428), Rose(0xFFFFD3E6, 0xFFFF7FB6, 0xFFC7417F), Rose(0xFFFF8E9B, 0xFFE02A40, 0xFF96122A)];
    public static bool IsRed(int flower) => flower is 3 or 5 or 7;
    static Drawing Blossom(uint light, uint main, uint edge, uint centre)
    {
        var petals = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int k = 0; k < 5; k++) petals.Children.Add(new EllipseGeometry(new Point(0, -5.6), 4.3, 5.4) { Transform = new RotateTransform(k * 72) });
        petals.Freeze();
        var dots = new GeometryGroup(); for (int k = 0; k < 5; k++) { double a = (k * 72 + 36) * Math.PI / 180; dots.Children.Add(Ellipse(3.5 * Math.Sin(a), -3.5 * Math.Cos(a), .62, .62)); }
        dots.Freeze();
        return Group(Part(Radial(new(0, 0), 11, (0, 0xFFFFFFFF), (.32, light), (1, main)), Pen(edge, .7), petals),
            Part(Solid(0xFFE8508A), null, dots), Part(Radial(new(-.6, -.6), 3, (0, 0xFFFFFFFF), (.5, centre), (1, centre)), Pen(0xFFF0A63A, .5), Ellipse(0, 0, 2.5, 2.5)));
    }
    static Drawing RosePetal(uint light, uint main, uint edge) => Group(
        Part(Linear(new(-6, -8), new(5, 7), (0, light), (.55, main), (1, main)), Pen(edge, .7), G("M 0,7 C -6.2,6 -8,-2 -5,-6 C -3,-8.6 3,-8.6 5,-6 C 8,-2 6.2,6 0,7 Z")),
        Part(null, Pen(0x66FFFFFF, .9), G("M -4.2,-4 C -2,-6.4 2,-6.4 4.2,-4")), Part(null, Pen(0x55000000 | (edge & 0xFFFFFF), .6), G("M 0,5.6 C -.6,2 -.4,-1 0,-3.4")));
    static Drawing Rose(uint light, uint main, uint edge) => Group(
        Part(Radial(new(-1, -1.5), 9.5, (0, light), (.55, main), (1, main)), Pen(edge, .75), G("M -8,0 C -8,-6 -3,-8.5 0,-8.5 C 3,-8.5 8,-6 8,0 C 8,6 3,8.5 0,8.5 C -3,8.5 -8,6 -8,0 Z")),
        Part(null, Pen(edge, .75), G("M -1.5,1 C -4,-1 -1,-5 2,-3.5 C 4.5,-2 3,2.5 0,3 C -3.5,3.5 -6,-1 -3.5,-4.5 M 4.6,2.6 C 3.5,6 -3,6.6 -5.6,3.2 M -6.4,-2.2 C -5.2,-6 -.5,-7.4 3,-6.2")),
        Part(null, Pen(0x77FFFFFF, .8), G("M -2.6,-6.2 C -.6,-7.2 1.8,-7.1 3.4,-6.3")));

    // Hearts: chubby sticker hearts with a glossy highlight. 0 neon, 1 light pink, 2 pink-lavender, 3 rose.
    public static readonly Geometry HeartShape = G("M 0,8 C -2.2,6 -9.5,1.4 -9.5,-3.4 C -9.5,-7 -6.8,-9 -4.6,-9 C -2.4,-9 -.8,-7.6 0,-5.8 C .8,-7.6 2.4,-9 4.6,-9 C 6.8,-9 9.5,-7 9.5,-3.4 C 9.5,1.4 2.2,6 0,8 Z");
    public static readonly Drawing[] Hearts = [Heart(0xFFFF8CC3, 0xFFFF2E97, 0xFFD81B7A, 0xFFB0145F), Heart(0xFFFFD6E8, 0xFFFF9CC8, 0xFFF26EA9, 0xFFD85590), Heart(0xFFF9CDF3, 0xFFE592E8, 0xFFC460C9, 0xFFA24AA8), Heart(0xFFFFA8BC, 0xFFFF5C86, 0xFFE0305E, 0xFFB91E48)];
    static Drawing Heart(uint light, uint main, uint deep, uint edge) => Group(
        Part(Linear(new(-8, -9), new(7, 8), (0, light), (.45, main), (1, deep)), Pen(edge, .8), HeartShape),
        Part(Solid(0xE6FFFFFF), null, Ellipse(-4.7, -4.9, 2.4, 1.3, -42)), Part(Solid(0xCCFFFFFF), null, Ellipse(-1.7, -7, .75, .75)));

    // Sparkles: a four-point glint on a soft glow with a small cross twinkle; 3 is a tiny five-point star.
    static readonly Geometry Glint = G("M 0,-10 C .8,-2.2 2.2,-.8 10,0 C 2.2,.8 .8,2.2 0,10 C -.8,2.2 -2.2,.8 -10,0 C -2.2,-.8 -.8,-2.2 0,-10 Z");
    static readonly Geometry StarShape = G("M 0,-9 L 2.4,-3.1 L 8.6,-2.8 L 3.8,1.2 L 5.4,7.4 L 0,3.9 L -5.4,7.4 L -3.8,1.2 L -8.6,-2.8 L -2.4,-3.1 Z");
    public static readonly Drawing[] Sparkles = [Sparkle(0xFFFF8CC6, 0x99FF8CC6), Sparkle(0xFFFF2E97, 0x88FF4FA8), Sparkle(0xFFE7A6FF, 0x88E7A6FF), Group(
        Part(Radial(new(0, 0), 9, (0, 0x77FF9BCB), (1, 0x00FF9BCB)), null, Ellipse(0, 0, 9, 9)),
        Part(Radial(new(0, -1), 8, (0, 0xFFFFFFFF), (.45, 0xFFFFE3F1), (1, 0xFFFF8CC6)), Pen(0xCCFF6FB5, .6), StarShape))];
    static Drawing Sparkle(uint edge, uint glow) => Group(
        Part(Radial(new(0, 0), 7.5, (0, glow), (1, glow & 0xFFFFFF)), null, Ellipse(0, 0, 7.5, 7.5)),
        Part(Radial(new(0, 0), 10, (0, 0xFFFFFFFF), (.35, 0xFFFFF0F7), (1, edge)), null, Glint),
        Part(Solid(0xCCFFFFFF), null, Transformed(Glint, new MatrixTransform(.3, .3, -.3, .3, 0, 0))));

    // Butterfly (top view, head up): right and left wing pairs (separate so they can flap) and the body. Variants: pink,
    // lavender, coral rose, blush white.
    static readonly Geometry ForeWing = G("M .6,-1.5 C 2.5,-7.5 8.5,-13.5 13.2,-11.6 C 16.2,-10.2 14.6,-4.6 11.2,-2.2 C 8.2,-.2 3.6,.4 .6,.2 Z");
    static readonly Geometry HindWing = G("M .6,.6 C 4.6,.6 10.6,1.6 11.6,5.6 C 12.4,9.2 8.4,11.8 5.4,10.4 C 2.8,9.2 1.2,5 .6,2.2 Z");
    public static readonly (Drawing Left, Drawing Right, Drawing Body)[] Butterflies = [Butterfly(0xFFE83F8E, 0xFFFF7DB8, 0xFFFFD2E8, 0xFF7A1E4F), Butterfly(0xFFB34FC9, 0xFFDC8CEB, 0xFFF7D9FB, 0xFF5E2470), Butterfly(0xFFE8456B, 0xFFFF8FA8, 0xFFFFE0E7, 0xFF7E1F37), Butterfly(0xFFF06AA8, 0xFFFFB8D6, 0xFFFFFFFF, 0xFF8A2A5C)];
    static (Drawing, Drawing, Drawing) Butterfly(uint deep, uint main, uint tip, uint edge)
    {
        var fill = Radial(new(0, 0), 15, (0, deep), (.5, main), (1, tip));
        var spots = new GeometryGroup(); spots.Children.Add(Ellipse(11.4, -9, 1.15, 1.15)); spots.Children.Add(Ellipse(13, -6.4, .8, .8)); spots.Children.Add(Ellipse(8.3, 6.8, 1, 1)); spots.Freeze();
        var veins = G("M 1.4,-.8 C 5,-3.6 9,-7.4 12,-10 M 1.6,1.6 C 4.6,3.4 7.2,6 8.6,9");
        var right = Group(Part(fill, Pen(edge, .75), HindWing), Part(fill, Pen(edge, .75), ForeWing), Part(null, Pen(0x44000000 | (edge & 0xFFFFFF), .5), veins), Part(Solid(0xEEFFFFFF), null, spots));
        var body = Group(Part(null, Pen(0xFF5B1840, .6), G("M -.4,-6.8 C -1.2,-9.6 -2.6,-11.8 -4,-12.4 M .4,-6.8 C 1.2,-9.6 2.6,-11.8 4,-12.4")),
            Part(Solid(0xFF5B1840), null, Ellipse(-4, -12.4, .75, .75)), Part(Solid(0xFF5B1840), null, Ellipse(4, -12.4, .75, .75)),
            Part(Linear(new(-1, 0), new(1.4, 0), (0, 0xFF7A2A57), (1, 0xFF4A1233)), null, Ellipse(0, 1, 1.3, 5.8)), Part(Solid(0xFF5B1840), null, Ellipse(0, -5.6, 1.6, 1.6)));
        return (Mirrored(right), right, body);
    }

    // Bow (loose satin bow; the ribbons carry one at their head) and the ribbon colours.
    public static readonly Drawing Bow = Group(
        Part(Linear(new(0, -9), new(0, 13), (0, 0xFFFF8CC3), (.5, 0xFFFF3E9E), (1, 0xFFD81B7A)), Pen(0xFFA8125A, .7), G("M .8,1.6 C 2.6,4.6 4.6,8.2 6.4,11.6 L 4.2,10.8 L 3.2,13.2 C 1.8,9.6 .2,5.6 -.6,2 Z M -.8,1.6 C -2.6,4.6 -4.6,8.2 -6.4,11.6 L -4.2,10.8 L -3.2,13.2 C -1.8,9.6 -.2,5.6 .6,2 Z")),
        Part(Linear(new(0, -9), new(0, 4), (0, 0xFFFFA3CF), (1, 0xFFFF3E9E)), Pen(0xFFA8125A, .7), G("M 1.6,-1.2 C 4.6,-7.6 12.6,-9.2 12.8,-2.4 C 13,3.2 6.2,3.6 1.6,1.2 Z M -1.6,-1.2 C -4.6,-7.6 -12.6,-9.2 -12.8,-2.4 C -13,3.2 -6.2,3.6 -1.6,1.2 Z")),
        Part(Solid(0x40A0105A), null, G("M 3.4,-.6 C 5.8,-4.4 10.2,-5 10.4,-1.8 C 10.4,.6 6.6,.8 3.4,-.1 Z M -3.4,-.6 C -5.8,-4.4 -10.2,-5 -10.4,-1.8 C -10.4,.6 -6.6,.8 -3.4,-.1 Z")),
        Part(null, Pen(0xA6FFFFFF, .9), G("M 4,-4.6 C 6.6,-6.8 10,-7.2 11.4,-5 M -4,-4.6 C -6.6,-6.8 -10,-7.2 -11.4,-5")),
        Part(Linear(new(0, -2.6), new(0, 2.6), (0, 0xFFFF6FB0), (1, 0xFFC8156E)), Pen(0xFFA8125A, .7), new RectangleGeometry(new Rect(-2.4, -2.6, 4.8, 5.2), 1.6, 1.6)));
    public static readonly SolidColorBrush RibbonFront = Solid(0xFFFF7DB8), RibbonBack = Solid(0xFFE0479A), RibbonSheen = Solid(0xB3FFD0E6);
    public static readonly Pen RibbonEdge = Pen(0x99B8166A, .6);

    // Secrets: a tiny heart, a heart-shaped sugar cookie with sprinkles, a wishing star; sprinkles for the bursts.
    public static readonly Drawing Cookie = Group(
        Part(Linear(new(-8, -9), new(7, 8), (0, 0xFFF6D3B0), (1, 0xFFE0A877)), Pen(0xFFB9784C, .8), HeartShape),
        Part(Linear(new(-7, -8), new(6, 6), (0, 0xFFFFD3E6), (1, 0xFFFF8CC3)), null, Transformed(HeartShape, new ScaleTransform(.74, .74, 0, -.6))),
        Part(null, Pen(0xFFFFFFFF, 1), G("M -3.6,-3.4 L -2.4,-2.8 M 2.4,-4.4 L 3.4,-3.4 M -.6,0 L .6,.8 M 3,.4 L 2,1.4 M -4.8,-.6 L -4,.4")),
        Part(null, Pen(0xFFFF2E97, 1), G("M -1.2,-4.6 L 0,-4.2 M 4.6,-1.6 L 5,-.4 M -2.6,2 L -1.6,2.6")));
    public static readonly Drawing[] Sprinkles = [Sprinkle(0xFFFF2E97), Sprinkle(0xFFFFFFFF), Sprinkle(0xFFE592E8), Sprinkle(0xFFFF9CC8)];
    static Drawing Sprinkle(uint argb) => Group(Part(null, Pen(argb, 2.4), G("M -2.6,0 L 2.6,0")), Part(null, Pen(0x55FFFFFF, .8), G("M -1.6,-.4 L 1.6,-.4")));

    // Effect icons (16 x 16, filled with the icon colour): heart, flower, sakura blossom, sparkle, butterfly, bow, dog.
    public static readonly Geometry[] Icons = BuildIcons();
    static Geometry[] BuildIcons()
    {
        var heart = Transformed(HeartShape, new MatrixTransform(.74, 0, 0, .74, 8, 8.6));
        Geometry flower = Geometry.Empty;
        for (int k = 0; k < 5; k++) { double a = k * 72 * Math.PI / 180; flower = Geometry.Combine(flower, new EllipseGeometry(new Point(8 + 3.7 * Math.Sin(a), 8 - 3.7 * Math.Cos(a)), 3, 3), GeometryCombineMode.Union, null); }
        flower = Geometry.Combine(flower, new EllipseGeometry(new Point(8, 8), 1.7, 1.7), GeometryCombineMode.Exclude, null);
        var flowerIcon = new GeometryGroup(); flowerIcon.Children.Add(flower); flowerIcon.Children.Add(new EllipseGeometry(new Point(8, 8), .95, .95));
        Geometry sakura = Geometry.Empty;
        for (int k = 0; k < 5; k++) { var m = new Matrix(); m.Translate(0, -10); m.Scale(.37, .37); m.Rotate(k * 72); m.Translate(8, 8); sakura = Geometry.Combine(sakura, Transformed(PetalShape, new MatrixTransform(m)), GeometryCombineMode.Union, null); }
        var sparkle = new GeometryGroup(); sparkle.Children.Add(Transformed(Glint, new MatrixTransform(.68, 0, 0, .68, 6.8, 9))); sparkle.Children.Add(Transformed(Glint, new MatrixTransform(.3, 0, 0, .3, 12.8, 3.4)));
        Geometry butterfly = Geometry.Empty;
        foreach (var wing in new[] { ForeWing, HindWing })
        {
            butterfly = Geometry.Combine(butterfly, Transformed(wing, new MatrixTransform(.52, 0, 0, .52, 8, 8.8)), GeometryCombineMode.Union, null);
            butterfly = Geometry.Combine(butterfly, Transformed(wing, new MatrixTransform(-.52, 0, 0, .52, 8, 8.8)), GeometryCombineMode.Union, null);
        }
        butterfly = Geometry.Combine(butterfly, new EllipseGeometry(new Point(8, 9.2), .9, 3.6), GeometryCombineMode.Union, null);
        var bow = Transformed(G("M .8,1.6 C 2.6,4.6 4.6,8.2 6.4,11.6 L 4.2,10.8 L 3.2,13.2 C 1.8,9.6 .2,5.6 -.6,2 Z M -.8,1.6 C -2.6,4.6 -4.6,8.2 -6.4,11.6 L -4.2,10.8 L -3.2,13.2 C -1.8,9.6 -.2,5.6 .6,2 Z M 1.6,-1.2 C 4.6,-7.6 12.6,-9.2 12.8,-2.4 C 13,3.2 6.2,3.6 1.6,1.2 Z M -1.6,-1.2 C -4.6,-7.6 -12.6,-9.2 -12.8,-2.4 C -13,3.2 -6.2,3.6 -1.6,1.2 Z M -2.4,-2.6 L 2.4,-2.6 L 2.4,2.6 L -2.4,2.6 Z"), new MatrixTransform(.58, 0, 0, .58, 8, 7));
        Geometry dog = Geometry.Combine(new EllipseGeometry(new Point(8, 9), 4.7, 4.4), G("M 3.8,5.4 C 1.4,5 .4,8.4 1.2,11.2 C 1.8,12.8 3.6,12 4.3,9.8 Z M 12.2,5.4 C 14.6,5 15.6,8.4 14.8,11.2 C 14.2,12.8 12.4,12 11.7,9.8 Z"), GeometryCombineMode.Union, null);
        var features = new GeometryGroup(); features.Children.Add(new EllipseGeometry(new Point(6.3, 8.3), .85, .95)); features.Children.Add(new EllipseGeometry(new Point(9.7, 8.3), .85, .95)); features.Children.Add(new EllipseGeometry(new Point(8, 10.9), 1.25, .9));
        dog = Geometry.Combine(dog, features, GeometryCombineMode.Exclude, null);
        var icons = new Geometry[] { heart, flowerIcon, sakura, sparkle, butterfly, bow, dog };
        foreach (var icon in icons) if (!icon.IsFrozen) icon.Freeze();
        return icons;
    }

    // Simply Pink: a clean blush-to-rose gradient with two very soft lights; no scenery, no motion.
    public static void DrawSimplyPink(DrawingContext dc, Size size)
    {
        var all = new Rect(size);
        dc.DrawRectangle(SimplyBase, null, all);
        dc.DrawEllipse(SimplyLight, null, new Point(size.Width * .82, size.Height * .08), size.Width * .55, size.Height * .62);
        dc.DrawEllipse(SimplyBlush, null, new Point(size.Width * .12, size.Height * .96), size.Width * .5, size.Height * .45);
    }
    static readonly Brush SimplyBase = Frozen(new LinearGradientBrush(new GradientStopCollection { new(C(0xFFFFE8F2), 0), new(C(0xFFFAD2E4), .55), new(C(0xFFF5BCD6), 1) }, new Point(0, 0), new Point(1, 1)));
    static readonly Brush SimplyLight = Frozen(new RadialGradientBrush(C(0x99FFFFFF), C(0x00FFFFFF)));
    static readonly Brush SimplyBlush = Frozen(new RadialGradientBrush(C(0x40FF9CC8), C(0x00FF9CC8)));
    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    public static DrawingBrush SimplyPinkBrush()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open()) DrawSimplyPink(dc, new Size(160, 90));
        var brush = new DrawingBrush(group) { Stretch = Stretch.UniformToFill }; brush.Freeze(); return brush;
    }
}
