using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Frame.App;

// Minute slider (0.5.4): replaces the history dial. Same discrete values (HistoryDial.Values: 1, 2, 3, 5, 10, 15, 30,
// 60 min plus a custom persisted value), same Committed/persistence path. 0.5.5: easier to recognise as the replay-length
// control (second in the hierarchy after the Shadow stone): a higher-contrast graphite track, the selected range in
// restrained gold, a larger graphite handle with a gold rim, the value brighter and larger, and a small "replay length"
// caption under it (the configured length to keep, never elapsed time). Still no glow, halo, timer or storyboard:
// vector-drawn in OnRender only on change, nothing runs while FRAME is idle, hidden or in the tray. The whole control is
// the (invisible) hit area, so the handle stays easy to grab.
public sealed class HistorySliderControl : Control
{
    static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x55, 0x4D, 0x47)));     // graphite, readable on the dunes
    static readonly Brush TrackHover = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x68, 0x5E, 0x57)));
    static readonly Brush RangeBrush = Frozen(new LinearGradientBrush(Color.FromArgb(0xD8, 0xD9, 0xA2, 0x5C), Color.FromArgb(0xA8, 0xA8, 0x72, 0x3E), 90)); // restrained gold, fading down
    static readonly Brush HandleFill = Frozen(new LinearGradientBrush(Color.FromRgb(0x7C, 0x74, 0x6D), Color.FromRgb(0x40, 0x3B, 0x37), 90)); // slight bevel
    static readonly Brush HandleHover = Frozen(new LinearGradientBrush(Color.FromRgb(0x92, 0x89, 0x80), Color.FromRgb(0x4B, 0x45, 0x40), 90));
    static readonly Pen HandleEdge = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC8, 0x93, 0x52)), 1)); // gold rim
    static readonly Pen HandleLight = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xF4, 0xE8)), 1));
    static readonly Pen Accent = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0xE0, 0xAA, 0x62)), 1.2));
    public static readonly Brush WarmGrey = Frozen(new SolidColorBrush(Color.FromRgb(0xE2, 0xD5, 0xC6)));          // value text (dark theme): light warm grey
    static readonly Brush CaptionBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xB4, 0xA6, 0x98)));
    Brush valueBrush = WarmGrey, captionBrush = CaptionBrush;
    public Brush ValueBrush { get => valueBrush; set { valueBrush = value; InvalidateVisual(); } } // set per theme in ApplyTheme
    public Brush CaptionText { get => captionBrush; set { captionBrush = value; InvalidateVisual(); } } // set per theme in ApplyTheme
    public static readonly Brush DarkCaption = CaptionBrush;
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    // Handle: HandleW x HandleH pill; Stone = vertical travel margin (also the grab tolerance). TrackX from the left;
    // the value text ends LabelGap left of the handle.
    const double Stone = 22, HandleW = 26, HandleH = 9, TrackWidth = 3, TrackX = 92, LabelGap = 10, Pad = 6;
    public const double HandleRight = TrackX + HandleW / 2; // for layout next to the Shadow stone
    int[] values = HistoryDial.Values(300);
    int index = 3, committed = 300;
    double dragY, grab;
    bool finishingDrag;
    public int Seconds => values[index];
    public event Action<int>? Committed;
    public bool IsDragging => IsMouseCaptured;
    public HistorySliderControl()
    {
        Focusable = true; Cursor = Cursors.Hand; FocusVisualStyle = null; MinWidth = 100; MinHeight = 60;
        System.Windows.Automation.AutomationProperties.SetName(this, "Replay length"); System.Windows.Automation.AutomationProperties.SetHelpText(this, "How many minutes Shadow Recording keeps for saving");
        IsEnabledChanged += (_, _) => { if (!IsEnabled && IsMouseCaptured) Cancel(); InvalidateVisual(); };
    }
    public void SetSeconds(int seconds) { if (IsDragging || (committed == seconds && Seconds == seconds)) return; committed = seconds; values = HistoryDial.Values(seconds); index = Array.IndexOf(values, seconds); InvalidateVisual(); }

    // Stone centre range: top = longest history, bottom = shortest. Values are evenly spaced steps.
    double Top => Pad + Stone / 2;
    double Bottom => Math.Max(Top + 1, ActualHeight - Pad - Stone / 2);
    internal double YFor(int i) => Bottom - (Bottom - Top) * i / Math.Max(1, values.Length - 1);
    int IndexFor(double y) => (int)Math.Round(Math.Clamp((Bottom - y) / (Bottom - Top), 0, 1) * (values.Length - 1), MidpointRounding.AwayFromZero);
    double ThumbY => IsDragging ? dragY : YFor(index);
    internal Point PointFor(int i) => new(TrackX, YFor(i));

    void Commit() { int old = committed; Committed?.Invoke(Seconds); if (UIElementAutomationPeer.FromElement(this) is HistoryPeer p && old != Seconds) p.RaisePropertyChangedEvent(System.Windows.Automation.RangeValuePatternIdentifiers.ValueProperty, (double)old, (double)Seconds); }
    void Cancel() { ReleaseMouseCapture(); SetSeconds(committed); InvalidateVisual(); }
    void Step(int to) { if (IsDragging) Cancel(); to = Math.Clamp(to, 0, values.Length - 1); if (to == index) return; index = to; InvalidateVisual(); Commit(); }

    // Grabbing the stone keeps the grab offset (no jump); pressing the track moves the stone to the pointer.
    internal void BeginDrag(Point point)
    {
        if (!IsEnabled) return; Focus(); if (!CaptureMouse()) return;
        double y = YFor(index); grab = Math.Abs(point.Y - y) <= Stone / 2 ? point.Y - y : 0;
        MoveDragTo(point.Y);
    }
    void MoveDragTo(double pointerY) { dragY = Math.Clamp(pointerY - grab, Top, Bottom); index = IndexFor(dragY); InvalidateVisual(); }
    internal void MoveDrag(Point point) { if (IsMouseCaptured) MoveDragTo(point.Y); }
    internal void FinishDrag() { if (!IsMouseCaptured) return; finishingDrag = true; ReleaseMouseCapture(); finishingDrag = false; InvalidateVisual(); Commit(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); BeginDrag(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); MoveDrag(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); FinishDrag(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); if (!finishingDrag) SetSeconds(committed); InvalidateVisual(); }
    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;
        if (e.Key == Key.Escape) { if (IsDragging) Cancel(); e.Handled = true; return; }
        int delta = e.Key is Key.Up or Key.Right or Key.Add or Key.OemPlus or Key.PageUp ? 1 : e.Key is Key.Down or Key.Left or Key.Subtract or Key.OemMinus or Key.PageDown ? -1 : 0;
        if (delta == 0 && e.Key is not (Key.Home or Key.End)) return;
        Step(e.Key == Key.Home ? 0 : e.Key == Key.End ? values.Length - 1 : index + delta); e.Handled = true;
    }
    // Wheel works while hovering (no focus needed): up = more minutes.
    protected override void OnMouseWheel(MouseWheelEventArgs e) { base.OnMouseWheel(e); if (!IsEnabled || IsDragging) return; Step(index + Math.Sign(e.Delta)); e.Handled = true; }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); if (IsDragging) Cancel(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // Hit area: the slider column and the label, not the empty space around them.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, TrackX + HandleW / 2 + 8, ActualHeight)); // label + track + shard
        if (!IsEnabled) dc.PushOpacity(.5);
        double y = ThumbY, top = Top - 10, bottom = Bottom + 10;
        bool lively = IsEnabled && (IsMouseOver || IsDragging || IsKeyboardFocused);
        dc.DrawRoundedRectangle(lively ? TrackHover : TrackBrush, null, new Rect(TrackX - TrackWidth / 2, top, TrackWidth, bottom - top), TrackWidth / 2, TrackWidth / 2);
        if (bottom - y > 0) dc.DrawRoundedRectangle(RangeBrush, null, new Rect(TrackX - TrackWidth / 2, y, TrackWidth, bottom - y), TrackWidth / 2, TrackWidth / 2);
        var pill = new Rect(TrackX - HandleW / 2, y - HandleH / 2, HandleW, HandleH);
        dc.DrawRoundedRectangle(lively ? HandleHover : HandleFill, HandleEdge, pill, HandleH / 2, HandleH / 2);
        dc.DrawLine(HandleLight, new Point(pill.Left + HandleH / 2, pill.Top + 1), new Point(pill.Right - HandleH / 2, pill.Top + 1)); // bevel highlight
        dc.DrawLine(Accent, new Point(TrackX - 5, y + .5), new Point(TrackX + 5, y + .5));                                              // amber hairline
        if (IsKeyboardFocused) { var ring = pill; ring.Inflate(4, 4); dc.DrawRoundedRectangle(null, new Pen((Brush)FindResource("Accent"), 1) { DashStyle = DashStyles.Dot }, ring, ring.Height / 2, ring.Height / 2); }
        // Live value left of the handle, vertically centred on it, with a small "replay length" caption under it (both move
        // with the handle); plain text in the app font, no box.
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip, right = TrackX - HandleW / 2 - LabelGap;
        var label = new FormattedText(HistoryDial.Label(Seconds), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 15, valueBrush, dip);
        dc.DrawText(label, new Point(right - label.WidthIncludingTrailingWhitespace, y - label.Height / 2));
        var caption = new FormattedText("replay length", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, captionBrush, dip);
        dc.DrawText(caption, new Point(right - caption.WidthIncludingTrailingWhitespace, y + label.Height / 2 - 2));
        if (!IsEnabled) dc.Pop();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new HistoryPeer(this);
    sealed class HistoryPeer(HistorySliderControl owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => "Replay history slider";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 60; public double SmallChange => 60; public double Maximum => 3600; public double Minimum => owner.values[0]; public double Value => owner.Seconds;
        public void SetValue(double value) { if (IsReadOnly) throw new InvalidOperationException("Stop capture to change history."); if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value)); owner.index = Enumerable.Range(0, owner.values.Length).MinBy(i => Math.Abs(owner.values[i] - value)); owner.InvalidateVisual(); owner.Commit(); }
    }
}
