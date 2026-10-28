using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Frame.App;

// Cinematic D Capture layout (0.6.0), after the "Alternative D" reference: the obsidian stone large at the left centre
// resting on dark ground, the Shadow Recording title and subtitle to its right, the horizontal replay length, quiet manual
// recording and "Save last …", the two shortcut rows with key chips, and a bottom panel with display, quality and audio.
// Real controls fed by CaptureView; every action goes through ICaptureCommands (same paths as the Original GUI).
// Geometry: Relayout() places everything from the window size only (interpolated between the minimum 930 x 690 and the
// default 1280 x 800 designs, growing a little beyond); text never sizes a slot, so title/label changes, notes, manual
// recording and saving move nothing. The notes are the last row of the right column (it scrolls if a large panel appears).
// Light: one CinematicLight drives the stone, the scene's ground/halo layers (CinematicScene, created here, registered by
// ModeView as the mode's scene) and the warm highlights on the title, eyebrow and slider. The layout is the light's only
// clock client: attached to CaptureAtmosphere while it is visible, detached otherwise; Present() shows the still state.
partial class CinematicCaptureLayout : ICaptureLayout, IDecorativeClient
{
    readonly ModeContext context;
    readonly ICaptureCommands commands;
    readonly CinematicLight light = new();
    readonly CinematicStone stone = new();
    readonly CinematicScene scene = new();
    readonly ContextMenu qualityMenu = new() { Placement = PlacementMode.Top, VerticalOffset = -10 };
    readonly MenuItem[] qualityItems = new MenuItem[4];
    // Warm light on the title (left side, toward the stone) and subtitle/eyebrow: stop colours set per clock step.
    readonly GradientStop titleWarm = new(Colors.White, 0), titleMid = new(Colors.White, .42), titleCool = new(Colors.White, 1);
    readonly GradientStop subWarm = new(Colors.White, 0), subCool = new(Colors.White, .7);
    readonly SolidColorBrush eyebrow = new(Color.FromRgb(0xC2, 0xB2, 0xA3));
    static readonly Color TextColor = Color.FromRgb(0xF5, 0xEE, 0xE6), SubColor = Color.FromRgb(0xC9, 0xBB, 0xAE), EyebrowColor = Color.FromRgb(0xB8, 0xA8, 0x99), Glow = Color.FromRgb(0xFF, 0xC0, 0x84);
    bool updating, attached, clocked;
    double lastPhase;
    CaptureView? last;
    public FrameworkElement Root => this;
    public Panel NotesHost => Notes;
    public Decorator DisplayHost => DisplaysHost;
    public ContentControl? HeroHost => null;
    public CinematicScene Scene => scene;
    public CinematicStone Stone => stone;
    public CinematicLight Light => light;
    public bool WantsFrames => IsVisible;
    public bool FullRate => false;

