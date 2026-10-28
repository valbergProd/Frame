using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using F = System.Windows.Forms;
using D = System.Drawing;

namespace Frame.App;

public partial class MainWindow
{
    AudioEndpoint[] playbackEndpoints = [new("", "Windows default")];
    AudioEndpoint[] microphoneEndpoints = [new("", "Windows default")];
    static AudioEndpoint[] PreserveEndpoint(AudioEndpoint[] devices, string? saved) =>
        string.IsNullOrEmpty(saved) || devices.Any(d => d.Id == saved) ? devices : devices.Append(new AudioEndpoint(saved, "Saved device · refresh to check availability")).ToArray();
    // 0.6.0: the theme is the visual mode (ModeView.ApplyMode, title bar); the Light appearance checkbox and its preview are gone.
    // The manual-recording button is as wide as its widest label ("Stop …" is a few pixels wider than "Start …", plus the
    // 2 px active edge), so the Save button beside it never shifts when a recording starts or stops (0.5.5).
    void FitRecordButton()
    {
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip, widest = 0;
        foreach (var text in new[] { "Start manual recording", "Stop manual recording" })
            widest = Math.Max(widest, new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(RecordTitle.FontFamily, RecordTitle.FontStyle, RecordTitle.FontWeight, RecordTitle.FontStretch), RecordTitle.FontSize, Brushes.White, dip).WidthIncludingTrailingWhitespace);
        RecordButton.Width = Math.Max(RecordButton.MinWidth, Math.Ceiling(widest + RecordButton.Padding.Left + RecordButton.Padding.Right + 4));
    }
    // Capture settings while only Shadow Recording runs: restart it with the new settings through the same path as the
    // Capture page's quality choice (ShadowRestart.cs; the replay length is kept). If they cannot start, RestartShadowWith has
    // restored the previous settings and said what happened; the Apply fails, so the changes stay pending on the page.
    async Task ApplyCaptureWhileShadow(Settings next) { if (!await RestartShadowWith(next, "Settings applied.")) throw new InvalidOperationException(Message.Text); }
    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    static TextBox Field(string text) => new() { Text = text };
    static CheckBox Toggle(string text, bool value)
    {
        var control = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, FontSize = 13, Margin = new Thickness(0, 5, 0, 5) };
        TooltipPolish.Describe(control, text switch {
            "Include mouse cursor in video" => "Show the pointer in saved videos",
            "Show save notifications" => "Confirm saved clips in a corner of the screen",
            _ when text.StartsWith("Start FRAME with Windows") => "Launch hidden in the tray when you sign in",
            "Start shadow recording when FRAME starts" => "Begin buffering automatically at launch",
            _ when text.StartsWith("Combine") => "Save displays together in one video",
            _ => text
        }); return control;
    }
    static ComboBox Choice(string[] options, int index) { var c = new ComboBox { ItemsSource = options, SelectedIndex = Math.Max(0, index) }; return c; }
    // One settings row: label (with optional quiet hint) on the left, control on the right. Tooltips keep their 0.3.x wording.
    void Pair(Panel panel, string title, UIElement control, string hint = "", (string Heading, string Text)? info = null)
    {
        if (control is FrameworkElement element) TooltipPolish.Describe(element, control is Button { Tag: Hotkey } ? "Press a new shortcut to save immediately" : title switch {
            "FFmpeg" => "Locate the installed capture engine",
            "Video encoder" => "Choose how video is compressed",
            "Frame rate" => "Set captured frames per second",
            "Output resolution" => "Set the saved video height",
            "Video bitrate" => "Balance file size and video detail",
            "Desktop audio source" => "Choose the playback device to record",
            "Microphone source" => "Choose the microphone to record",
            "Replay duration" => "Set how much recent footage to retain",
            "Replay disk budget" => "Limit disk space for unpinned history",
            _ => title
        });
        var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); row.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 440 });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        var heading = new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (info is { } about) { var line = new StackPanel { Orientation = Orientation.Horizontal }; line.Children.Add(heading); line.Children.Add(InfoIcon(about.Heading, about.Text)); label.Children.Add(line); }
        else label.Children.Add(heading);
        if (hint.Length > 0) label.Children.Add(MutedText(hint, 11, new Thickness(0, 3, 0, 0)));
        row.Children.Add(label);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row);
    }
    // Settings text and lines use resource references (not FindResource snapshots), so a theme preview restyles the page
    // in place: no rebuild under the pointer, no mixture of light and dark brushes.
    static TextBlock MutedText(string text, double size, Thickness margin) => Themed(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin }, TextBlock.ForegroundProperty, "Muted");
    static Border Hairline(Thickness margin) => Themed(new Border { Height = 1, Opacity = .55, Margin = margin }, Border.BackgroundProperty, "Line");
    // Small "i" beside a settings title (0.5.5, Recovery): its explanation in FRAME's tooltip style on hover and on keyboard
    // focus (Tab), with a heading line. No panel, nothing else changes.
    FrameworkElement InfoIcon(string heading, string text)
    {
        var glyph = new Grid { Width = 14, Height = 14 };
        glyph.Children.Add(Themed(new System.Windows.Shapes.Ellipse { StrokeThickness = 1 }, System.Windows.Shapes.Shape.StrokeProperty, "Muted"));
        glyph.Children.Add(Themed(new TextBlock { Text = "i", FontSize = 9.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Muted"));
        var content = new StackPanel { MaxWidth = 290 };
        content.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var tip = new ToolTip { Content = content }; tip.SetResourceReference(StyleProperty, "RichToolTip");
        var host = new Border { Child = glyph, Background = Brushes.Transparent, Padding = new Thickness(3), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = true, Cursor = Cursors.Help, ToolTip = tip, Opacity = .85 };
        host.SetResourceReference(FocusVisualStyleProperty, "QuietFocus");
        ToolTipService.SetInitialShowDelay(host, 200);
        System.Windows.Automation.AutomationProperties.SetName(host, heading); System.Windows.Automation.AutomationProperties.SetHelpText(host, text);
        host.GotKeyboardFocus += (_, _) => { tip.PlacementTarget = host; tip.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; tip.IsOpen = true; };
        host.LostKeyboardFocus += (_, _) => tip.IsOpen = false;
        host.MouseEnter += (_, _) => host.Opacity = 1; host.MouseLeave += (_, _) => host.Opacity = .85;
        return host;
    }
    // Restrained section heading: light title, quiet description, hairline above (except the first).
    void Section(Panel panel, string title, string description)
    {
        if (panel.Children.Count > 0) panel.Children.Add(Hairline(new Thickness(0, 26, 0, 0)));
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.Light, Margin = new Thickness(0, panel.Children.Count > 0 ? 20 : 0, 0, 4) });
        panel.Children.Add(MutedText(description, 11, new Thickness(0, 0, 0, 8)));
    }
    // Collapsible section (Recording, General, Advanced settings): the Advanced QuietExpander look (circle chevron, 15 px header,
    // short caption via Tag), same hairline as Section. Controls are built once per BuildSettings and only hidden, so Apply
    // reads collapsed sections too. Open/closed state is remembered for this app session only (not saved to settings.json);
    // default collapsed, except Recording on first run (onboarding asks for capture preferences).
    HashSet<string>? openSettingsSections;
    StackPanel Collapsible(Panel panel, string title, string caption)
    {
        openSettingsSections ??= settings.Onboarded ? [] : ["Recording"];
        panel.Children.Add(Hairline(new Thickness(0, 26, 0, 0)));
        var section = Themed(new Expander { Header = title, Tag = caption, IsExpanded = openSettingsSections.Contains(title), Margin = new Thickness(0, 20, 0, 0), FontSize = 15 }, ForegroundProperty, "Text");
        section.SetResourceReference(StyleProperty, "QuietExpander");
        section.Expanded += (_, e) => { if (e.OriginalSource == section) openSettingsSections.Add(title); };
        section.Collapsed += (_, e) => { if (e.OriginalSource == section) openSettingsSections.Remove(title); };
        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; section.Content = body; panel.Children.Add(section); return body;
    }
    UIElement Inline(params UIElement[] items) { var p = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var i in items) p.Children.Add(i); return p; }
    readonly List<Button> hotkeyButtons = [];
    // Smart Apply: every Apply-only control is tracked with its value as built from the saved settings. Apply is shown
    // (accent) only while a tracked value differs; reverting a value hides it again. Unapplied values live in
    // settingsDraft, so rebuilding the page (navigation, probe, minute slider, hotkey or folder changes) keeps them and
    // their dirty state; collapsing a section only hides controls. Cleared after a successful Apply; kept on failure.
    // Hotkeys, the capture folder and "Minimize FRAME when a game starts" save immediately and are not tracked.
    readonly Dictionary<string, string> settingsDraft = [];
    readonly Dictionary<string, (Func<string> Read, string Saved)> applyFields = [];
    Button? applyButton;
    void Track(string key, Control control, Func<string> read, Action<string> restore)
    {
        string saved = read(); applyFields[key] = (read, saved);
        if (settingsDraft.TryGetValue(key, out var draft)) { if (draft == saved) settingsDraft.Remove(key); else restore(draft); }
        void Changed() { var value = read(); if (value == saved) settingsDraft.Remove(key); else settingsDraft[key] = value; UpdateApplyState(); }
        switch (control)
        {
            case ComboBox c: c.SelectionChanged += (_, _) => Changed(); break;
            case TextBox t: t.TextChanged += (_, _) => Changed(); break;
            case CheckBox t: t.Checked += (_, _) => Changed(); t.Unchecked += (_, _) => Changed(); break;
            case Slider v: v.ValueChanged += (_, _) => Changed(); break;
        }
    }
    void TrackToggle(string key, CheckBox box) => Track(key, box, () => box.IsChecked == true ? "1" : "0", v => box.IsChecked = v == "1");
    void TrackText(string key, TextBox box) => Track(key, box, () => box.Text.Trim(), v => box.Text = v);
    void TrackIndex(string key, ComboBox box) => Track(key, box, () => box.SelectedIndex.ToString(), v => box.SelectedIndex = int.Parse(v));
    // First run: Apply also completes onboarding (Onboarded = true), so it stays available there.
    bool SettingsPending => !settings.Onboarded || applyFields.Values.Any(f => f.Read() != f.Saved);
    // The Apply button lives in the fixed Settings heading (ApplySettingsButton, outside the scrolling page): completely
    // hidden without pending changes, shown with the accent face while Smart Apply reports changes, and only on Settings.
    void UpdateApplyState()
    {
        if (applyButton == null) return;
        bool pending = SettingsPending; applyButton.IsEnabled = pending;
        applyButton.Visibility = pending && SettingsPage.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (pending) { applyButton.SetResourceReference(BorderBrushProperty, "Accent"); var wash = new SolidColorBrush(((SolidColorBrush)FindResource("Accent")).Color) { Opacity = .22 }; wash.Freeze(); applyButton.Background = wash; }
        else { applyButton.ClearValue(BorderBrushProperty); applyButton.ClearValue(BackgroundProperty); }
    }
    Func<Task>? applySettings; // set by BuildSettings (reads that build's controls)
    async void ApplySettingsClick(object sender, RoutedEventArgs e) { if (applySettings != null) await applySettings(); }
    // Settings order (0.5.4): Hotkeys, Storage, Recording (audio sources directly below Output resolution), General, then a collapsed Advanced section.
    // Hotkeys and Storage stay open; Recording, General and Advanced are sibling collapsible sections (Collapsible).
    // Same controls, keys and Apply semantics as before; hotkeys still save immediately.
    void BuildSettings()
    {
        SettingsPage.Children.Clear(); hotkeyButtons.Clear(); applyFields.Clear(); applyButton = null;
        var page = SettingsPage;

        Section(page, "Hotkeys", "Click a shortcut and press the new key or combination. Esc cancels.");
        // Display order only: saving a shadow recording is the primary action, then starting/stopping it, then manual recording; the stored order of
        // settings.Hotkeys is unchanged (Apply writes them back in that order).
        foreach (var h in settings.Hotkeys.OrderBy(h => h.Action switch { "Replay" => 0, "Shadow" => 1, "Record" => 2, "Pause" => 3, _ => 4 }))
        {
            var button = new Button { Content = FormatHotkey(h), Tag = h, HorizontalContentAlignment = HorizontalAlignment.Left, FontFamily = new FontFamily("Segoe UI Semibold"), Padding = new Thickness(14, 8, 14, 8), MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
            string title = h.Action switch { "Replay" => "Save shadow recording", "Shadow" => "Start / stop shadow recording", "Record" => "Start / stop manual recording", "Pause" => "Pause / resume recording", "Monitors" => "Open capture source", "Open" => "Show FRAME window", _ => h.Action };
            System.Windows.Automation.AutomationProperties.SetName(button, title + " hotkey. Click, then press the new key.");
            var note = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            button.Click += (_, _) => BeginHotkeyEdit(button, note);
            var cell = new StackPanel(); cell.Children.Add(button); cell.Children.Add(note);
            Pair(page, title, cell); TooltipPolish.Describe(button, "Click, then press the new key or combination"); hotkeyButtons.Add(button);
        }

        // Start-up directly below Hotkeys (easy to find), with the game-minimize option as its last item. Same controls, values
        // and save behaviour as before: the two start-up options apply with Apply settings, game minimizing saves immediately.
        Section(page, "Start-up", "Run in the background and keep your history ready.");
        var startWindows = Toggle("Start FRAME with Windows (opens quietly in the tray)", settings.StartWithWindows);
        var autoShadow = Toggle("Start shadow recording when FRAME starts", settings.StartShadowOnLaunch);
        var startup = new StackPanel { Margin = new Thickness(0, 2, 0, 0) }; startup.Children.Add(startWindows); startup.Children.Add(autoShadow); page.Children.Add(startup);
        // Window behaviour only (never touches capture), so it saves immediately like hotkeys, also while recording.
        var gameHide = Toggle("Minimize FRAME when a game starts", settings.MinimizeOnGameStart);
        gameHide.ToolTip = "Hide FRAME to the tray once when a recognized game starts; recording and hotkeys keep running";
        gameHide.Click += (_, _) => {
            try { var next = CloneSettings(); next.MinimizeOnGameStart = gameHide.IsChecked == true; SettingsStore.Save(configPath, next); settings = next; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { gameHide.IsChecked = settings.MinimizeOnGameStart; Notify("Setting was not saved: " + e.Message); }
        };
        startup.Children.Add(gameHide); // last Start-up item: same margins and typography as the two above

        Section(page, "Storage", "Where captures and recovery data live.");
        var folderText = new TextBlock { Text = settings.OutputDirectory, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8), ToolTip = settings.OutputDirectory };
        var changeFolder = ActionButton("Choose capture folder", () => CaptureFolderClick(this, new RoutedEventArgs())); TooltipPolish.Describe(changeFolder, engine.Busy ? "Stop capture before changing the save folder" : "Choose where new captures are saved"); changeFolder.IsEnabled = !engine.Busy;
        var openFolder = ActionButton("View capture folder", ViewCaptureFolder); TooltipPolish.Describe(openFolder, "Open the capture folder in File Explorer (works while recording)");
        // Path on its own line; the two buttons side by side in a non-wrapping row (a WrapPanel moved "View" to a second row).
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal }; folderButtons.Children.Add(changeFolder); folderButtons.Children.Add(openFolder);
        var folderBox = new StackPanel(); folderBox.Children.Add(folderText); folderBox.Children.Add(folderButtons);
        Pair(page, "Capture folder", folderBox, "New recordings, replays and clips");
        var recovery = ActionButton("Recovery files", () => OpenFolder(Path.Combine(data, "Recovery"))); recovery.HorizontalAlignment = HorizontalAlignment.Left;
        Pair(page, "Recovery", recovery, "Segments kept after a failed session; may contain private content",
            ("What is Recovery?", "FRAME may keep temporary video segments if a recording crashes or stops unexpectedly.\nUse Recovery files to access these files if you need to recover an unfinished recording.\nRecovery is not guaranteed, and these files may contain private footage."));

        var rec = Collapsible(page, "Recording", "Quality, history and sound");
        rec.Children.Add(MutedText("Apply settings restarts Shadow Recording with new capture settings (the replay length is kept, earlier footage restarts); stop manual recording first.", 11, new Thickness(0, 0, 0, 8)));
        var fps = Choice(["30", "60", "120"], Array.IndexOf(new[] { 30, 60, 120 }, settings.Fps)); Pair(rec, "Frame rate", fps, "Actual output depends on hardware and load");
        var resolution = Choice(["Native", "720p", "1080p", "1440p", "2160p"], Array.IndexOf(new[] { 0, 720, 1080, 1440, 2160 }, settings.OutputHeight)); Pair(rec, "Output resolution", resolution);
        var outputs = PreserveEndpoint(playbackEndpoints, settings.OutputDevice); var inputs = PreserveEndpoint(microphoneEndpoints, settings.InputDevice);
        var outputDevice = Choice(outputs.Select(d => d.Name).ToArray(), Array.FindIndex(outputs, d => d.Id == (settings.OutputDevice ?? "")));
        var inputDevice = Choice(inputs.Select(d => d.Name).ToArray(), Array.FindIndex(inputs, d => d.Id == (settings.InputDevice ?? "")));
        Pair(rec, "Desktop audio source", outputDevice); Pair(rec, "Microphone source", inputDevice);
        var refresh = ActionButton("Refresh audio devices", async () => await Act(() => {
            if (engine.Busy) throw new InvalidOperationException("Stop capture before refreshing audio devices.");
            var selectedOutput = outputs[outputDevice.SelectedIndex].Id;
            var selectedInput = inputs[inputDevice.SelectedIndex].Id;
            playbackEndpoints = AudioDevices.List(true); microphoneEndpoints = AudioDevices.List(false);
            outputs = PreserveEndpoint(playbackEndpoints, selectedOutput); inputs = PreserveEndpoint(microphoneEndpoints, selectedInput);
            outputDevice.ItemsSource = outputs.Select(d => d.Name).ToArray(); outputDevice.SelectedIndex = Array.FindIndex(outputs, d => d.Id == selectedOutput);
            inputDevice.ItemsSource = inputs.Select(d => d.Name).ToArray(); inputDevice.SelectedIndex = Array.FindIndex(inputs, d => d.Id == selectedInput);
            Notify("Audio device list refreshed. Windows sound settings are unchanged."); return Task.CompletedTask;
        }));
        refresh.HorizontalAlignment = HorizontalAlignment.Left; Pair(rec, "Audio devices", refresh, "Listed only on request; Windows sound settings stay unchanged");
        var bitrate = Field(settings.BitrateMbps.ToString()); Pair(rec, "Video bitrate", bitrate, "2–200 Mbps");
        var replay = Field(settings.ReplaySeconds.ToString());
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (int seconds in new[] { 15, 30, 60, 120, 300, 600 }) { int s = seconds; var chip = ActionButton(s < 60 ? s + "s" : s / 60 + "m", () => replay.Text = s.ToString()); chip.Padding = new Thickness(10, 5, 10, 5); chip.MinHeight = 28; chip.FontSize = 12; chip.Margin = new Thickness(0, 0, 6, 0); presets.Children.Add(chip); }
        var replayBox = new StackPanel(); replayBox.Children.Add(replay); replayBox.Children.Add(presets); Pair(rec, "Replay duration", replayBox, "5–3600 seconds of shadow history");
        TooltipPolish.Describe(replay, "Set how much recent footage to retain");
        var budget = Field(settings.ReplayBudgetMb.ToString()); Pair(rec, "Replay disk budget", budget, "64–32768 MB; duration may be shortened");
        var cursor = Toggle("Include mouse cursor in video", settings.Cursor);
        var combined = Toggle("Combine selected displays into their desktop arrangement (more processing)", settings.Combined);
        var captureOptions = new StackPanel(); captureOptions.Children.Add(cursor); captureOptions.Children.Add(combined); Pair(rec, "Capture", captureOptions);

        // General (0.5.5): FRAME makes no sounds, so the cue toggle and notification volume are gone; "Reduce visual motion" is
        // gone too (decorative motion stops whenever FRAME is not visible and follows the Windows animation setting).
        // 0.6.0: "Light appearance" is replaced by the visual modes (title bar, applied at once) and the floating manual-recording
        // controls ("On-screen controls") were removed; manual recording stays on the Capture page, its hotkeys and the tray.
        var general = Collapsible(page, "General", "Notifications and updates");
        var toasts = Toggle("Show save notifications", settings.ShowSaveNotifications); Pair(general, "Notifications", toasts, "Small top-right notes when a clip is saved or Shadow Recording starts; stop warnings always show. FRAME plays no sounds");
        BuildUpdates(general);
        var automaticUpdates = Toggle("Check for updates when FRAME starts", settings.CheckForUpdatesOnStartup);
        Pair(general, "Update checks", automaticUpdates, "Checks once at launch. Download and installation always require your choice");

        // Advanced: collapsed by default; FFmpeg location, encoder choice and diagnostics. Its former whole-section tooltip is now the caption.
        var adv = Collapsible(page, "Advanced settings", "Capture engine, encoder and diagnostics");
        var ffmpeg = Field(settings.FfmpegPath);
        string engineStatus; try { var found = Backend.Resolve(settings.FfmpegPath); engineStatus = (ready ? "Detected and usable · " : "Found, not yet verified · ") + found; } catch (FileNotFoundException) { engineStatus = "Not found · choose ffmpeg.exe (8.1 or newer)"; }
        var chooseFfmpeg = ActionButton("Choose ffmpeg.exe", () => { var d = new Microsoft.Win32.OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe" }; if (d.ShowDialog() == true) ffmpeg.Text = d.FileName; });
        var ffmpegBox = new StackPanel(); ffmpegBox.Children.Add(ffmpeg); var ffmpegRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; ffmpegRow.Children.Add(chooseFfmpeg); ffmpegBox.Children.Add(ffmpegRow);
        ffmpegBox.Children.Add(Themed(new TextBlock { Text = engineStatus, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }, TextBlock.ForegroundProperty, ready ? "Muted" : "Accent"));
        Pair(adv, "FFmpeg", ffmpegBox, "Leave blank to detect automatically");
        var choices = new[] { "Automatic · prefer H.264 hardware" }.Concat(encoders.Select(e => e.Name)).ToArray();
        var encoder = Choice(choices, encoders.FindIndex(e => e.Id == settings.Encoder) + 1); Pair(adv, "Video encoder", encoder, "Only encoders that passed a probe appear");
        // Diagnostics: the local log folder and a user-started, redacted ZIP for bug reports (DiagnosticsExport). No upload.
        var diagnosticsStatus = MutedText("", 11, new Thickness(0, 6, 0, 0)); diagnosticsStatus.Visibility = Visibility.Collapsed;
        var openLogs = ActionButton("Open log folder", OpenLogFolder); TooltipPolish.Describe(openLogs, "FRAME's diagnostic logs on this PC");
        var exportDiagnostics = ActionButton("Export diagnostics", async () => { diagnosticsStatus.Visibility = Visibility.Visible; diagnosticsStatus.Text = "Preparing…"; if (await ExportDiagnosticsInteractive(diagnosticsStatus) == null && diagnosticsStatus.Text == "Preparing…") diagnosticsStatus.Visibility = Visibility.Collapsed; });
        TooltipPolish.Describe(exportDiagnostics, "Save a ZIP with logs and system details for a bug report; nothing is uploaded");
        var diagnosticsButtons = new StackPanel { Orientation = Orientation.Horizontal }; diagnosticsButtons.Children.Add(openLogs); diagnosticsButtons.Children.Add(exportDiagnostics);
        var diagnosticsBox = new StackPanel(); diagnosticsBox.Children.Add(diagnosticsButtons); diagnosticsBox.Children.Add(diagnosticsStatus);
        Pair(adv, "Diagnostics", diagnosticsBox, "Logs stay on this PC. Export creates a ZIP you can attach to a bug report");
        adv.Children.Add(MutedText("Recovery segments are retained after a failure and removed after a successful session. The replay budget excludes segments pinned by recording or export. Export re-encodes with the selected encoder to trim paused intervals. HDR and tightly synchronized multi-display/audio workflows require further hardware validation.", 11, new Thickness(0, 10, 0, 0)));

        TrackIndex("Fps", fps); TrackIndex("OutputHeight", resolution); TrackText("BitrateMbps", bitrate); TrackText("ReplaySeconds", replay); TrackText("ReplayBudgetMb", budget);
        Track("OutputDevice", outputDevice, () => outputDevice.SelectedIndex >= 0 ? outputs[outputDevice.SelectedIndex].Id : "", v => { int i = Array.FindIndex(outputs, d => d.Id == v); if (i >= 0) outputDevice.SelectedIndex = i; });
        Track("InputDevice", inputDevice, () => inputDevice.SelectedIndex >= 0 ? inputs[inputDevice.SelectedIndex].Id : "", v => { int i = Array.FindIndex(inputs, d => d.Id == v); if (i >= 0) inputDevice.SelectedIndex = i; });
        TrackToggle("Cursor", cursor); TrackToggle("Combined", combined);
        TrackToggle("StartWithWindows", startWindows); TrackToggle("StartShadowOnLaunch", autoShadow); TrackToggle("ShowSaveNotifications", toasts);
        TrackText("FfmpegPath", ffmpeg);
        TrackToggle("CheckForUpdatesOnStartup", automaticUpdates);
        Track("Encoder", encoder, () => encoder.SelectedIndex <= 0 ? "auto" : encoders[encoder.SelectedIndex - 1].Id, v => encoder.SelectedIndex = encoders.FindIndex(e => e.Id == v) + 1);

        // Apply (0.5.5): stays on Settings at the same scroll position (sections keep their open state); before, it jumped to
        // Capture. Appearance, notifications, start-up and the replay length also apply while recording (before, every Apply was
        // refused while capture ran, so a theme change "did not stick"); capture settings restart Shadow Recording with the
        // new values (replay length kept) and still wait for a manual recording to end. On failure the draft stays pending.
        applySettings = async () => await Act(async () => {
            var next = CloneSettings(); next.FfmpegPath = ffmpeg.Text.Trim(); next.Encoder = encoder.SelectedIndex == 0 ? "auto" : encoders[encoder.SelectedIndex - 1].Id;
            next.CheckForUpdatesOnStartup = automaticUpdates.IsChecked == true;
            next.Fps = new[] { 30, 60, 120 }[fps.SelectedIndex]; next.OutputHeight = new[] { 0, 720, 1080, 1440, 2160 }[resolution.SelectedIndex];
            next.BitrateMbps = int.Parse(bitrate.Text); next.ReplaySeconds = int.Parse(replay.Text); next.ReplayBudgetMb = int.Parse(budget.Text);
            next.OutputDevice = outputs[outputDevice.SelectedIndex].Id; next.InputDevice = inputs[inputDevice.SelectedIndex].Id;
            next.Combined = combined.IsChecked == true; next.Cursor = cursor.IsChecked == true; next.ShowSaveNotifications = toasts.IsChecked == true; next.StartWithWindows = startWindows.IsChecked == true; next.StartShadowOnLaunch = autoShadow.IsChecked == true;
            // Stored hotkey order kept (rows are shown in priority order).
            next.Hotkeys = settings.Hotkeys.Select(h => (Hotkey)hotkeyButtons.First(b => ((Hotkey)b.Tag).Action == h.Action).Tag).ToArray(); next.Onboarded = true; next.Validate();
            bool captureChanged = CaptureSettings.Differ(settings, next), ffmpegChanged = next.FfmpegPath != settings.FfmpegPath;
            if (captureChanged && engine.State is RecordingState.Recording or RecordingState.Paused or RecordingState.Starting or RecordingState.Finalizing)
                throw new InvalidOperationException("Stop manual recording first: these settings change how video is captured. Nothing was applied yet.");
            double offset = PageScroll.VerticalOffset; bool restarted = captureChanged && engine.Busy;
            if (restarted) await ApplyCaptureWhileShadow(next); // saves and restarts Shadow Recording (its status text stays)
            else { SettingsStore.Save(configPath, next); settings = next; if (engine.Busy) engine.SetReplaySeconds(next.ReplaySeconds); }
            settingsDraft.Clear(); BuildSettings(); // applied: the rebuilt page starts clean (Apply hidden); the visual mode is not an Apply setting
            SyncPreset(); SyncDuration(); RegisterKeys(); if (!args.Contains("--data")) SyncStartup(); if (ffmpegChanged || !ready) await Probe();
            UpdateLayout(); PageScroll.ScrollToVerticalOffset(offset); UpdateState();
            if (!restarted && !ffmpegChanged) Notify("Settings applied.");
        });
        applyButton = ApplySettingsButton; UpdateApplyState();
        page.Children.Add(MutedText("Hotkeys, the capture folder and game minimizing save immediately. Other changes show Apply settings next to the heading.", 11, new Thickness(0, 24, 0, 20)));
    }
    // FormatHotkey (the single label entry point) is in HotkeyNames.cs: layout-aware key names (0.5.5).
    bool hotkeyCaptureActive;
    // Inline hotkey editing (0.5.4): click the field, it shows "Change key…", press the key or combination, done.
    // No dialog, no global keyboard hook: the keys arrive as normal window input while FRAME has focus. While editing,
    // FRAME's own global hotkeys are unregistered, so pressing an assigned combination cannot trigger its action.
    // Esc, clicking elsewhere or leaving the window cancels; an invalid or unavailable key keeps the previous one.
    Button? editingHotkey; TextBlock? editingNote; bool hotkeyInputHooked;
    void BeginHotkeyEdit(Button button, TextBlock note)
    {
        if (editingHotkey == button) return;
        if (editingHotkey != null) EndHotkeyEdit();
        if (!hotkeyInputHooked)
        {
            hotkeyInputHooked = true;
            PreviewKeyDown += HotkeyEditKeyDown; PreviewKeyUp += HotkeyEditKeyUp;
            Deactivated += (_, _) => { if (editingHotkey != null) EndHotkeyEdit(); };
        }
        editingHotkey = button; editingNote = note; note.Visibility = Visibility.Collapsed;
        button.Content = "Change key…"; button.SetResourceReference(BorderBrushProperty, "Accent");
        hotkeyCaptureActive = true;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(handle, id);
        hotkeys.Clear();
        button.LostKeyboardFocus += HotkeyFieldLostFocus;
        button.Focus();
    }
    void HotkeyFieldLostFocus(object sender, KeyboardFocusChangedEventArgs e) { if (editingHotkey == sender) EndHotkeyEdit(); }
    // Restores the field and the registered hotkeys (from saved settings). Returns the field that was being edited.
    Button? EndHotkeyEdit()
    {
        var button = editingHotkey; if (button == null) return null;
        editingHotkey = null; button.LostKeyboardFocus -= HotkeyFieldLostFocus;
        button.Content = FormatHotkey((Hotkey)button.Tag); button.ClearValue(BorderBrushProperty);
        hotkeyCaptureActive = false; RegisterKeys();
        return button;
    }
    static uint ModifierMask(ModifierKeys m) => ((m & ModifierKeys.Alt) != 0 ? 1u : 0) | ((m & ModifierKeys.Control) != 0 ? 2u : 0) | ((m & ModifierKeys.Shift) != 0 ? 4u : 0) | ((m & ModifierKeys.Windows) != 0 ? 8u : 0);
    static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    void HotkeyEditKeyUp(object sender, KeyEventArgs e)
    {
        if (editingHotkey == null) return;
        e.Handled = true;
        uint mods = ModifierMask(Keyboard.Modifiers);
        editingHotkey.Content = mods == 0 ? "Change key…" : FormatModifiers(mods) + "…";
    }
    void HotkeyEditKeyDown(object sender, KeyEventArgs e)
    {
        if (editingHotkey == null) return;
        e.Handled = true; // the key belongs to the editor (no menu, focus or button activation)
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (e.IsRepeat) return;
        if (key == Key.Escape) { EndHotkeyEdit(); return; }
        uint mods = ModifierMask(Keyboard.Modifiers);
        if (IsModifier(key)) { editingHotkey.Content = FormatModifiers(mods) + "…"; return; }
        var button = editingHotkey; var note = editingNote!;
        var original = (Hotkey)button.Tag;
        Hotkey chosen;
        try { chosen = HotkeyChoice.Create(original.Action, mods, (uint)KeyInterop.VirtualKeyFromKey(key), hotkeyButtons.Where(b => b != button).Select(b => (Hotkey)b.Tag)); }
        catch (ArgumentException error) { note.Text = error.Message; note.Visibility = Visibility.Visible; button.Content = "Change key…"; return; } // keep listening
        EndHotkeyEdit(); // previous hotkeys active again before trying the new one
        try
        {
            SaveHotkey(chosen); // registers the new key first; on failure the previous one stays registered and saved
            button.Tag = chosen; button.Content = FormatHotkey(chosen); note.Visibility = Visibility.Collapsed; UpdateState();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            note.Text = error is InvalidOperationException ? FormatHotkey(chosen) + " is in use by Windows or another app. " + FormatHotkey(original) + " is kept." : "Not saved: " + error.Message;
            DiagnosticLog.Warning("hotkeys", $"{original.Action} hotkey not changed to {FormatHotkey(chosen)}", error);
            note.Visibility = Visibility.Visible;
        }
    }
    static string FormatModifiers(uint m) => string.Concat((m & 2) != 0 ? "Ctrl+" : "", (m & 4) != 0 ? "Shift+" : "", (m & 1) != 0 ? "Alt+" : "", (m & 8) != 0 ? "Win+" : "");
    void SaveHotkey(Hotkey chosen)
    {
        var next = CloneSettings(); next.Hotkeys = next.Hotkeys.Select(h => h.Action == chosen.Action ? chosen : h).ToArray(); next.Validate();
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var previous = settings.Hotkeys.First(h => h.Action == chosen.Action);
        var previousIds = hotkeys.Where(p => p.Value == chosen.Action).Select(p => p.Key).ToArray();
        if (chosen == previous && previousIds.Length > 0) { SettingsStore.Save(configPath, next); settings = next; Notify("Hotkey saved and active: " + FormatHotkey(chosen)); return; }
        int id = 1000; while (hotkeys.ContainsKey(id)) id++;
        if (!Native.RegisterHotKey(handle, id, chosen.Modifiers | 0x4000, chosen.Key))
            throw new InvalidOperationException("Hotkey was not changed. Windows could not activate " + FormatHotkey(chosen) + " (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + "). It may be reserved by another application. Try another combination.");
        try { SettingsStore.Save(configPath, next); }
        catch { Native.UnregisterHotKey(handle, id); throw; }
        foreach (int old in previousIds) { Native.UnregisterHotKey(handle, old); hotkeys.Remove(old); }
        hotkeys[id] = chosen.Action; settings = next;
        Notify("Hotkey saved and active: " + FormatHotkey(chosen));
    }
    Hotkey? CaptureHotkey(Hotkey original, Hotkey[] others, Action<Window, Action<Key, ModifierKeys>>? testDriver = null)
    {
        Hotkey? result = null;
        var dialog = new Window { Title = "FRAME · Set hotkey", Owner = this, Width = 490, Height = 280, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var body = new StackPanel { Margin = new Thickness(26) };
        body.Children.Add(new TextBlock { Text = "Press your new hotkey…", FontSize = 23, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Hold Ctrl, Alt, Shift or Win and press a key.\nYou can also use a function key (F1–F24).", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = (Brush)FindResource("Muted") });
        var feedback = new TextBlock { Text = "Current: " + FormatHotkey(original), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 18), Foreground = (Brush)FindResource("Accent") }; body.Children.Add(feedback);
        body.Children.Add(ActionButton("Cancel · Esc", () => dialog.Close())); dialog.Content = body;
        void AcceptKey(Key key, ModifierKeys modifiers)
        {
            if (key == Key.Escape) { dialog.Close(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) { feedback.Text = "Keep holding the modifier and press another key…"; return; }
            uint mask = ((modifiers & ModifierKeys.Alt) != 0 ? 1u : 0) | ((modifiers & ModifierKeys.Control) != 0 ? 2u : 0) | ((modifiers & ModifierKeys.Shift) != 0 ? 4u : 0) | ((modifiers & ModifierKeys.Windows) != 0 ? 8u : 0);
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            try { result = HotkeyChoice.Create(original.Action, mask, vk, others); dialog.Close(); }
            catch (ArgumentException error) { feedback.Text = error.Message; }
        }
        dialog.PreviewKeyDown += (_, e) => {
            e.Handled = true;
            if (!e.IsRepeat) AcceptKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        };
        if (testDriver != null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -20000;
            dialog.ContentRendered += (_, _) => dialog.Dispatcher.BeginInvoke(() => testDriver(dialog, AcceptKey));
        }
        // Losing activation is also part of normal Close(). Do not close recursively
        // from Deactivated; Escape, Cancel and the window close button cancel instead.
        hotkeyCaptureActive = true;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(handle, id);
        hotkeys.Clear();
        try { dialog.ShowDialog(); }
        finally { hotkeyCaptureActive = false; if (testDriver == null) RegisterKeys(); }
        return result;
    }
    void ValidateHotkeyDialogs(string report)
    {
        var checks = new List<string>(); var original = new Hotkey("Replay", 6, 83);
        var chosen = CaptureHotkey(original, [], (window, accept) => { accept(Key.LeftAlt, ModifierKeys.Alt); accept(Key.Z, ModifierKeys.Alt); });
        if (chosen != new Hotkey("Replay", 1, 90)) throw new Exception("Alt+Z selection failed.");
        checks.Add("Alt+Z closes the actual modal WPF dialog without recursive Close");
        chosen = CaptureHotkey(original, [], (_, accept) => accept(Key.Escape, ModifierKeys.None));
        if (chosen != null) throw new Exception("Escape should cancel."); checks.Add("Escape cancels without changing the binding");
        chosen = CaptureHotkey(original, [], (window, _) => window.Close());
        if (chosen != null) throw new Exception("Window close should cancel."); checks.Add("Window close cancels without recursion");
        chosen = CaptureHotkey(original, [new("Record", 1, 90)], (window, accept) => {
            accept(Key.Z, ModifierKeys.Alt);
            if (!window.IsVisible) throw new Exception("Duplicate unexpectedly closed the dialog.");
            accept(Key.Y, ModifierKeys.Control);
        });
        if (chosen != new Hotkey("Replay", 2, 89)) throw new Exception("Correction after duplicate failed.");
        checks.Add("Duplicate keeps the dialog open; corrected combination is accepted");
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { passed = checks.Count, checks, scope = "Real offscreen WPF dialogs; keys supplied to the same handler programmatically. No physical keyboard, capture or audio. Global registration not tested." }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
