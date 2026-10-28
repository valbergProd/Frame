using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

// Trim timeline: thumbnail strip, dimmed outside the selection, two handles and a playhead.
// Pure drawing + mouse; selection rules live in Core TrimSelection. Redraws only on input, new thumbnails or playhead moves.
public sealed class TrimTimeline : FrameworkElement
{
    const double Strip = 64, HandleWidth = 10, Grab = 14, Top = 4;
    static readonly SolidColorBrush Dim = Frozen(Color.FromArgb(170, 12, 10, 8)), Empty = Frozen(Color.FromArgb(255, 38, 32, 28)), Shadow = Frozen(Color.FromArgb(120, 0, 0, 0));
    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    TrimSelection? selection;
    BitmapSource?[] thumbs = [];
    double position;
    enum Part { None, Start, End, Playhead }
    Part dragging, hover;
    public event Action<double, bool>? Seek; // time, final (mouse released)
    public event Action? Edited;

    public TrimTimeline() { Height = Strip + 30; Focusable = false; Cursor = Cursors.Hand; ClipToBounds = false; }

    public void Attach(TrimSelection value, int thumbnailCount)
    {
        if (selection != null) selection.Changed -= InvalidateVisual;
        selection = value; selection.Changed += InvalidateVisual; thumbs = new BitmapSource?[thumbnailCount]; InvalidateVisual();
    }
    public void Detach() { if (selection != null) selection.Changed -= InvalidateVisual; selection = null; thumbs = []; Seek = null; Edited = null; }
    public void SetThumbnail(int index, BitmapSource image) { if (index >= 0 && index < thumbs.Length) { thumbs[index] = image; InvalidateVisual(); } }
    public int Thumbnails => thumbs.Count(t => t != null);
    public double Position { get => position; set { if (Math.Abs(value - position) < 1e-4) return; position = value; InvalidateVisual(); } }

    double Width0 => Math.Max(1, ActualWidth - 2 * HandleWidth);
    double X(double t) => selection == null ? HandleWidth : HandleWidth + t / selection.Duration * Width0;
    double T(double x) => selection == null ? 0 : Math.Clamp((x - HandleWidth) / Width0 * selection.Duration, 0, selection.Duration);

    protected override void OnRender(DrawingContext dc)
    {
        if (selection == null || ActualWidth <= 2 * HandleWidth) return;
        var strip = new Rect(HandleWidth, Top, Width0, Strip);
        dc.DrawRectangle(Empty, null, strip);
        if (thumbs.Length > 0)
        {
            double w = strip.Width / thumbs.Length;
            dc.PushClip(new RectangleGeometry(strip));
            for (int i = 0; i < thumbs.Length; i++)
                if (thumbs[i] is { } image)
                {
                    double drawW = Math.Max(w, Strip * image.PixelWidth / Math.Max(1, image.PixelHeight));
                    dc.DrawImage(image, new Rect(strip.X + i * w + (w - drawW) / 2, strip.Y, drawW, Strip));
                }
            dc.Pop();
        }
        double s = X(selection.Start), e = X(selection.End);
        if (s > strip.Left) dc.DrawRectangle(Dim, null, new Rect(strip.Left, strip.Top, s - strip.Left, Strip));
        if (e < strip.Right) dc.DrawRectangle(Dim, null, new Rect(e, strip.Top, strip.Right - e, Strip));
        var accent = (Brush)FindResource("Accent"); var text = (Brush)FindResource("Text"); var muted = (Brush)FindResource("Muted");
        var edge = new Pen(accent, 2); edge.Freeze();
        dc.DrawLine(edge, new Point(s, strip.Top + 1), new Point(e, strip.Top + 1));
        dc.DrawLine(edge, new Point(s, strip.Bottom - 1), new Point(e, strip.Bottom - 1));
        Handle(dc, s - HandleWidth, accent, hover == Part.Start || dragging == Part.Start, true);
        Handle(dc, e, accent, hover == Part.End || dragging == Part.End, false);
        double p = X(Math.Clamp(position, 0, selection.Duration));
        dc.DrawRectangle(Shadow, null, new Rect(p - 1.5, strip.Top - 3, 3, Strip + 6));
        dc.DrawRectangle(text, null, new Rect(p - .75, strip.Top - 3, 1.5, Strip + 6));
        dc.DrawEllipse(text, null, new Point(p, strip.Top - 3), 3.5, 3.5);
        // Sparse readable ticks: about one label per 110px, on round second values.
        double step = NiceStep(selection.Duration * 110 / Width0);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (double t = 0; t <= selection.Duration + 1e-6; t += step)
        {
            var label = new FormattedText(TimeText.Short(t), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, muted, dpi);
            double x = Math.Clamp(X(t) - label.Width / 2, 0, ActualWidth - label.Width);
            dc.DrawText(label, new Point(x, strip.Bottom + 9));
        }
    }
    static double NiceStep(double raw) { foreach (var s in new[] { 1d, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 }) if (s >= raw) return s; return 7200; }
    static void Handle(DrawingContext dc, double x, Brush accent, bool hot, bool left)
    {
        var r = new Rect(x, Top - 1, HandleWidth, Strip + 2);
        dc.PushOpacity(hot ? 1 : .88);
        dc.DrawRoundedRectangle(accent, null, r, 3, 3);
        var grip = new Pen(new SolidColorBrush(Color.FromArgb(150, 20, 14, 10)), 1.2); grip.Freeze();
        dc.DrawLine(grip, new Point(x + HandleWidth / 2, r.Top + Strip / 2 - 8), new Point(x + HandleWidth / 2, r.Top + Strip / 2 + 8));
        dc.Pop();
    }

