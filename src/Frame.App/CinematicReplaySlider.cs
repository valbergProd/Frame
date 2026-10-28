using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Frame.App;

// Cinematic D replay length (0.6.0): a horizontal slider over the real choices (CaptureView.ReplayChoices: 1, 2, 3, 5, 10,
// 15, 30, 60 minutes plus a custom saved value), evenly spaced, each with a tick and its minute label (seconds with "s").
// A thin graphite track, the selected range in gold, a ring handle and the selected label emphasised. Mouse: press anywhere
// (the handle jumps there) and drag (it follows the pointer, the nearest value is emphasised), release commits; wheel and
// arrow keys/PageUp/PageDown/+/- step and commit, Home/End reach the limits, Escape cancels a drag. Committed raises the
// new seconds (the layout calls ICaptureCommands.SetReplaySeconds). Vector-drawn in OnRender only on change; the stone's
// light reaches it through one non-frozen brush (Warmth: halo around the handle and a glow under the range), so the clock
// never re-renders the control itself. Labels are cached FormattedText (rebuilt on choices/DPI/size changes).
public sealed class CinematicReplaySlider : Control
{
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB4, 0x6E, 0x5E, 0x51)));
    static readonly Brush TrackLively = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x84, 0x72, 0x63)));
    static readonly Brush RangeBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0xB4, 0x70, 0x36), Color.FromRgb(0xF2, 0xB6, 0x62), 0));
    static readonly Pen TickIdle = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x9C, 0x9E, 0x8C, 0x7C))), 1));
    static readonly Pen TickLit = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0xE6, 0xA8, 0x5C))), 1));
    static readonly Brush HandleFill = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x16, 0x11)));
    static readonly Pen HandleRing = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xB2, 0x5E))), 2.2));
    static readonly Pen HandleRingLively = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x84))), 2.4));
    static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA9, 0x99, 0x8A)));
    static readonly Brush SelectedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xC4)));
    static readonly Pen FocusPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC8, 0xC7, 0xB8, 0xAA))), 1) { DashStyle = Frozen(new DashStyle([1, 2.5], 0)) });
    // Warm light from the stone: alpha follows Warmth (set per clock step); drawn as the handle halo and the range glow.
    readonly RadialGradientBrush halo = new();
    readonly SolidColorBrush rangeGlow = new(Color.FromArgb(0x5A, 0xFF, 0x9A, 0x48));
    const double Inset = 12, HandleRadius = 10.5;
    int[] values = [60, 120, 180, 300, 600, 900, 1800, 3600];
    int index = 3, committed = 300;
    double dragX;
    bool finishing, keyboardCue;
    FormattedText[] labels = [], selected = [];
    double labelSize = 13.5, selectedSize = 17, cachedDip;
    public double TrackY { get; set; } = 17;
    public int Seconds => values[index];
    public event Action<int>? Committed;
    public event Action<int>? Previewed;   // value under the pointer while dragging (live duration text)
    public bool IsDragging => IsMouseCaptured;

    public CinematicReplaySlider()
    {
        Focusable = true; IsTabStop = true; FocusVisualStyle = null; Cursor = Cursors.Hand; SnapsToDevicePixels = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "Replay length");
        System.Windows.Automation.AutomationProperties.SetHelpText(this, "How many minutes Shadow Recording keeps for saving");
        for (int k = 0; k <= 8; k++) { double r = k / 8d, f = (1 - r * r) * (1 - r * r); halo.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(150 * f), 255, 150, 66), r)); }
        halo.Opacity = rangeGlow.Opacity = 0;
        IsEnabledChanged += (_, _) => { if (!IsEnabled && IsMouseCaptured) Cancel(); InvalidateVisual(); };
    }
    public void SetFonts(double label, double emphasised)
    {
        if (label == labelSize && emphasised == selectedSize) return;
        labelSize = label; selectedSize = emphasised; labels = []; InvalidateVisual();
    }
    // Warmth 0..1: the stone's current light (no re-render: brush opacity only).
    public void Warm(double warmth) { halo.Opacity = Math.Clamp(warmth, 0, 1); rangeGlow.Opacity = Math.Clamp(warmth * .8, 0, 1); }
    public void Show(IReadOnlyList<int> choices, int seconds)
    {
        if (IsDragging) return;
        bool same = choices.Count == values.Length && choices.SequenceEqual(values);
        if (same && committed == seconds && Seconds == seconds) return;
        if (!same) { values = choices.Count > 0 ? [.. choices] : [seconds]; labels = []; }
        committed = seconds; index = Math.Max(0, Array.IndexOf(values, seconds)); InvalidateVisual();
    }
    static string Label(int seconds) => seconds < 60 ? seconds + "s" : $"{seconds / 60d:0.##}";

    double X0 => Inset;
    double X1 => Math.Max(X0 + 1, ActualWidth - Inset);
    double XFor(int i) => X0 + (X1 - X0) * i / Math.Max(1, values.Length - 1);
    int IndexFor(double x) => (int)Math.Round(Math.Clamp((x - X0) / (X1 - X0), 0, 1) * (values.Length - 1), MidpointRounding.AwayFromZero);
    double HandleX => IsDragging ? dragX : XFor(index);

    void Commit()
    {
        int old = committed; if (Seconds == old) return;
        Committed?.Invoke(Seconds);
        if (UIElementAutomationPeer.FromElement(this) is ReplayPeer p) p.RaisePropertyChangedEvent(System.Windows.Automation.RangeValuePatternIdentifiers.ValueProperty, (double)old, (double)Seconds);
    }
    void Cancel() { ReleaseMouseCapture(); index = Math.Max(0, Array.IndexOf(values, committed)); Previewed?.Invoke(Seconds); InvalidateVisual(); }
    void Step(int to) { if (IsDragging) Cancel(); to = Math.Clamp(to, 0, values.Length - 1); if (to == index) return; index = to; Previewed?.Invoke(Seconds); InvalidateVisual(); Commit(); }
    void MoveTo(double x)
    {
        dragX = Math.Clamp(x, X0, X1); int next = IndexFor(dragX);
        if (next != index) { index = next; Previewed?.Invoke(Seconds); }
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); e.Handled = true; keyboardCue = false;
        if (!IsEnabled) return; Focus(); if (!CaptureMouse()) return;
        MoveTo(e.GetPosition(this).X);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured) MoveTo(e.GetPosition(this).X); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); e.Handled = true;
        if (!IsMouseCaptured) return;
        finishing = true; ReleaseMouseCapture(); finishing = false; InvalidateVisual(); Commit();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); if (!finishing) Cancel(); }
    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { base.OnMouseWheel(e); if (!IsEnabled || IsDragging) return; Step(index + Math.Sign(e.Delta)); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;
        if (e.Key == Key.Escape) { if (IsDragging) { Cancel(); e.Handled = true; } return; }
        int delta = e.Key is Key.Right or Key.Up or Key.PageUp or Key.Add or Key.OemPlus ? 1 : e.Key is Key.Left or Key.Down or Key.PageDown or Key.Subtract or Key.OemMinus ? -1 : 0;
        if (delta == 0 && e.Key is not (Key.Home or Key.End)) return;
        keyboardCue = true; Step(e.Key == Key.Home ? 0 : e.Key == Key.End ? values.Length - 1 : index + delta); e.Handled = true; InvalidateVisual();
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); keyboardCue = InputManager.Current.MostRecentInputDevice is KeyboardDevice; InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); if (IsDragging) Cancel(); keyboardCue = false; InvalidateVisual(); }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); labels = []; InvalidateVisual(); }

    void BuildLabels()
    {
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (labels.Length == values.Length && dip == cachedDip) return;
        cachedDip = dip; labels = new FormattedText[values.Length]; selected = new FormattedText[values.Length];
        var normal = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal); var bold = new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        for (int i = 0; i < values.Length; i++)
        {
            labels[i] = new FormattedText(Label(values[i]), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, normal, labelSize, LabelBrush, dip);
            selected[i] = new FormattedText(Label(values[i]), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold, selectedSize, SelectedBrush, dip);
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight; if (w <= 2 * Inset || h <= 0) return;
        BuildLabels();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // the whole control is the hit area
        if (!IsEnabled) dc.PushOpacity(.45);
        bool lively = IsEnabled && (IsMouseOver || IsDragging || IsKeyboardFocused);
        double y = TrackY, x = HandleX;
        dc.DrawRectangle(rangeGlow, null, new Rect(X0, y - 2.5, Math.Max(0, x - X0), 5));                       // warm light under the range
        dc.DrawRoundedRectangle(lively ? TrackLively : TrackBrush, null, new Rect(X0, y - .75, X1 - X0, 1.5), .75, .75);
        if (x > X0) dc.DrawRoundedRectangle(RangeBrush, null, new Rect(X0, y - 1.1, x - X0, 2.2), 1.1, 1.1);
        double labelMid = y + 25 + selectedSize * .62;
        for (int i = 0; i < values.Length; i++)
        {
            double tx = Math.Round(XFor(i)) + .5;
            dc.DrawLine(tx <= x + .5 ? TickLit : TickIdle, new Point(tx, y - 5), new Point(tx, y + 5));
            var text = i == index ? selected[i] : labels[i];
            double left = Math.Clamp(XFor(i) - text.Width / 2, 0, w - text.Width);
            dc.DrawText(text, new Point(left, labelMid - text.Height / 2));
        }
        dc.DrawEllipse(halo, null, new Point(x, y), HandleRadius * 3.2, HandleRadius * 3.2);
        dc.DrawEllipse(HandleFill, lively ? HandleRingLively : HandleRing, new Point(x, y), HandleRadius, HandleRadius);
        if (IsKeyboardFocused && keyboardCue) dc.DrawEllipse(null, FocusPen, new Point(x, y), HandleRadius + 5, HandleRadius + 5);
        if (!IsEnabled) dc.Pop();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ReplayPeer(this);
    sealed class ReplayPeer(CinematicReplaySlider owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => "Replay length slider";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 60; public double SmallChange => 60; public double Maximum => owner.values[^1]; public double Minimum => owner.values[0]; public double Value => owner.Seconds;
        public void SetValue(double value)
        {
            if (IsReadOnly) throw new InvalidOperationException("The replay length cannot be changed right now.");
            if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            owner.Step(Enumerable.Range(0, owner.values.Length).MinBy(i => Math.Abs(owner.values[i] - value)));
        }
    }
}