    internal CinematicCaptureLayout(ModeContext context)
    {
        this.context = context; commands = context.Commands;
        InitializeComponent();
        Stage.Children.Add(stone);
        stone.Click += async (_, _) => await commands.ToggleShadow();
        // The title is part of the same action (as the 0.5.5 label): click toggles, hover lifts the stone. The stone is the
        // keyboard stop and automation element, so the title is not focusable.
        TitleText.MouseLeftButtonUp += async (_, e) => { if (stone.IsEnabled) { e.Handled = true; await commands.ToggleShadow(); } };
        TitleText.MouseEnter += (_, _) => stone.Hint(true); TitleText.MouseLeave += (_, _) => stone.Hint(false);
        var title = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        title.GradientStops.Add(titleWarm); title.GradientStops.Add(titleMid); title.GradientStops.Add(titleCool); TitleText.Foreground = title;
        var sub = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        sub.GradientStops.Add(subWarm); sub.GradientStops.Add(subCool); Subtitle.Foreground = sub; Eyebrow.Foreground = eyebrow;

        Length.Committed += seconds => { if (last != null && seconds != last.ReplaySeconds) commands.SetReplaySeconds(seconds); };
        Length.Previewed += seconds => LengthValue.Text = Duration(seconds);
        ManualButton.Click += async (_, _) => await commands.ToggleManual();
        PauseButton.Click += async (_, _) => await commands.PauseResume();
        SaveButton.Click += async (_, _) => await commands.SaveReplay();
        ChangeShadowKey.Click += (_, _) => commands.ChangeHotkey("Shadow");
        ChangeSaveKey.Click += (_, _) => commands.ChangeHotkey("Replay");
        // A click on the button while the popup is open first closes it (StaysOpen=false); do not reopen it at once.
        long closedAt = 0; SourcePopup.Closed += (_, _) => closedAt = Environment.TickCount64;
        SourceButton.Click += (_, _) => { if (Environment.TickCount64 - closedAt > 250) SourcePopup.IsOpen = true; };
        for (int i = 0; i < qualityItems.Length; i++)
        {
            int index = i; var item = qualityItems[i] = new MenuItem { Header = CaptureView.PresetNames[i], IsEnabled = i < 3 };
            item.Click += (_, _) => { if (!updating && index <= 2) commands.SetPreset(index); };
            qualityMenu.Items.Add(item);
        }
        qualityMenu.PlacementTarget = QualityButton;
        QualityButton.Click += (_, _) => qualityMenu.IsOpen = true;
        RoutedEventHandler audio = (_, _) => { if (!updating) commands.SetAudio(DesktopCheck.IsChecked == true, MicCheck.IsChecked == true); };
        DesktopCheck.Click += audio; MicCheck.Click += audio;
        SizeChanged += (_, _) => Relayout(); Loaded += (_, _) => Relayout(); // the scene needs window coordinates (after load)
        IsVisibleChanged += (_, _) => Attach(IsVisible);
        ApplyLight();
        DiagnosticLog.Write("cinematic-d", $"Stone art {CinematicArt.Milliseconds:0} ms, ground {GroundArt.Milliseconds:0} ms (one-time)");
        // Diagnostics (--render ... --cinematic-states <json>): every recording state through Update; positions of the main
        // elements after each (window DIP), to show that state and text changes move nothing. The real view is restored.
        var probe = Array.IndexOf(Environment.GetCommandLineArgs(), "--cinematic-states");
        if (probe > 0)
        {
            string path = Environment.GetCommandLineArgs()[probe + 1];
            Loaded += (_, _) => Dispatcher.InvokeAsync(() => ProbeStates(path), System.Windows.Threading.DispatcherPriority.Background);
            SizeChanged += (_, _) => { if (IsLoaded && context.Window.Width <= context.Window.MinWidth) Dispatcher.InvokeAsync(() => ProbeStates(System.IO.Path.ChangeExtension(path, ".minimum.json")), System.Windows.Threading.DispatcherPriority.Background); }; // --minimum
        }
    }
    void ProbeStates(string report)
    {
        var real = last ?? new CaptureView(); int[] choices = [60, 120, 180, 300, 600, 900, 1800, 3600];
        var idle = real with { ShadowTitle = "Start shadow recording", ShadowActive = false, ShadowEnabled = true, ManualActive = false, ManualElapsed = "", SaveEnabled = false, ReplayChoices = choices, ReplaySeconds = 300, Duration = "5 minutes", SaveTitle = "Save last 5 minutes", ReplayLengthEnabled = true };
        var states = new (string Name, CaptureView View, string Loading)[] {
            ("idle", idle, ""), ("starting", idle with { ShadowTitle = "Starting…", ShadowEnabled = false, ShadowDot = "transition" }, "Preparing shadow recording..."),
            ("active", idle with { ShadowTitle = "Stop shadow recording", ShadowActive = true, SaveEnabled = true }, ""),
            ("saving", idle with { ShadowTitle = "Stop shadow recording", ShadowActive = true, SaveTitle = "Saving…" }, ""),
            ("length-15min", idle with { ShadowTitle = "Stop shadow recording", ShadowActive = true, ReplaySeconds = 900, Duration = "15 minutes", SaveTitle = "Save last 15 minutes", SaveHotkeyLabel = "Hotkey for saving the 15 minutes" }, ""),
            ("custom-45s", idle with { ReplayChoices = [45, .. choices], ReplaySeconds = 45, Duration = "45 seconds", SaveTitle = "Save last 45 seconds" }, ""),
            ("manual", idle with { ShadowTitle = "Stop shadow recording", ShadowActive = true, ManualActive = true, ManualTitle = "Stop manual recording", ManualElapsed = "00:01:23", PauseLabel = "Pause" }, ""),
            ("manual-paused", idle with { ShadowTitle = "Stop shadow recording", ShadowActive = true, ManualActive = true, ManualPaused = true, ManualTitle = "Stop manual recording", ManualElapsed = "00:01:23", PauseLabel = "Resume" }, ""),
            ("stopping", idle with { ShadowTitle = "Stopping recording…", ShadowActive = true, ShadowEnabled = false }, ""),
            ("quality-audio-source", idle with { PresetIndex = 2, DesktopAudio = true, Microphone = true, SourceTitle = "2 displays", SourceDetail = "Combined canvas" }, ""),
            ("engine-check", idle with { ShadowEnabled = false, Ready = false }, "Capture engine unavailable · check Settings") };
        FrameworkElement[] parts = [stone, TitleText, Subtitle, Length, ManualButton, ActionDivider, SaveButton, ManualDetail, ShadowChip, SaveChip, ChangeShadowKey, ChangeSaveKey, Notes, BottomPanel, SourceButton, QualityButton, DesktopCheck, MicCheck];
        string[] names = ["stone", "title", "subtitle", "slider", "manual", "divider", "save", "manualDetail", "shadowChip", "saveChip", "changeShadowKey", "changeSaveKey", "notes", "panel", "source", "quality", "desktopAudio", "microphone"];
        var positions = new Dictionary<string, Dictionary<string, string>>(); var baseline = new Point[parts.Length]; double worst = 0; string worstAt = "";
        foreach (var (name, view, loading) in states)
        {
            Update(view); ShowLoading(loading, loading.Length > 0); UpdateLayout();
            var row = new Dictionary<string, string>();
            for (int i = 0; i < parts.Length; i++)
            {
                var at = parts[i].TranslatePoint(new Point(), context.Window); row[names[i]] = $"{at.X:0.##},{at.Y:0.##}";
                if (name == "idle") baseline[i] = at;
                else { double shift = Math.Max(Math.Abs(at.X - baseline[i].X), Math.Abs(at.Y - baseline[i].Y)); if (shift > worst) { worst = shift; worstAt = name + "/" + names[i]; } }
            }
            positions[name] = row;
        }
        Update(real); ShowLoading("", false);
        System.IO.File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { window = $"{context.Window.ActualWidth}x{context.Window.ActualHeight}", maxShiftDip = Math.Round(worst, 3), worstAt, positions }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    // Clock client while visible (the host does not call Shown/Hidden on page changes; visibility covers pages, modes,
    // tray and minimize). Detached: the next Update snaps the light to the exact state.
    void Attach(bool on)
    {
        if (on == attached) return;
        attached = on;
        if (on) context.Atmosphere.Attach(this);
        else { context.Atmosphere.Detach(this); clocked = false; light.Snap(context.Atmosphere.ReducedMotion); ApplyLight(); }
    }
    public void Advance(double dt)
    {
        // --render-animation restarts the shared clock (Phase back to ~0): restart the light too, so clips are reproducible.
        if (context.Atmosphere.Phase < lastPhase) light.Restart();
        lastPhase = context.Atmosphere.Phase;
        clocked = true; light.Advance(dt); ApplyLight();
    }
    public void Present() { clocked = false; light.Snap(context.Atmosphere.ReducedMotion); ApplyLight(); }
    void ApplyLight()
    {
        stone.Apply(light); scene.Apply(light);
        double w = light.Warmth;
        titleWarm.Color = Mix(TextColor, Glow, .5 * w); titleMid.Color = Mix(TextColor, Glow, .14 * w); titleCool.Color = TextColor;
        subWarm.Color = Mix(SubColor, Glow, .3 * w); subCool.Color = SubColor;
        eyebrow.Color = Mix(EyebrowColor, Glow, .28 * w);
        Length.Warm(.25 + .75 * w);
    }
    static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    static double L(double a, double b, double t) => a + (b - a) * t;
    // Window-size geometry. s: 0 at the minimum window, 1 at the default 1280 x 800 (content area 1090 x 764); big: growth
    // beyond the default (to about 1920 x 1080).
    void Relayout()
    {
        double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        double sw = Math.Clamp((w - 740) / 350, 0, 1), sh = Math.Clamp((h - 654) / 110, 0, 1), s = Math.Min(sw, sh);
        double big = Math.Clamp(Math.Min((w - 1090) / 640, (h - 764) / 280), 0, 1), gapScale = 1 + .3 * big;
        double diameter = L(228, 320, s) + 70 * big, titleSize = L(34, 40, s) + 4 * big;
        TitleText.FontSize = titleSize; Subtitle.FontSize = L(14.5, 16.5, s);
        ShadowKeyLabel.FontSize = SaveKeyLabel.FontSize = L(12, 13, s);
        ManualTitle.FontSize = SaveTitle.FontSize = L(13, 14.5, s);
        Length.SetFonts(L(13, 15, s), L(16, 19, s)); Length.TrackY = L(15, 18, s);
        // Right column rows: title, subtitle, loading, gap, eyebrow, gap, slider, gap, actions, manual detail, gap, hotkeys, gap.
        double keyRow = L(34, 38, s);
        double[] rows = [Math.Round(titleSize * 1.36), L(22, 26, s), L(18, 20, s), L(6, 10, s) * gapScale, 18, L(8, 12, s) * gapScale, L(64, 72, s), L(20, 38, s) * gapScale, L(36, 40, s), L(18, 20, s), L(12, 25, s) * gapScale, 2 * keyRow, L(2, 4, s)];
        for (int i = 0; i < rows.Length; i++) Column.RowDefinitions[i].Height = new GridLength(Math.Round(rows[i]));
        Hotkeys.RowDefinitions[0].Height = Hotkeys.RowDefinitions[1].Height = new GridLength(Math.Round(keyRow));
        double block = rows.Sum(r => Math.Round(r)) + Notes.MinHeight;
        // Bottom panel and the column above it; taller windows keep the block nearer the middle.
        double panelHeight = L(84, 92, s), panelBottom = L(48, 54, sh) + 20 * big, panelTop = h - panelBottom - panelHeight;
        double columnTop = Math.Max(16, panelTop - L(6, 8, s) - block - Math.Max(0, h - 764) * .5);
        double stoneLeft = L(30, 70, sw), gap = L(38, 64, sw), right = L(28, 48, sw);
        double columnWidth = Math.Min(L(410, 540, s) + 80 * big, w - stoneLeft - diameter - gap - right);
        double shift = Math.Max(0, w - 1090) * .36; // wide windows: the group moves right, the stone stays left of centre
        double stoneX = stoneLeft + shift, columnX = stoneX + diameter + gap;
        double stoneY = columnTop + L(118, 140, s) * gapScale - diameter / 2;
        stone.SetDiameter(diameter); Canvas.SetLeft(stone, stoneX); Canvas.SetTop(stone, stoneY);
        // The viewport extends 14 DIP left and right of the column so the slider handle and its glow are never clipped.
        ColumnScroll.Margin = new Thickness(columnX - 14, columnTop, 0, 0); ColumnScroll.Width = columnWidth + 28; ColumnScroll.Height = Math.Max(0, panelTop - 4 - columnTop);
        Column.Width = columnWidth; Column.Margin = new Thickness(14, 0, 14, 0);
        // Manual column: fixed to the longest label, so Start/Stop never moves the divider or Save.
        var measure = new FormattedText("Start manual recording", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(ManualTitle.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), ManualTitle.FontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        ManualColumn.Width = new GridLength(Math.Ceiling(Math.Max(24 + measure.WidthIncludingTrailingWhitespace + L(20, 30, s), .55 * columnWidth)));
        SaveButton.Margin = new Thickness(L(20, 30, s), 0, 0, 0);
        double panelLeft = Math.Max(20, stoneX - 10), panelRight = Math.Min(w - 20, columnX + columnWidth + right);
        BottomPanel.Margin = new Thickness(panelLeft, panelTop, 0, 0); BottomPanel.Width = Math.Max(0, panelRight - panelLeft); BottomPanel.Height = panelHeight;
        double inset = L(16, 46, sw);
        foreach (var e in new FrameworkElement[] { SourceSection, QualityButton }) e.Margin = new Thickness(inset, 0, 8, 0);
        AudioSection.Margin = new Thickness(inset, 0, L(18, 40, sw), 0);
        SourceTitle.FontSize = QualityLabel.FontSize = L(14, 15, s); DesktopCheck.FontSize = MicCheck.FontSize = L(12, 13, s);
        MicCheck.Margin = new Thickness(L(12, 22, sw), 0, 0, 0); MicIcon.Margin = new Thickness(0, 0, L(10, 18, sw), 0);
        PlaceLight();
    }
    // Scene layers (window coordinates) follow the stone.
    void PlaceLight()
    {
        if (!IsLoaded || PresentationSource.FromVisual(this) == null) return;
        double d = stone.Diameter, box = stone.ArtBox; if (d <= 0) return;
        var centre = TranslatePoint(new Point(Canvas.GetLeft(stone) + d / 2, Canvas.GetTop(stone) + d / 2), context.Window);
        scene.Place(centre, d / 2, new Rect(centre.X - StoneArt.Centre.X * box, centre.Y - StoneArt.Centre.Y * box, box, box));
        scene.Apply(light);
    }

    static string Duration(int seconds) => seconds < 60 ? $"{seconds} seconds" : $"{seconds / 60d:0.##} " + (seconds == 60 ? "minute" : "minutes");
    public void Update(CaptureView v)
    {
        updating = true;
        try
        {
            last = v;
            TitleText.Text = v.ShadowTitle; stone.IsEnabled = v.ShadowEnabled; stone.ToolTip = v.ShadowTooltip.Length > 0 ? v.ShadowTooltip : null;
            TitleText.Cursor = v.ShadowEnabled ? System.Windows.Input.Cursors.Hand : null;
            if (light.Active != v.ShadowActive) { light.Active = v.ShadowActive; if (!clocked) { light.Snap(context.Atmosphere.ReducedMotion); ApplyLight(); } }

            Length.Show(v.ReplayChoices, v.ReplaySeconds); Length.IsEnabled = v.ReplayLengthEnabled; Length.ToolTip = v.ReplayLengthTooltip;
            if (!Length.IsDragging) LengthValue.Text = v.Duration;

            ManualTitle.Text = v.ManualTitle; ManualButton.IsEnabled = v.ManualEnabled; ManualButton.ToolTip = v.ManualTooltip.Length > 0 ? v.ManualTooltip : null;
            ManualPlay.Visibility = v.ManualActive ? Visibility.Collapsed : Visibility.Visible; ManualStop.Visibility = v.ManualActive ? Visibility.Visible : Visibility.Collapsed;
            ManualStop.Opacity = v.ManualPaused ? .5 : 1; ManualDot.Opacity = v.ManualPaused ? .35 : 1;
            ManualDetail.Visibility = v.ManualActive ? Visibility.Visible : Visibility.Hidden; // Hidden: the line keeps its place
            ManualTimer.Text = v.ManualElapsed; PauseButton.Content = v.PauseLabel;
            PauseButton.ToolTip = v.ManualPaused ? "Continue the current manual recording" : "Pause without ending this recording";

            SaveTitle.Text = v.SaveTitle; SaveButton.IsEnabled = v.SaveEnabled; SaveButton.ToolTip = v.SaveTooltip.Length > 0 ? v.SaveTooltip : null;

            Chip(ShadowChip, ShadowKey, v.ShadowHotkey); Chip(SaveChip, SaveKey, v.SaveHotkey);
            SaveKeyLabel.Text = v.SaveHotkeyLabel.Length > 0 ? v.SaveHotkeyLabel : "Hotkey for saving the " + v.Duration;

            SourceTitle.Text = v.SourceTitle.Length > 0 ? v.SourceTitle : "Capture source"; SourceDetail.Text = v.SourceDetail;
            SourceButton.ToolTip = v.SourceLocked ? "Stop capture before changing source." : "Choose which displays to capture";
            int preset = Math.Clamp(v.PresetIndex, 0, CaptureView.PresetNames.Length - 1);
            QualityDetail.Text = CaptureView.PresetNames[preset];
            QualityButton.IsEnabled = v.PresetEnabled; QualityButton.ToolTip = v.PresetTooltip.Length > 0 ? v.PresetTooltip : "Choose resolution and recording frame rate";
            for (int i = 0; i < qualityItems.Length; i++)
            {
                qualityItems[i].Header = (i == preset ? "✓  " : "     ") + CaptureView.PresetNames[i];
                qualityItems[i].FontWeight = i == preset ? FontWeights.SemiBold : FontWeights.Normal;
            }
            DesktopCheck.IsChecked = v.DesktopAudio; MicCheck.IsChecked = v.Microphone; DesktopCheck.IsEnabled = MicCheck.IsEnabled = v.AudioEnabled;
        }
        finally { updating = false; }
    }
    static void Chip(Border chip, TextBlock key, string label)
    {
        bool set = label.Length > 0 && label != "Not set";
        key.Text = set ? label : "Not set"; key.Foreground = set ? TextBrush : NotSetBrush;
        chip.Opacity = set ? 1 : .7;
    }
    static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xEE, 0xE6))), NotSetBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA9, 0x99, 0x8A)));
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    public void ShowLoading(string text, bool visible) => LoadingText.Text = visible ? text : "";
    public void Shown() => Attach(IsVisible);
    public void Hidden() { SourcePopup.IsOpen = false; qualityMenu.IsOpen = false; Attach(false); }
}
