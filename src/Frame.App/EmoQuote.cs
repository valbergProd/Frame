using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Frame.App;

// Emo mode quote (0.6.0): one prominent, minimal quote in quiet grey italic (Palatino Linotype, larger than UI text) in the
// Capture layout's HeroHost, above "SHADOW RECORDING". Space is reserved for the tallest of all EmoQuotes at the current
// width and size (FormattedText, re-measured only when the width or size changes), and the text sits on the bottom of that
// slot, so a new quote never moves anything below it and the gap to the controls stays constant. Size follows the window
// (EmoScene sets Size: 26 px at the default 1280 × 800, down to 20 px at FRAME's minimum height) and shrinks further only if
// the longest quote would not fit on one line, so the quote never pushes the Capture page into scrolling. A change fades out and in (0.3 + 0.45 s);
// the fade is a WPF animation that exists only during that transition and only while the quote is visible.
sealed class EmoQuoteView : FrameworkElement
{
    public const double MaxSize = 26, MinSize = 18, MaxQuoteWidth = 640;
    static readonly Typeface Face = new(new FontFamily("Palatino Linotype"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
    static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x92, 0x89, 0x8E)));
    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    FormattedText? text;
    double measuredWidth = -1, reserved, size = MaxSize, used = MaxSize;
    string? pending;
    public string Current { get; private set; } = "";
    public double Reserved => reserved;
    public double UsedSize => used;
    public double Size { get => size; set { value = Math.Clamp(value, MinSize, MaxSize); if (Math.Abs(value - size) < .1) return; size = value; measuredWidth = -1; InvalidateMeasure(); InvalidateVisual(); } }
    public EmoQuoteView()
    {
        Focusable = false; IsHitTestVisible = false; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        HorizontalAlignment = HorizontalAlignment.Left;
    }
    FormattedText Format(string quote, double width, double em) =>
        new(quote, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, em, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(40, width), TextAlignment = TextAlignment.Left };
    // Size that keeps every quote on one line at this width (never above Size), and the height of the tallest quote then.
    public (double Size, double Height) Fit(double width, double em)
    {
        double widest = EmoQuotes.All.Max(q => new FormattedText(q, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, em, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace);
        double fitted = Math.Max(MinSize, Math.Min(em, Math.Floor(em * (width - 2) / widest * 4) / 4));
        return (fitted, EmoQuotes.All.Max(q => Format(q, width, fitted).Height));
    }
    protected override Size MeasureOverride(Size available)
    {
        double w = Math.Min(double.IsInfinity(available.Width) ? MaxQuoteWidth : available.Width, MaxQuoteWidth);
        if (Math.Abs(w - measuredWidth) > .5) { measuredWidth = w; (used, reserved) = Fit(w, size); text = null; }
        return new Size(w, reserved);
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (Current.Length == 0 || ActualWidth <= 0) return;
        text ??= Format(Current, ActualWidth, used);
        dc.DrawText(text, new Point(0, Math.Max(0, ActualHeight - text.Height)));
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    sealed class Peer(EmoQuoteView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => owner.Current;
        protected override string GetClassNameCore() => "EmoQuote";
        protected override bool IsContentElementCore() => true;
    }
    void Set(string quote) { Current = quote; text = null; InvalidateVisual(); }
    // Shows a quote: with a short fade when visible and asked to, otherwise at once.
    public void Show(string quote, bool fade)
    {
        if (!fade || !IsVisible || Current.Length == 0) { Snap(); Set(quote); return; }
        pending = quote;
        var away = new DoubleAnimation(0, TimeSpan.FromSeconds(.3)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } };
        away.Completed += (_, _) =>
        {
            if (pending == null) return;
            Set(pending); pending = null;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.45)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
        };
        BeginAnimation(OpacityProperty, away);
    }
    // Ends any transition immediately (hidden, Leave): final text, full opacity, no animation clock left running.
    public void Snap()
    {
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        if (pending != null) { Set(pending); pending = null; }
    }
}

// Quote rotation. Enter() shows a different quote at every entry into Emo mode (EmoQuotes.Next shuffle bag, persisted at once
// through ModeContext.Update: no immediate repetition, also across restarts). Afterwards the next quote follows every
// EmoQuotes.Interval (10 min; diagnostics: --emo-quote-seconds <n>) of time on screen. One one-shot DispatcherTimer holds the
// remaining time; it is stopped whenever FRAME is not on screen (hidden, tray, minimized, cloaked, locked: CaptureAtmosphere
// .OnScreenChanged) and on Leave, keeping what is left. On return it continues from there, but never sooner than MinResume
// (so reopening never shows an immediate change), and a resume fires at most once (each tick re-arms a full interval).
sealed class EmoQuoteRotation
{
    readonly ModeContext context;
    readonly EmoQuoteView view;
    readonly Random random;
    readonly DispatcherTimer timer = new(DispatcherPriority.Normal);
    readonly TimeSpan interval, minResume;
    TimeSpan remaining;
    long armedAt;
    bool active, running;
    bool? simulatedOnScreen; // diagnostics only
    public int Index { get; private set; } = -1;
    public event Action<int, string>? Shown; // diagnostics: index and reason ("enter", "interval")
    public TimeSpan Interval => interval;
    public bool Running => running;
    public EmoQuoteRotation(ModeContext context, EmoQuoteView view, Random random)
    {
        this.context = context; this.view = view; this.random = random;
        var args = Environment.GetCommandLineArgs();
        interval = Array.IndexOf(args, "--emo-quote-seconds") is int i and >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds) : EmoQuotes.Interval;
        minResume = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(30).Ticks, interval.Ticks / 4));
        timer.Tick += (_, _) => Tick();
    }
    bool OnScreen => simulatedOnScreen ?? context.Atmosphere.IsOnScreen;
    public void Enter()
    {
        if (active) return;
        active = true; remaining = interval;
        Advance("enter", fade: OnScreen);
        context.Atmosphere.OnScreenChanged += Sync; Sync();
    }
    public void Leave()
    {
        if (!active) return;
        active = false; context.Atmosphere.OnScreenChanged -= Sync; Pause(); view.Snap();
    }
    void Sync() { if (active && OnScreen) Resume(); else Pause(); }
    void Resume()
    {
        if (running) return;
        if (remaining < minResume) remaining = minResume;
        running = true; armedAt = Stopwatch.GetTimestamp(); timer.Interval = remaining; timer.Start();
    }
    void Pause()
    {
        if (!running) return;
        running = false; timer.Stop();
        remaining -= Stopwatch.GetElapsedTime(armedAt); if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        view.Snap();
    }
    void Tick()
    {
        timer.Stop(); running = false;
        if (!active) return;
        Advance("interval", fade: true); remaining = interval; Sync();
    }
    void Advance(string reason, bool fade)
    {
        var s = context.Settings;
        var (index, bag) = EmoQuotes.Next(s.EmoQuoteLast, s.EmoQuoteBag, random);
        context.Update(x => { x.EmoQuoteLast = index; x.EmoQuoteBag = bag; });
        Index = index; view.Show(EmoQuotes.All[index], fade);
        Shown?.Invoke(index, reason);
    }
    // Diagnostics: pretend FRAME left/returned to the screen (null = follow the real state).
    internal void SimulateOnScreen(bool? value) { simulatedOnScreen = value; Sync(); }
    internal void AdvanceNow() { if (active) Advance("diagnostics", fade: true); }
    internal TimeSpan Remaining => running ? remaining - Stopwatch.GetElapsedTime(armedAt) : remaining;
}