    Part HitTest(Point p)
    {
        if (selection == null) return Part.None;
        double s = X(selection.Start), e = X(selection.End);
        double ds = Math.Abs(p.X - (s - HandleWidth / 2)), de = Math.Abs(p.X - (e + HandleWidth / 2));
        if (ds <= Grab || de <= Grab) return ds < de || (ds == de && p.X < s) ? Part.Start : Part.End;
        return Part.Playhead;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (selection == null) return;
        dragging = HitTest(e.GetPosition(this)); CaptureMouse(); Apply(e.GetPosition(this), false); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var p = e.GetPosition(this);
        if (dragging != Part.None && e.LeftButton == MouseButtonState.Pressed) { Apply(p, false); return; }
        var next = HitTest(p); if (next != hover) { hover = next; Cursor = next is Part.Start or Part.End ? Cursors.SizeWE : Cursors.Hand; InvalidateVisual(); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (dragging == Part.None) return;
        Apply(e.GetPosition(this), true); dragging = Part.None; ReleaseMouseCapture(); InvalidateVisual();
    }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if (hover != Part.None) { hover = Part.None; InvalidateVisual(); } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); dragging = Part.None; }

    // Handles move the selection and show that frame; the playhead scrubs. Seek is throttled by the editor.
    void Apply(Point p, bool final)
    {
        if (selection == null) return;
        double t = T(dragging == Part.Start ? p.X + HandleWidth / 2 : dragging == Part.End ? p.X - HandleWidth / 2 : p.X);
        switch (dragging)
        {
            case Part.Start: selection.SetStart(t); Edited?.Invoke(); Seek?.Invoke(selection.Start, final); break;
            case Part.End: selection.SetEnd(t); Edited?.Invoke(); Seek?.Invoke(selection.End, final); break;
            case Part.Playhead: Position = t; Seek?.Invoke(t, final); break;
        }
    }
    // Test hook: drive the same code path as mouse dragging.
    internal void SimulateDrag(bool start, double fromX, double toX, int steps)
    {
        dragging = start ? Part.Start : Part.End;
        for (int i = 0; i <= steps; i++) Apply(new Point(fromX + (toX - fromX) * i / steps, Top + Strip / 2), i == steps);
        dragging = Part.None;
    }
    internal double XOf(double t) => X(t);
}
