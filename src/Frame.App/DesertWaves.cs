using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Frame.App;

// Living desert (0.5.4): the lower dunes of the background flow slowly. The same background image is drawn again in seven
// overlapping horizontal bands over the lower half; each band has a soft vertical fade (opacity mask; neighbouring fades
// add up to exactly 1, the topmost band fades into the static image above) and its own small TranslateTransform. The
// offsets follow slow travelling waves (23 and 31 s periods, phase shifted per band) plus very slow pseudo-noise, a few
// pixels at most and larger in the foreground, so dune contours rise, fall and drift without a visible loop or seam.
// Cost: no pixel processing, shader or own clock; seven transform updates per step of CaptureAtmosphere's clock, composited
// by WPF's render thread from cached band bitmaps. When FRAME is not on screen (hidden, minimized, tray, cloaked, locked)
// or Windows animations are off, that clock is stopped, so nothing changes and nothing is re-rendered. The original image
// always lies beneath the bands, so a moved band can never reveal a gap.
sealed class DesertWaves
{
    const int Bands = 7;
    const double BandTop = .52;                    // upper half of the artwork stays still
    static readonly double Step = (1 - BandTop) / (Bands - 1);
    readonly Grid host = new() { IsHitTestVisible = false };
    readonly (Rectangle Rect, ImageBrush Brush, LinearGradientBrush Fade, TranslateTransform Move, double Depth)[] bands;
    double phase;

    public DesertWaves(Border backdrop, ImageSource image)
    {
        bands = new (Rectangle, ImageBrush, LinearGradientBrush, TranslateTransform, double)[Bands];
        for (int k = 0; k < Bands; k++)
        {
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, ViewportUnits = BrushMappingMode.Absolute };
            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            var move = new TranslateTransform();
            // BitmapCache: the band (image + fade mask) is rasterized once per size; a moved band only re-composites the cache.
            var rect = new Rectangle { Fill = brush, OpacityMask = fade, RenderTransform = move, VerticalAlignment = VerticalAlignment.Top, CacheMode = new BitmapCache() };
            bands[k] = (rect, brush, fade, move, k / (double)(Bands - 1));
            host.Children.Add(rect);
        }
        backdrop.Child = host; host.SizeChanged += (_, _) => Layout();
    }

    // Band k peaks at Centre(k) and fades to zero at its neighbours' centres; only the layout changes these brushes.
    static double Centre(int k) => BandTop + k * Step;
    void Layout()
    {
        double w = host.ActualWidth, h = host.ActualHeight;
        if (w <= 0 || h <= 0) return;
        for (int k = 0; k < Bands; k++)
        {
            double from = Centre(k - 1), peak = Centre(k), to = k == Bands - 1 ? 1 : Centre(k + 1);
            double top = from * h, height = Math.Max(1, (to - from) * h);
            var b = bands[k];
            b.Rect.Margin = new Thickness(0, top, 0, 0); b.Rect.Height = height; b.Rect.Width = w;
            b.Brush.Viewport = new Rect(0, -top, w, h); // the same mapping as the full background
            b.Fade.GradientStops.Clear();
            b.Fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            b.Fade.GradientStops.Add(new GradientStop(Colors.Black, (peak - from) / (to - from)));
            if (k < Bands - 1) b.Fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }
    }

    public void Restart() => phase = 0; // diagnostics: reproducible clips

    public void Advance(double seconds)
    {
        phase += seconds;
        for (int k = 0; k < Bands; k++)
        {
            var b = bands[k];
            double ax = 2 + 4 * b.Depth, ay = .8 + 2 * b.Depth; // foreground dunes move a little more
            double flow = .7 * Math.Sin(2 * Math.PI * (phase / 31 + k * .17)) + .3 * Drift.Noise(phase * .05, k);
            double swell = .7 * Math.Sin(2 * Math.PI * (phase / 23 + k * .23 + .3)) + .3 * Drift.Noise(phase * .07, k + 9);
            b.Move.X = ax * flow; b.Move.Y = ay * swell;
        }
    }
}
