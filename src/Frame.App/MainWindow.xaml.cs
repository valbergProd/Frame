using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using F = System.Windows.Forms;
using D = System.Drawing;

namespace Frame.App;

public partial class MainWindow : Window
{
    readonly string[] args;
    readonly string data, configPath;
    readonly CaptureCoordinator engine;
    readonly ReplayGameTracker gameTracker = new();
    readonly CaptureAtmosphere atmosphere;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly HotkeyGate hotkeyGate = new();
    readonly Dictionary<int, string> hotkeys = [];
    readonly SemaphoreSlim actions = new(1);
    Settings settings;
    List<Encoder> encoders = [];
    // testSource: diagnostics harnesses only (--capture-test, --audio-test, --game-test, …) capture their own synthetic test
    // window (or one display) through Backend's window path. Never set from the UI: window/region modes were removed (0.5.5).
    CaptureTarget[] displays = [], testSource = [];
    readonly HashSet<string> selected = [];
    F.NotifyIcon? tray;
    HwndSource? source;
    bool loaded, quitting, ready, shadowTransition, savingReplay;
    bool checkingEngine = true;
    const string EngineLoadingLabel = "Preparing capture…";
    int loadingCharacters;
    readonly DispatcherTimer loadingTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(55) };
    int libraryLimit = 50, libraryVersion;
    double previousCpu;
    readonly Stopwatch clock = Stopwatch.StartNew();
    double previousTime;
    string hotkeyIssue = "";
    public MainWindow(string[] args)
    {
        this.args = args; InitializeComponent();
        loadingTimer.Tick += (_, _) => {
            loadingCharacters = (loadingCharacters + 1) % ((TypedLabel()?.Length ?? 0) + 13); // ~0.7 s pause, then repeat
            UpdateLoadingText();
        };
        IsVisibleChanged += (_, _) => UpdateLoadingText();
        Closed += (_, _) => loadingTimer.Stop();
        DesertBackdrop.Background = DesertAppearance.Background(); atmosphere = new(this, SandDrift, DuneFlow, ShadowButton, RecordButton);
        if (!args.Contains("--no-waves")) atmosphere.Waves = new DesertWaves(DesertBackdrop, ((ImageBrush)DesertBackdrop.Background).ImageSource); // --no-waves: diagnostics (cost comparison)
        atmosphere.VisibilityUpdated += UpdateLoadingText;
        HistorySlider.Committed += CommitHistory;
        Closed += (_, _) => gameTracker.Dispose();
        if (args.Contains("--render") || args.Contains("--idle-benchmark") || args.Contains("--hotkey-ui-test") || args.Contains("--capture-test") || args.Contains("--audio-test") || args.Contains("--game-test") || args.Contains("--live-length-test") || args.Contains("--capture-ui-test") || args.Contains("--library-test") || args.Contains("--mode-switch-test")) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; ShowInTaskbar = false; }
        if (args.Contains("--resource-test")) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; ShowActivated = false; } // placed behind other windows by the test; never takes focus
        int di = Array.IndexOf(args, "--data");
        Backend.OnlyConfiguredPath = di >= 0 && args.Contains("--ffmpeg-only-settings"); // diagnostic runs: simulate "FFmpeg not installed"
        data = di >= 0 ? args[di + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FRAME");
        Directory.CreateDirectory(data); configPath = Path.Combine(data, "settings.json");
        DiagnosticLog.Folder ??= data; DiagnosticLog.Startup(CurrentRelease.ToString());
        // An unreadable or invalid settings file is kept as settings.json.unreadable-<time> (it would otherwise be replaced by
        // the defaults at the next save) and logged; FRAME starts with default settings.
        try { settings = SettingsStore.Load(configPath); }
        catch (Exception e)
        {
            settings = new(); string kept = "";
            try { if (File.Exists(configPath)) { kept = configPath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); File.Copy(configPath, kept, false); } } catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { kept = ""; }
            Message.Text = "Settings could not be read: " + e.Message + (kept.Length > 0 ? " A copy was kept; defaults are used." : "");
            DiagnosticLog.Error("settings", "Settings could not be read; defaults are used" + (kept.Length > 0 ? ", the file was kept as " + Path.GetFileName(kept) : ""), e);
        }
        engine = new(Path.Combine(data, "Recovery")) { ObserveReplayGame = gameTracker.Observe }; engine.FaultRaised += f => Dispatcher.InvokeAsync(() => OnFault(f)); LoadRecordingStatus(); engine.Notice += s => Dispatcher.InvokeAsync(() => Notify(s)); engine.Changed += () => Dispatcher.InvokeAsync(UpdateState); SetupAudioStatus();
        SourceInitialized += (_, _) => { Native.Decorate(this); source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(WndProc); };
        Loaded += OnLoaded; Closing += OnClosing;
        SizeChanged += (_, _) => HomePage.Margin = new Thickness(0, ActualHeight < 740 ? 42 : 104, 0, 0);
        ShadowButton.SizeChanged += (_, _) => PlaceShadowGroup(); // raised after the layout pass: template label is arranged
        StateChanged += (_, _) => {
            if (WindowState != WindowState.Minimized) lastShownState = WindowState;
            if (WindowState == WindowState.Minimized && tray != null) Hide();
            UpdateLoadingText();
            WindowOutline.Visibility = WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
            MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M 0,3 L 7,3 7,10 0,10 Z M 3,3 L 3,0 10,0 10,7 7,7" : "M 0,0 L 10,0 10,10 0,10 Z");
        };
        timer.Tick += async (_, _) => { await engine.Tick(); UpdateState(); UpdateDiagnostics(); };
    }
    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DesktopCheck.IsChecked = settings.DesktopAudio; MicCheck.IsChecked = settings.Microphone;
        SyncPreset(); SyncDuration();
        foreach (var id in settings.SelectedDisplays) selected.Add(id);
        RefreshDisplays(); ApplyMode(StartupMode(), persist: StartupNeedsMigration()); loaded = true; BuildSettings(); UpdateState(); ShowUnexpectedExitNote();
        int hotkeyTest = Array.IndexOf(args, "--hotkey-ui-test");
        if (hotkeyTest >= 0)
        {
            int exitCode = 0;
            try { ValidateHotkeyDialogs(args[hotkeyTest + 1]); }
            catch (Exception error) { File.WriteAllText(args[hotkeyTest + 1] + ".error.txt", error.ToString()); exitCode = 1; }
            quitting = true; Application.Current.Shutdown(exitCode); return;
        }
        int render = Array.IndexOf(args, "--render");
        if (render >= 0)
        {
            Left = -20000; ShowInTaskbar = false;
            await Probe(); await RefreshLibrary(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            int uiTest = Array.IndexOf(args, "--ui-test");
            if (uiTest >= 0) await ValidateDesertUi(args[uiTest + 1]);
            if (args.Contains("--minimum")) { Width = MinWidth; Height = MinHeight; }
            if (args.Contains("--render-library")) { ShowPage(LibraryPage, "Library", ""); await RefreshLibrary(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
            if (args.Contains("--render-states")) // diagnostic: every Shadow label state; stone position measured per state
            {
                var states = new (string Name, string Title, string Loading, bool Active)[] {
                    ("start", "Start shadow recording", "", false), ("starting", "Starting…", "Prepar", false), ("starting-full", "Starting…", ShadowPreparingLabel, false),
                    ("stop", "Stop shadow recording", "", true), ("stopping", "Stopping recording…", "", true) };
                var positions = new List<object>();
                foreach (var st in states)
                {
                    UpdateRecordingPresentation(st.Active, RecordingState.Idle); ShadowTitle.Text = st.Title;
                    ShadowLoadingText.Text = st.Loading; ShadowLoadingText.Visibility = st.Loading.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                    loadingTimer.Stop(); UpdateLayout(); await Task.Delay(900); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var stone = (FrameworkElement)ShadowButton.Template.FindName("Stone", ShadowButton);
                    var at = stone.TransformToAncestor(this).Transform(new Point());
                    positions.Add(new { st.Name, x = Math.Round(at.X, 3), y = Math.Round(at.Y, 3), w = stone.ActualWidth, h = stone.ActualHeight, stoneVisualMid = Math.Round(at.Y + stone.ActualHeight * StoneVisualCentre(), 2), sliderTrackMid = Math.Round(HistorySlider.TranslatePoint(new Point(0, HistorySlider.ActualHeight / 2), this).Y, 2), sliderX = Math.Round(HistorySlider.TranslatePoint(new Point(), this).X, 2), labelX = Math.Round(ShadowTitle.TranslatePoint(new Point(), this).X, 2), folderTextX = Math.Round(CaptureFolderButton.TranslatePoint(new Point(CaptureFolderButton.BorderThickness.Left + CaptureFolderButton.Padding.Left, 0), this).X, 2), folderY = Math.Round(CaptureFolderButton.TranslatePoint(new Point(), this).Y, 2), titleBottom = Math.Round(ShadowTitle.TranslatePoint(new Point(0, ShadowTitle.ActualHeight), this).Y, 2) });
                    SaveRender(Path.Combine(Path.GetDirectoryName(args[render + 1])!, Path.GetFileNameWithoutExtension(args[render + 1]) + "-" + st.Name + ".png"), 1);
                }
                await File.WriteAllTextAsync(args[render + 1] + ".states.json", JsonSerializer.Serialize(positions));
            }
            if (Array.IndexOf(args, "--render-wave-phase") is int wp and >= 0) { atmosphere.Waves?.Advance(double.Parse(args[wp + 1], System.Globalization.CultureInfo.InvariantCulture)); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); } // diagnostic: dune waves at a given time
            if (args.Contains("--render-active")) // diagnostic: active Shadow presentation without starting capture
            {
                UpdateRecordingPresentation(true, RecordingState.Idle); ShadowTitle.Text = "Stop shadow recording"; PushCaptureView();
                await Task.Delay(1200); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            if (Array.IndexOf(args, "--render-animation") is int ra and >= 0) await RenderAnimation(args[ra + 1]); // diagnostic: deterministic animation frames (AnimationCapture.cs)
            // --render-settings: Settings top, sections collapsed; --render-settings-expanded: Recording and General open, scrolled to Recording.
            if (args.Contains("--render-hotkeys") || args.Contains("--render-storage") || args.Contains("--render-settings") || args.Contains("--render-settings-expanded"))
            {
                bool expand = args.Contains("--render-settings-expanded");
                foreach (var section in SettingsPage.Children.OfType<Expander>()) section.IsExpanded = expand && section.Header is "Recording" or "General";
                ShowPage(SettingsPage, "Tuned to you.", "PREFERENCES"); UpdateLayout();
                FrameworkElement heading = expand ? SettingsPage.Children.OfType<Expander>().First(x => x.Header as string == "Recording") : SettingsPage.Children.OfType<TextBlock>().First(t => t.Text == (args.Contains("--render-storage") ? "Storage" : "Hotkeys"));
                PageScroll.ScrollToVerticalOffset(heading.TransformToAncestor(SettingsPage).Transform(new Point()).Y);
                if (expand) await Task.Delay(300); // QuietExpander body fade-in (0.16 s)
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            if (args.Contains("--render-settings-advanced")) // diagnostic: Settings › Advanced open (FFmpeg, encoder, Diagnostics), scrolled to the end
            {
                ShowPage(SettingsPage, "", ""); foreach (var section in SettingsPage.Children.OfType<Expander>()) section.IsExpanded = section.Header as string == "Advanced settings";
                UpdateLayout(); await Task.Delay(300); PageScroll.ScrollToEnd(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            if (args.Contains("--render-updates"))
            {
                ShowPage(SettingsPage, "", ""); foreach (var section in SettingsPage.Children.OfType<Expander>()) section.IsExpanded = section.Header as string == "General";
                UpdateLayout(); await Task.Delay(300); PageScroll.ScrollToEnd(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await ValidateUpdateControls(args[render + 1] + ".checks.json");
            }
            if (args.Contains("--render-settings-pending")) // diagnostic: Settings with one pending Apply change (bitrate) through the Smart Apply draft; --render-scroll <dip> scrolls the page
            {
                settingsDraft["BitrateMbps"] = settings.BitrateMbps == 25 ? "26" : "25"; BuildSettings(); ShowPage(SettingsPage, "", ""); UpdateLayout();
                if (Array.IndexOf(args, "--render-scroll") is int rs and >= 0) PageScroll.ScrollToVerticalOffset(double.Parse(args[rs + 1], System.Globalization.CultureInfo.InvariantCulture));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            if (Array.IndexOf(args, "--display-ui-test") is int displayTest and >= 0) await ValidateDisplayCards(args[displayTest + 1]); // diagnostic: monitor cards (with --fake-displays)
            if (Array.IndexOf(args, "--hotkey-names-test") is int namesTest and >= 0) await ValidateHotkeyNames(args[namesTest + 1]); // diagnostic: layout-aware hotkey labels (HotkeyNames.cs)
            if (args.Contains("--render-source")) { SourceExpander.IsExpanded = true; UpdateLayout(); await Task.Delay(400); PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset + SourceExpander.TranslatePoint(new Point(), PageScroll).Y - 60); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); } // diagnostic: Capture source map (after the 0.16 s expand fade)
            if (Array.IndexOf(args, "--nav-test") is int navTest and >= 0) await ValidateNavigation(args[navTest + 1]); // diagnostic: sidebar corner frame
            if (Array.IndexOf(args, "--diagnostics-test") is int diagTest and >= 0) await ValidateDiagnostics(args[diagTest + 1]); // diagnostic: logging, failures, export
            SaveRender(args[render + 1], args.Contains("--scale150") ? 1.5 : 1);
            atmosphere.Dispose(); gameTracker.Dispose();
            quitting = true; Application.Current.Shutdown(); return;
        }
        try { SetupTray(); } catch (Exception trayError) { tray = null; Notify("The tray icon could not be created: " + trayError.Message); DiagnosticLog.Error("tray", "Tray icon could not be created; minimize keeps the window on the taskbar", trayError); }
        RegisterKeys(); timer.Start(); SetupGameMinimize();
        bool background = args.Contains("--background");
        if (background) { Hide(); ShowInTaskbar = true; }
        if (!args.Contains("--data")) SyncStartup();
        engine.IdleNoticed += label => DiagnosticLog.Write("capture-idle", label + ": no new frames for 15 s (screen unchanged); capture continues");
        await RefreshLibrary();
        if (!settings.Onboarded)
        {
            ShowPage(SettingsPage, "Make it yours.", "WELCOME TO FRAME");
            Notify("Choose your capture preferences below. Replay stays off until you enable it. Default hotkeys use Ctrl + Shift + R / P / S / M. Choose the capture folder on the Capture page.");
        }
        await Probe();
        // Remembered choice: start buffering automatically (also after a Start-with-Windows launch).
        if (settings.StartShadowOnLaunch && ready && !engine.Replay && !args.Contains("--idle-benchmark")) await Act(ToggleShadow);
        ShowStartupDisplayNote(); // a display fallback found at start-up stays readable after the engine check / auto-start messages
        if (await RunRecordingHarness()) return; // --quality-change-test / --close-test (isolated diagnostics, RecordingHarness.cs)
        if (await RunModeHarness()) return; // --mode-switch-test (isolated diagnostics, ModeHarness.cs)
        if (!args.Contains("--data") && settings.CheckForUpdatesOnStartup) _ = CheckUpdates(true);
        int captureTest = Array.IndexOf(args, "--capture-test");
        if (captureTest >= 0)
        {
            try { await CaptureReliabilityTest(args[captureTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[captureTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int audioTest = Array.IndexOf(args, "--audio-test");
        if (audioTest >= 0 && args.Contains("--data"))
        {
            try { await AudioReliabilityTest(args[audioTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[audioTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int animBench = Array.IndexOf(args, "--anim-benchmark");
        if (animBench >= 0 && args.Contains("--data"))
        {
            try { await AnimationBenchmark(args[animBench + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[animBench + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int layoutTest = Array.IndexOf(args, "--capture-ui-test");
        if (layoutTest >= 0 && args.Contains("--data"))
        {
            try { await CaptureLayoutTest(args[layoutTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[layoutTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int resourceTest = Array.IndexOf(args, "--resource-test");
        if (resourceTest >= 0 && args.Contains("--data"))
        {
            try { await ResourceTest(args[resourceTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[resourceTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int libraryTest = Array.IndexOf(args, "--library-test");
        if (libraryTest >= 0 && args.Contains("--data"))
        {
            try { await LibraryPreviewTest(args[libraryTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[libraryTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int lengthTest = Array.IndexOf(args, "--live-length-test");
        if (lengthTest >= 0 && args.Contains("--data"))
        {
            try { await LiveLengthTest(args[lengthTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[lengthTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int openTest = Array.IndexOf(args, "--open-hotkey-test");
        if (openTest >= 0 && args.Contains("--data"))
        {
            try { await OpenHotkeyTest(args[openTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[openTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int gameTest = Array.IndexOf(args, "--game-test");
        if (gameTest >= 0 && args.Contains("--data"))
        {
            try { await GameStressTest(args[gameTest + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[gameTest + 1] + ".error.txt", error.ToString()); }
            await Quit(); return;
        }
        int backgroundTest = Array.IndexOf(args, "--background-test");
        if (backgroundTest >= 0)
        {
            await File.WriteAllTextAsync(args[backgroundTest + 1], JsonSerializer.Serialize(new { background, visible = IsVisible, taskbarWhileHidden = ShowInTaskbar && IsVisible, tray = tray?.Visible == true, ready, hotkeys = hotkeys.Count, replay = engine.Replay, atmosphere = atmosphere.Running, startNotices = SaveOverlay.History.Count(h => h == "Shadow Recording active"), overlays = SaveOverlay.History.ToArray(), trayState = TrayStateName, selectedDisplays = selected.ToArray(), primaryDisplay = PrimaryDisplayId(), followsPrimary = settings.FollowPrimaryDisplay }));
            await Quit(); return;
        }
        int benchmark = Array.IndexOf(args, "--idle-benchmark");
        if (benchmark >= 0)
        {
            await Task.Delay(2000); using var p = Process.GetCurrentProcess(); double before = p.TotalProcessorTime.TotalSeconds; var measured = Stopwatch.StartNew();
            await Task.Delay(10000); p.Refresh();
            var result = new { mode = "Idle native UI, tray and 1 Hz diagnostics; no capture", elapsedSeconds = measured.Elapsed.TotalSeconds, processCpuPercent = (p.TotalProcessorTime.TotalSeconds - before) / measured.Elapsed.TotalSeconds / Environment.ProcessorCount * 100, workingSetMb = p.WorkingSet64 / 1048576d, privateBytesMb = p.PrivateMemorySize64 / 1048576d, displayCount = displays.Length, registeredHotkeys = hotkeys.Count, encoder = ChooseEncoder().Name };
            await File.WriteAllTextAsync(args[benchmark + 1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            timer.Stop(); tray?.Dispose(); quitting = true; Application.Current.Shutdown();
        }
    }
    // Typed status under the Shadow label: engine check ("Preparing capture…") and Shadow start-up ("Preparing shadow
    // recording..."). One 55 ms DispatcherTimer, only while visible (also in Performance mode); stops the moment the
    // engine is ready / Shadow is confirmed active / a start fails. Lives in the label column, so it never moves the stone.
    const string ShadowPreparingLabel = "Preparing shadow recording...";
    bool preparingShadow;
    string? TypedLabel() => !ready && checkingEngine ? EngineLoadingLabel : preparingShadow ? ShadowPreparingLabel : null;
    void UpdateLoadingText()
    {
        bool preparing = ready && shadowTransition && !engine.Replay;
        if (preparing && !preparingShadow) loadingCharacters = 0; // start typing from the first character
        preparingShadow = preparing;
        var label = TypedLabel();
        ShadowLoadingText.Visibility = label != null || (!ready && !checkingEngine) ? Visibility.Visible : Visibility.Collapsed;
        // Typing is a status indicator, not decoration: it also runs in Performance mode (one text change per 55 ms, only
        // while initialising), but still stops whenever FRAME is hidden, minimized, in the tray or cloaked.
        ShadowLoadingText.Text = label != null ? label[..Math.Min(loadingCharacters, label.Length)] : !ready ? (engineProblem != null ? "FFmpeg needed · see below" : "Capture engine unavailable · check Settings") : "";
        if (label != null && IsVisible && WindowState != WindowState.Minimized && !atmosphere.Cloaked) loadingTimer.Start();
        else loadingTimer.Stop();
        activeLayout?.ShowLoading(ShadowLoadingText.Text, ShadowLoadingText.Visibility == Visibility.Visible);
    }
    async Task Probe()
    {
        loadingCharacters = 0;
        checkingEngine = true; ready = false; UpdateState(); Notify("Checking installed capture and encoding capabilities…");
        try
        {
            var exe = Backend.Resolve(settings.FfmpegPath); encoders = await Task.Run(() => Backend.Probe(exe));
            DiagnosticLog.Write("engine", $"{exe} · encoders: {string.Join(", ", encoders.Select(x => x.Id))} · selected {ChooseEncoder().Id}");
            ready = true; HideEngineProblem(); BuildSettings(); if (LibraryPage.Visibility == Visibility.Visible) _ = RefreshLibrary(); // previews waiting for FFmpeg Notify($"Ready · {ChooseEncoder().Name}. Shadow recording is {(engine.Replay ? "on" : "off")}." + (hotkeyIssue.Length > 0 ? " " + hotkeyIssue : ""));
        }
        catch (Exception e)
        {
            Notify(e is FileNotFoundException ? "FFmpeg is needed to record · see the Capture page." : "FFmpeg cannot be used · see the Capture page."); DiagnosticLog.Error("engine-failed", "FFmpeg check failed: " + e.Message, e); ShowEngineProblem(e); UpdateState();
            if (!settings.Onboarded && SettingsPage.Visibility == Visibility.Visible) ShowPage(HomePage, "Capture", ""); // first run: the FFmpeg panel matters more than the preferences page
        }
        checkingEngine = false;
        UpdateState();
    }
    Encoder? forcedEncoder; // --diagnostics-test only: an encoder FFmpeg rejects, to exercise the start-failure path
    Encoder ChooseEncoder() => forcedEncoder ?? encoders.FirstOrDefault(e => e.Id == settings.Encoder) ?? encoders.FirstOrDefault(e => e.Id.StartsWith("h264") && e.Hardware) ?? encoders.FirstOrDefault() ?? new("libx264", "CPU · H.264", false, false);
    // Sources for a start. A start (capture idle) enumerates the displays again (current monitor handles) and recovers a saved
    // selection that no longer matches (DisplayRecovery.cs), so Start/auto-start work after a display was switched off. While
    // capture runs (a manual start joining Shadow's pipelines), the running sources are used and nothing is changed.
    CaptureTarget[] Targets()
    {
        if (testSource.Length > 0) return testSource;
        if (!engine.Busy) { displays = FakeDisplays() ?? Native.Displays(); RecoverDisplaySelection(starting: true); DrawMonitors(); }
        var chosen = displays.Where(d => selected.Contains(d.Id)).ToArray();
        if (chosen.Length == 0 && !engine.Busy) throw new InvalidOperationException(displays.Length == 0 ? "No display is available to capture. Turn on or connect a display, then try again." : "Choose a display to capture on the Capture page.");
        return chosen;
    }
    void RefreshDisplays()
    {
        displays = FakeDisplays() ?? Native.Displays(); RecoverDisplaySelection();
        DrawMonitors();
    }
    // Monitor map + source summary. The cards (DisplayMap.cs) are rebuilt only on display, width, selection, theme and
    // capture-lock changes; there is no timer or polling. A display recovery deferred by a running capture is applied here
    // once capture is idle (UpdateState redraws when the lock lifts).
    void DrawMonitors()
    {
        if (displayRecoveryPending && !SelectionLocked(false)) RecoverDisplaySelection();
        BuildDisplayCards();
        var chosen = displays.Where(d => selected.Contains(d.Id)).ToArray();
        SourceExpander.Header = testSource.Length > 0 ? testSource[0].Name : chosen.Length == 1 ? chosen[0].Name : chosen.Length > 1 ? $"{chosen.Length} displays" : "Capture source";
        SourceLabel.Text = displays.Length == 0 ? "No displays detected" : testSource.Length > 0 ? $"{testSource[0].Bounds.Width} × {testSource[0].Bounds.Height} · diagnostics test source"
            : chosen.Length == 0 ? "Selected display not connected" : (chosen.Length == 1 ? $"{chosen[0].Bounds.Width} × {chosen[0].Bounds.Height}" : settings.Combined ? "Combined canvas" : "Independent files") + (displayHint.Length > 0 ? " · " + displayHint : "");
        SourceExpander.ToolTip = engine.Busy ? "Stop capture before changing source." : "Choose which displays to capture.";
    }
    void MonitorSizeChanged(object sender, SizeChangedEventArgs e) { if (e.WidthChanged && displays.Length > 0) DrawMonitors(); } // height is set by DrawMonitors itself
    async Task Act(Func<Task> action)
    {
        if (quitRequested || quitting) return;
        if (!await actions.WaitAsync(0)) { Notify("An operation is already in progress."); return; }
        try { UpdateState(); await action(); }
        catch (Exception e) { Notify(e.Message); if (!quitRequested && !IsVisible && SaveOverlay.Current == null) SaveOverlay.Present("FRAME", SaveFeedback.Failed(e.Message), false); await LogError(e); }
        finally { actions.Release(); UpdateState(); }
    }
    async Task LogError(Exception error)
    {
        try
        {
            await Task.Run(() => DiagnosticLog.Error("action", error.Message, error));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Message.Text += " Diagnostic log could not be written."; }
    }
    async void RecordClick(object sender, RoutedEventArgs e) => await Act(ToggleRecord);
    async Task ToggleRecord()
    {
        if (engine.State is RecordingState.Recording or RecordingState.Paused) { await engine.StopRecording(); await RefreshLibrary(); }
        else { engine.ResetFault(); if (!ready) throw new InvalidOperationException("Choose a compatible FFmpeg engine in Settings."); await engine.StartRecording(CloneSettings(), ChooseEncoder(), Targets()); }
    }
    async void PauseClick(object sender, RoutedEventArgs e) => await Act(engine.PauseResume);
    async void ReplaySaveClick(object sender, RoutedEventArgs e) => await RequestSaveReplay();
    // Single entry for hotkey, button and tray. A request that cannot start now is answered at once instead of
    // being dropped silently: a save already running keeps its job (no second export), any other action asks to retry.
    async Task RequestSaveReplay()
    {
        if (quitRequested || quitting) return;
        if (savingReplay)
        {
            Notify("A replay is already being saved."); DiagnosticLog.Write("replay-save", "Save request ignored: a replay is already being saved");
            if (settings.ShowSaveNotifications) SaveOverlay.Present("Still saving replay…", "Your previous clip is processing", true, pending: true);
            return;
        }
        if (actions.CurrentCount == 0)
        {
            Notify("Replay not saved: another action is in progress."); DiagnosticLog.Warning("replay-save", "Save request refused: another action is in progress");
            if (settings.ShowSaveNotifications) SaveOverlay.Present("Replay not saved", "FRAME is busy · try again in a moment", false);
            return;
        }
        await Act(SaveShadow);
    }
    async Task SaveShadow()
    {
        var edges = engine.RequestEdges(); // the moment of the request, before any waiting
        savingReplay = true; UpdateState();
        // Accepted request: say so immediately (before segment waits, ffmpeg and verification). The completion note
        // replaces this one in the same overlay; success is reported only after every file was exported and verified.
        if (settings.ShowSaveNotifications && engine.Replay) SaveOverlay.Present("Saving replay…", "Processing your clip", true, pending: true);
        try
        {
            Notify("Saving shadow recording…");
            var files = await engine.SaveReplay(edges);
            if (settings.ShowSaveNotifications) SaveOverlay.Present("Clip saved", SaveFeedback.Saved(await SavedSeconds(files), files.Count), true);
            await RefreshLibrary();
        }
        catch (Exception e) when (settings.ShowSaveNotifications) { SaveOverlay.Present("Replay not saved", SaveFeedback.Failed(e.Message), false); throw; }
        finally { savingReplay = false; UpdateState(); }
    }
    // Longest verified clip length via ffprobe (already validated by export); 0 falls back to "Clip saved".
    async Task<double> SavedSeconds(IReadOnlyList<string> files)
    {
        double longest = 0;
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(Backend.Resolve(settings.FfmpegPath))!, "ffprobe.exe");
            foreach (var file in files)
            {
                var r = await Backend.Run(probe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file], 10);
                if (r.Code == 0 && double.TryParse(r.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) longest = Math.Max(longest, d);
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { }
        return longest;
    }
    async void ShadowClick(object sender, RoutedEventArgs e) => await RequestShadowToggle();
    // Dot left of "SHADOW RECORDING" (0.5.5): the engine's real state, not the button's request. Grey = off (also after a
    // failed start or a fault), hollow amber ring = starting or stopping, solid amber = recording. No animation.
    internal string ShadowDotState => engine.Replay && !shadowTransition && !stopRequested ? "active" : shadowTransition || stopRequested ? "transition" : "off";
    void UpdateShadowDot()
    {
        string state = ShadowDotState;
        if (ShadowStatusDot.Tag as string == state) return;
        ShadowStatusDot.Tag = state;
        if (state == "off") { ShadowStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Muted"); ShadowStatusDot.Stroke = null; ShadowStatusDot.Opacity = .55; }
        else if (state == "active") { ShadowStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent"); ShadowStatusDot.Stroke = null; ShadowStatusDot.Opacity = 1; }
        else { ShadowStatusDot.Fill = Brushes.Transparent; ShadowStatusDot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent"); ShadowStatusDot.StrokeThickness = 1.5; ShadowStatusDot.Opacity = .9; }
        System.Windows.Automation.AutomationProperties.SetName(ShadowStatusDot, state == "active" ? "Shadow Recording is on" : state == "off" ? "Shadow Recording is off" : "Shadow Recording is starting or stopping");
    }
    // Start goes through Act as before. Stop is never rejected: before 0.5.4 a Stop click during a running replay
    // save/export (which holds the action gate for up to a minute) was refused ("An operation is already in progress")
    // and the button was disabled, so Stop seemed dead until the export ended. Now Stop is accepted at once, shows
    // "Stopping recording…", waits for the running action without blocking the UI, and duplicate requests are ignored.
    bool stopRequested;
    async Task RequestShadowToggle()
    {
        if (quitRequested || quitting) return;
        if (!engine.Replay) { await Act(ToggleShadow); return; }
        if (stopRequested) return;
        stopRequested = true; Notify("Stopping recording…"); UpdateState();
        try
        {
            await actions.WaitAsync();
            try { if (!quitRequested && engine.Replay) await ToggleShadow(); }
            catch (Exception e) { Notify(e.Message); await LogError(e); }
            finally { actions.Release(); }
        }
        finally { stopRequested = false; UpdateState(); }
    }
    async Task ToggleShadow()
    {
        if (!ready) throw new InvalidOperationException("Capture engine is not ready.");
        bool enable = !engine.Replay;
        shadowTransition = true; UpdateState();
        try
        {
            if (enable) engine.ResetFault();
            try { await engine.SetReplay(enable, CloneSettings(), ChooseEncoder(), Targets()); }
            catch (Exception e) when (enable) { OnStartFailure(e); throw; }
            if (enable) { MarkRecordingHealthy(); if (settings.ShowSaveNotifications) SaveOverlay.Present("Shadow Recording active", "Ready to save clips", true); }
            Notify(enable ? "Shadow recording started. Save a moment whenever you want." : "Shadow recording stopped. Saved clips remain in your library." + (engine.Busy ? " Manual recording continues." : ""));
        }
        finally { shadowTransition = false; UpdateState(); }
    }
    static string HistoryLabel(int seconds) => HistoryDial.Label(seconds);
    void SyncDuration() => HistorySlider.SetSeconds(settings.ReplaySeconds);
    // Capture-page Shadow group layout. Everything here depends only on the available width and the fixed title strings,
    // never on the current label, so Start/Starting/Stop/Stopping cannot move the folder buttons or the slider; the
    // obsidian stone itself is laid out by the button template and is not touched.
    // 1. Folder buttons: left edge of their text on the label's left edge, directly below the tallest label state
    //    (widest title wrapped to the label column, or "Starting…" + typed status). ShadowButton.Padding (40) reserves
    //    that room, so label + buttons form one group centred on the stone.
    // 2. Minute slider (left of the stone, 0.5.4): its track midpoint
    //    (= control midpoint: the track is symmetric) on the visual centre of the stone (alpha bounds).
    // Since 0.5.5 the stone artwork rotates about that centre (ObsidianStone), so its visible extent is the swept circle
    // (StoneArt: farthest visible pixel of the OFF/ON images, incl. pulse swell and hover scale), measured once.
    static double StoneVisualLeft() => StoneArt.SweptLeft;    // fraction of the box width where the rotating stone can reach
    static double StoneVisualCentre() => StoneArt.Centre.Y;   // fraction of the box height: rotation centre = visual centre
    void PlaceShadowGroup()
    {
        if (PresentationSource.FromVisual(ShadowTitle) == null || !ShadowTitle.IsDescendantOf(ShadowButton)) return;
        double labelLeft = ShadowTitle.TransformToAncestor(ShadowButton).Transform(new Point()).X, column = Math.Max(1, ShadowButton.ActualWidth - labelLeft);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Measure(string text, TextBlock style, double width) => new(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(style.FontFamily, style.FontStyle, style.FontWeight, style.FontStretch), style.FontSize, Brushes.White, dip) { MaxTextWidth = width }; // 0 = single line
        double titleWidth = 0, labelHeight = 0;
        foreach (var text in new[] { "Start shadow recording", "Stop shadow recording", "Stopping recording…" })
        {
            titleWidth = Math.Max(titleWidth, Measure(text, ShadowTitle, 0).WidthIncludingTrailingWhitespace);
            labelHeight = Math.Max(labelHeight, Measure(text, ShadowTitle, column).Height);
        }
        labelHeight = Math.Max(labelHeight, Measure("Starting…", ShadowTitle, column).Height + ShadowLoadingText.Margin.Top + Measure(ShadowPreparingLabel, ShadowLoadingText, column).Height);
        // The label is centred in the button height minus Padding.Bottom; the buttons follow its tallest state.
        double labelCentre = (ShadowButton.ActualHeight - ShadowButton.Padding.Bottom) / 2, gap = 4;
        // QuietButton text starts after its 1 px border + 8 px padding: shift so the text, not the invisible chrome, aligns.
        var first = CaptureFolderButton; double inset = first.BorderThickness.Left + first.Padding.Left;
        var origin = ShadowButton.TranslatePoint(new Point(), ShadowRow); // the button sits at the group's margin, not at the row origin
        FolderButtons.Margin = new Thickness(origin.X + labelLeft - inset, origin.Y + labelCentre + labelHeight / 2 + gap, 0, 0);

        var stone = (FrameworkElement)ShadowButton.Template.FindName("Stone", ShadowButton);
        // Centre point is unaffected by the stone's centred hover/press scale; the image fills the square stone box.
        double centre = stone.TranslatePoint(new Point(stone.ActualWidth / 2, stone.ActualHeight / 2), ShadowRow).Y + (StoneVisualCentre() - .5) * stone.ActualHeight;
        // The slider sits in its own column left of the Shadow stone; only its height is aligned (RenderTransform, so the
        // row height is not changed by a negative offset).
        // Horizontally ~18 px toward the stone, but never closer than 30 px to the rotating stone's swept edge (any angle,
        // hover or pulse; minimum window).
        double stoneEdge = stone.TranslatePoint(new Point(stone.ActualWidth * StoneVisualLeft(), 0), ShadowRow).X;
        double handleRight = HistorySlider.TranslatePoint(new Point(HistorySliderControl.HandleRight, 0), ShadowRow).X - (HistorySlider.RenderTransform as TranslateTransform)?.X ?? 0;
        HistorySlider.RenderTransform = new TranslateTransform(Math.Clamp(stoneEdge - 30 - handleRight, 0, 18), centre - HistorySlider.Height / 2);
    }
    void CommitHistory(int seconds)
    {
        // Also while Shadow Recording runs (and during a save): the running engine takes the new length at once. Only a
        // Shadow start/stop in progress refuses it (the engine is being set up or torn down).
        if (!loaded || shadowTransition || engine.State is RecordingState.Starting or RecordingState.Finalizing) { SyncDuration(); return; }
        var next = CloneSettings(); next.ReplaySeconds = seconds;
        try { SettingsStore.Save(configPath, next); settings = next; if (engine.Busy) engine.SetReplaySeconds(seconds); SyncDuration(); UpdateState(); BuildSettings(); }
        catch (Exception error) { SyncDuration(); Notify("History length could not be saved: " + error.Message); }
    }
    Settings CloneSettings() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
    void AudioChanged(object sender, RoutedEventArgs e) { if (!loaded) return; settings.DesktopAudio = DesktopCheck.IsChecked == true; settings.Microphone = MicCheck.IsChecked == true; SaveSettings(); }
    void SyncPreset()
    {
        bool wasLoaded = loaded; loaded = false;
        PresetCombo.SelectedIndex = (settings.OutputHeight, settings.Fps, settings.BitrateMbps) switch { (1080, 60, 20) => 0, (0, 60, 20) => 1, (720, 30, 6) => 2, _ => 3 };
        loaded = wasLoaded;
        PushCaptureView();
    }
    // Capture › quality. Idle: saved at once (as before). While Shadow Recording runs: Shadow restarts with the new profile
    // through RestartShadowWith (ShadowRestart.cs; one transition at a time, the replay length is kept). During manual recording,
    // a start/stop or another action the combo is disabled; a change that still arrives (keyboard/automation) is ignored.
    Task? presetChange; // the running Shadow restart for a quality change (diagnostics await it)
    void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded || PresetCombo.SelectedIndex is < 0 or > 2) return;
        int index = PresetCombo.SelectedIndex; var next = CloneSettings();
        next.OutputHeight = index == 0 ? 1080 : index == 2 ? 720 : 0; next.Fps = index == 2 ? 30 : 60; next.BitrateMbps = index == 2 ? 6 : 20;
        if (PresetBlockedReason() is { } blocked) { SyncPreset(); Notify(blocked); DiagnosticLog.Write("quality-change", "Ignored: " + blocked); return; }
        if (engine.Replay) { presetChange = Act(async () => await RestartShadowWith(next, $"Quality changed to {PresetName(next)}.")); return; }
        try { SettingsStore.Save(configPath, next); settings = next; }
        catch (Exception error) { SyncPreset(); Notify("Quality was not saved: " + error.Message); DiagnosticLog.Error("settings", "Quality preset was not saved", error); return; }
        DrawMonitors(); BuildSettings();
    }
    void SaveSettings() { try { SettingsStore.Save(configPath, settings); } catch (Exception e) { Notify("Settings were not saved: " + e.Message); DiagnosticLog.Error("settings", "Settings were not saved", e); } }
    // In-window status line only. Transient alerts use the top-right SaveOverlay; FRAME shows no tray balloons.
    void Notify(string text) => Message.Text = text;
    void UpdateRecordingPresentation(bool shadow, RecordingState state)
    {
        bool manual = state is RecordingState.Recording or RecordingState.Paused;
        ShadowButton.Tag = shadow ? "Active" : null;
        RecordButton.Tag = state == RecordingState.Recording ? "Active" : null;
        ShadowButton.ToolTip = shadow ? "Stop buffering; unsaved history is cleared" : "Continuously capture recent gameplay";
        RecordButton.ToolTip = manual ? "Finish and save this recording" : "Record until you choose to stop";
        RecordTitle.Text = manual ? "Stop manual recording" : "Start manual recording";
    }
    async void CaptureFolderClick(object sender, RoutedEventArgs e) => await Act(async () => {
        if (engine.Busy) throw new InvalidOperationException("Stop shadow and manual recording before changing folder.");
        if (BrowseForFolder(settings.OutputDirectory, "Choose capture folder") is not { } folder) return;
        CommitCaptureFolder(folder); await RefreshLibrary(); BuildSettings(); Notify("Capture folder updated.");
    });
    // Windows folder pickers list folders only, which made capture folders look empty. The standard Explorer file dialog
    // shows files (videos, images, everything) while browsing; "Select this folder" returns the folder being viewed, and
    // choosing a file returns its folder. Nothing changes until the user confirms; cancel keeps the current folder.
    string? lastBrowseFolder;
    string? BrowseForFolder(string current, string title)
    {
        string start = Directory.Exists(current) ? current : Directory.Exists(lastBrowseFolder) ? lastBrowseFolder! : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title + " · open the folder, then press Open", InitialDirectory = start, FileName = "Select this folder",
            Filter = "All files|*.*|Videos|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = false, CheckPathExists = true, ValidateNames = false, DereferenceLinks = true, AddToRecent = false
        };
        if (dialog.ShowDialog(this) != true) return null;
        string chosen = dialog.FileName;
        string? folder = Directory.Exists(chosen) ? chosen : Path.GetDirectoryName(chosen);
        if (folder == null || !Directory.Exists(folder)) { Notify("That folder is not available."); return null; }
        lastBrowseFolder = folder; return Path.GetFullPath(folder);
    }
    void CommitCaptureFolder(string folder)
    {
        if (engine.Busy) throw new InvalidOperationException("Stop shadow and manual recording before changing folder.");
        var next = CloneSettings(); next.OutputDirectory = folder; next.Validate();
        Directory.CreateDirectory(folder);
        string probe = Path.Combine(folder, ".frame-write-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) stream.WriteByte(0);
        SettingsStore.Save(configPath, next); settings = next; UpdateState(); BuildSettings();
    }
    // Capture › Change hotkey: opens Settings › Hotkeys and starts the inline editor on that action's field, so both places
    // edit the same setting (and the same registration, conflict checks and persistence).
    void ChangeHotkeyClick(object sender, RoutedEventArgs e) => ShowHotkeys("Replay", edit: true);
    void ChangeShadowHotkeyClick(object sender, RoutedEventArgs e) => ShowHotkeys("Shadow", edit: true);
    void ShowHotkeys(string action = "Replay", bool edit = false)
    {
        BuildSettings(); ShowPage(SettingsPage, "Settings", ""); UpdateLayout();
        PageScroll.ScrollToTop(); // Hotkeys are the first Settings section
        if (hotkeyButtons.FirstOrDefault(b => b.Tag is Hotkey h && h.Action == action) is not { } field) return;
        field.Focus();
        if (edit && field.Parent is StackPanel { Children.Count: > 1 } cell && cell.Children[1] is TextBlock note) BeginHotkeyEdit(field, note);
    }
    void UpdateState()
    {
        StatusBadge.Text = engine.State switch { RecordingState.Recording => "●  Recording", RecordingState.Paused => "Ⅱ  Paused", RecordingState.Finalizing => "Finalizing…", RecordingState.Starting => "Starting…", RecordingState.Faulted => "Action needed", _ => engine.Replay ? "●  Shadow active" : ready ? "●  Ready" : engineProblem != null ? "FFmpeg needed" : "Checking engine" };
        UpdateRecordingPresentation(engine.Replay && !shadowTransition, engine.State);
        UpdateShadowDot();
        bool operationPending = actions.CurrentCount == 0;
        RecordButton.IsEnabled = ready && !operationPending && engine.State is not (RecordingState.Starting or RecordingState.Finalizing);
        double available = engine.Replay ? Math.Min(settings.ReplaySeconds, engine.BufferedSeconds) : 0;
        ShadowTitle.Text = stopRequested || (shadowTransition && engine.Replay) ? "Stopping recording…" : shadowTransition ? "Starting…" : engine.Replay ? "Stop shadow recording" : "Start shadow recording";
        // Stop stays clickable while another action (e.g. a replay export) runs; it is queued, never refused.
        ShadowButton.IsEnabled = ready && !stopRequested && (engine.Replay ? !shadowTransition : !operationPending) && engine.State is not (RecordingState.Starting or RecordingState.Finalizing);
        UpdateLoadingText();
        HistorySlider.IsEnabled = !shadowTransition && engine.State is not (RecordingState.Starting or RecordingState.Finalizing);
        HistorySlider.ToolTip = engine.Replay ? "Change anytime: saves use the new length at once; a longer history fills up as Shadow Recording continues" : "Drag the shard up for more history; mouse wheel or arrow keys adjust";
        HistorySlider.SetSeconds(settings.ReplaySeconds);
        string duration = settings.ReplaySeconds < 60 ? $"{settings.ReplaySeconds} seconds" : $"{settings.ReplaySeconds / 60d:0.##} " + (settings.ReplaySeconds == 60 ? "minute" : "minutes");
        ReplaySaveTitle.Text = savingReplay ? "Saving…" : $"Save last {duration}";
        // Shortcut rows (Capture): same settings.Hotkeys as Settings › Hotkeys; refreshed on every state/slider/hotkey change.
        SaveHotkeyLabel.Text = $"Hotkey for saving the {duration}";
        SaveHotkeyText.Text = settings.Hotkeys.FirstOrDefault(h => h.Action == "Replay") is { } save ? FormatHotkey(save) : "Not set";
        ShadowHotkeyText.Text = settings.Hotkeys.FirstOrDefault(h => h.Action == "Shadow") is { } toggle ? FormatHotkey(toggle) : "Not set";
        CaptureFolderButton.IsEnabled = !engine.Busy && !operationPending;
        CaptureFolderButton.ToolTip = engine.Busy ? "Stop capture before changing the save folder" : "Choose where new captures are saved";
        ReplaySaveButton.IsEnabled = engine.Replay && available > 0 && !operationPending;
        // How much can be saved right now lives here (0.5.5), not in a line on the page that appeared when recording started.
        ReplaySaveButton.ToolTip = engine.ReplayUnavailableReason ?? $"Save the most recent footage · {(int)available / 60:00}:{(int)available % 60:00} of {HistoryLabel(settings.ReplaySeconds)} recorded so far";
        ToolTipService.SetShowOnDisabled(ReplaySaveButton, true);

        int streams = settings.Combined || testSource.Length > 0 ? 1 : Math.Max(1, selected.Count);
        double estimatedMb = settings.ReplaySeconds * (settings.BitrateMbps + (settings.DesktopAudio || settings.Microphone ? .192 : 0)) * streams / 8d;
        string budgetNote = estimatedMb > settings.ReplayBudgetMb ? " Disk budget may shorten history; increase it in Settings." : "";
        // Capture-page note (0.5.5): only what needs attention (a gap in the history, interrupted audio, a disk budget that may
        // shorten it). The live "00:01 available / 10 min" counter is gone: it appeared when recording started and pushed the
        // page down. The line keeps its reserved two-line height (Hidden, never Collapsed), so starting, stopping, saving,
        // a quality change or a new replay length never moves anything on the page.
        ReplayInfo.Text = engine.Replay && engine.Pipelines.Count > 0 && engine.Pipelines.All(p => p.Frames > 0) && engine.ReplayUnavailableReason is { } reason ? reason + budgetNote : budgetNote.Trim();
        if (AudioStatusText() is { Length: > 0 } audioText) ReplayInfo.Text = (ReplayInfo.Text + " " + audioText).Trim();
        ReplayInfo.Visibility = ReplayInfo.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        DesktopCheck.IsEnabled = MicCheck.IsEnabled = !engine.Busy && !operationPending; UpdatePresetAvailability(); // quality: also while Shadow runs (ShadowRestart.cs)
        if (cardsLocked != (engine.Busy || operationPending)) DrawMonitors(); // display cards: enabled state and tooltip
        PauseButton.Visibility = engine.State is RecordingState.Recording or RecordingState.Paused ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = engine.State == RecordingState.Paused ? "Resume" : "Pause";
        PauseButton.ToolTip = engine.State == RecordingState.Paused ? "Continue the current manual recording" : "Pause without ending this recording";
        string elapsed = TimeSpan.FromSeconds(engine.RecordedSeconds).ToString(@"hh\:mm\:ss"); RecordTimer.Text = PauseButton.Visibility == Visibility.Visible ? elapsed : "";
        UpdateTray(); UpdateFailurePanel();
        PushCaptureView(); // the active mode's Capture layout (ModeView.cs) shows the same state
    }
    void UpdateDiagnostics()
    {
        double totalCpu = 0; long memory = 0; int processes = 0;
        foreach (int id in engine.ProcessIds.Prepend(Environment.ProcessId)) try { using var p = Process.GetProcessById(id); totalCpu += p.TotalProcessorTime.TotalSeconds; memory += p.WorkingSet64; processes++; } catch (ArgumentException) { }
        double now = clock.Elapsed.TotalSeconds, cpu = Math.Max(0, totalCpu - previousCpu) / Math.Max(.1, now - previousTime) / Environment.ProcessorCount * 100;
        previousCpu = totalCpu; previousTime = now;
        Diagnostics.Text = $"Measured process usage · {cpu:0.0}% CPU · {memory / 1048576d:0} MB RAM · {processes} process{(processes > 1 ? "es" : "")}   |   {ChooseEncoder().Name}";
    }
    void SetupTray()
    {
        tray = new F.NotifyIcon { Text = "FRAME · Ready", Icon = CreateIcon(), Visible = true }; var menu = new F.ContextMenuStrip();
        menu.Items.Add("Open FRAME", null, (_, _) => Restore()); menu.Items.Add("Start / stop manual recording", null, async (_, _) => await Act(ToggleRecord));
        menu.Items.Add("Pause / resume", null, async (_, _) => await Act(engine.PauseResume)); menu.Items.Add("Save shadow recording", null, async (_, _) => await RequestSaveReplay());
        menu.Items.Add("Start / stop shadow recording", null, async (_, _) => await RequestShadowToggle());
        menu.Items.Add("Open captures", null, (_, _) => OpenFolder(settings.OutputDirectory)); menu.Items.Add("Quit FRAME", null, async (_, _) => await Quit());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Restore();
    }
    // Tray uses the canonical multi-size assets/Frame.ico (same file as the exe icon) at the system small-icon size.
    internal static D.Icon CreateIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Frame.ico")).Stream;
        return new D.Icon(stream, F.SystemInformation.SmallIconSize);
    }
    void Restore() { Show(); ShowInTaskbar = true; WindowState = WindowState.Normal; Activate(); }
    // Open hotkey: from the tray or minimized back to the previous size (a maximized window stays maximized); an open
    // window is only brought to the front. Never hides, minimizes or opens a second window. The WM_HOTKEY key press is
    // the user input that lets Windows give FRAME the foreground.
    WindowState lastShownState = WindowState.Normal;
    void BringToFront()
    {
        if (!IsVisible) { Show(); ShowInTaskbar = true; }
        if (WindowState == WindowState.Minimized) WindowState = lastShownState;
        MoveToPrimaryDisplay();
        Activate();
        if (!Native.ForceForeground(new WindowInteropHelper(this).Handle)) DiagnosticLog.Warning("open-hotkey", "Windows did not give FRAME the foreground");
    }
    // Open hotkey only: FRAME always comes up on Windows' primary display. A window already on it keeps its place and size;
    // otherwise it is centred in the primary work area, keeping its DIP size (Windows/WPF rescale it once on the DPI change), clamped
    // (WindowPlacement). A maximized window is restored, moved and maximized again on the primary display. Only the window
    // moves (SetWindowPos, no re-creation), so capture keeps running. Other ways of opening FRAME are unchanged.
    void MoveToPrimaryDisplay()
    {
        var hwnd = new WindowInteropHelper(this).Handle; nint primary = Native.PrimaryMonitor(), current = Native.MonitorOf(hwnd);
        if (hwnd == 0 || primary == 0 || current == primary || Native.WorkArea(primary) is not { } work) return;
        bool maximized = WindowState == WindowState.Maximized;
        if (maximized) WindowState = WindowState.Normal;
        if (!Native.GetWindowRect(hwnd, out var rect)) return;
        // Step 1: move only (size untouched) to the primary work area's corner. Crossing to a display with another scale
        // makes Windows send WM_DPICHANGED and WPF resizes the window to keep its DIP size (pre-scaling here as well would
        // scale twice: measured TV 300 % → primary 100 % ended at the minimum size).
        Native.SetWindowPos(hwnd, 0, work.X, work.Y, 0, 0, Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpNoSize);
        // Step 2, after the DPI change has been handled: clamp to the work area and centre (same display and scale now).
        Dispatcher.InvokeAsync(() => {
            if (Native.MonitorOf(hwnd) == primary && Native.GetWindowRect(hwnd, out var now))
            {
                var centred = WindowPlacement.CentreOn(now.Box, work, 1, 1);
                if (centred != now.Box) Native.SetWindowPos(hwnd, 0, centred.X, centred.Y, centred.Width, centred.Height, Native.SwpNoZOrder | Native.SwpNoActivate);
            }
            DiagnosticLog.Write("open-primary", $"moved from {rect.Box} (scale {Native.Scale(current):0.##}) to {(Native.GetWindowRect(hwnd, out var final) ? final.Box : null)} (scale {Native.Scale(primary):0.##})");
            if (maximized) WindowState = WindowState.Maximized;
        }, DispatcherPriority.Background);
    }
    // Keeps the per-user Run entry in line with the saved preference and the current exe location.
    void SyncStartup()
    {
        try { if (Environment.ProcessPath is { } exe) StartupRegistration.Apply(settings.StartWithWindows, exe); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException) { Notify("Start with Windows could not be updated: " + e.Message); DiagnosticLog.Error("startup-registration", "Start with Windows could not be updated", e); }
    }
    void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    void MaximizeClick(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    void CloseClick(object sender, RoutedEventArgs e) => Close();
    void RegisterKeys()
    {
        var handle = new WindowInteropHelper(this).Handle; foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(handle, id); hotkeys.Clear(); int i = 1; var failed = new List<string>();
        foreach (var key in settings.Hotkeys) { if (key.Key == 0) { i++; continue; } /* not set */ if (Native.RegisterHotKey(handle, i, key.Modifiers | 0x4000, key.Key)) hotkeys[i] = key.Action; else failed.Add(key.Action + " (Windows " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")"); i++; }
        hotkeyIssue = failed.Count > 0 ? "Hotkeys unavailable: " + string.Join(", ", failed) + ". Windows may have reserved or restricted these combinations." : "";
        if (hotkeyIssue.Length > 0) { Notify(hotkeyIssue); DiagnosticLog.Warning("hotkeys", hotkeyIssue); }
    }
    nint WndProc(nint hwnd, int msg, nint wp, nint lp, ref bool handled)
    {
        if (msg == 0x7E) ScheduleDisplayRefresh(); // WM_DISPLAYCHANGE (also while hidden in the tray): settled re-enumeration, DisplayRecovery.cs
        if (msg == 0x0312 && !hotkeyCaptureActive && hotkeys.TryGetValue((int)wp, out var action) && hotkeyGate.Accept(action, Environment.TickCount64))
        {
            handled = true;
            if (action == "Replay") { Dispatcher.InvokeAsync(async () => { if (!hotkeyCaptureActive) await RequestSaveReplay(); }); return 0; }
            // Open FRAME: never queued behind a running command (Act), so it works during a save, a start or a stop.
            if (action == "Open") { Dispatcher.InvokeAsync(() => { if (!hotkeyCaptureActive) BringToFront(); }); return 0; }
            // Start/stop Shadow Recording: the same command as the stone and the tray (start through Act, so a press during a
            // save/start is refused with a message; stop is queued and de-duplicated; engine not ready → explained, no start).
            if (action == "Shadow") { Dispatcher.InvokeAsync(async () => { if (!hotkeyCaptureActive && !quitting) await RequestShadowToggle(); }); return 0; }
            Dispatcher.InvokeAsync(async () => await Act(async () => {
                if (hotkeyCaptureActive) return;
                switch (action) { case "Record": await ToggleRecord(); break; case "Pause": await engine.PauseResume(); break; case "Replay": await SaveShadow(); break; case "Monitors": Restore(); ShowPage(HomePage, "Capture", ""); break; }
            }));
        }
        return 0;
    }
    // 0.6.0: the floating manual-recording toolbar ("On-screen controls": timer, pause, stop, save in a topmost window) was
    // removed with its setting. Manual recording itself is unchanged: Capture page, hotkeys, tray menu, finalized on Stop/exit.
    // Close (X, Alt+F4, taskbar Close) exits FRAME completely (0.5.5; it used to hide to the tray): the same Quit as the tray's
    // "Quit FRAME". Minimize still hides to the tray; the Open hotkey (Ctrl+Alt+F) restores from there.
    async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (quitting) return; e.Cancel = true; await Quit(); }
    // The complete drain/finalization has one deadline. A stuck action, device or export must not keep FRAME alive forever.
    // On timeout media processes are ended, recovery is retained, and an actionable note is shown on the next launch.
    bool quitRequested;
    async Task Quit()
    {
        if (quitting || quitRequested) return;
        quitRequested = true; Notify("Finalizing capture before closing…"); IsEnabled = false;
        updateDownload?.Cancel();
        timer.Stop(); displayChange?.Stop();
        TimeSpan limit = TimeSpan.FromSeconds(60);
        if (args.Contains("--data") && args.Contains("--close-test") && Array.IndexOf(args, "--shutdown-timeout-ms") is int ti and >= 0)
            limit = TimeSpan.FromMilliseconds(Math.Clamp(int.Parse(args[ti + 1]), 500, 60000));
        async Task Finish()
        {
            await actions.WaitAsync();
            if (quitting) return;
            UpdateState();
            await engine.DisposeAsync();
        }
        try
        {
            DiagnosticLog.Write("quit", $"Exit requested · Shadow {(engine.Replay ? "on" : "off")}, state {engine.State}");
            await Finish().WaitAsync(limit);
        }
        catch (Exception e)
        {
            engine.PreserveRecovery(); Backend.AbortAll();
            fault = new RecordingFault(DateTimeOffset.Now, FaultKind.Engine, "Capture needs recovery",
                "FRAME closed before capture could be finalized. Recovery files were retained.",
                "Open Settings › Storage › Recovery files to recover the unfinished recording.", e.ToString(), engine.Replay);
            faultResolved = faultDismissed = faultFromPreviousSession = false; SaveRecordingStatus();
            DiagnosticLog.Error("quit", "Shutdown could not finish; media processes ended and recovery retained", e);
        }
        finally
        {
            quitting = true;
            try
            {
                atmosphere.Dispose(); gameTracker.Dispose();
                foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, id);
                hotkeys.Clear(); tray?.Dispose(); tray = null;
            }
            catch (Exception e) { DiagnosticLog.Error("quit", "UI cleanup failed", e); }
            Application.Current.Shutdown();
        }
    }
    static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true }); }
    void FolderClick(object sender, RoutedEventArgs e) => OpenFolder(settings.OutputDirectory);
    // Settings › Storage "View capture folder": only opens Explorer; never creates, changes or touches capture. Works while recording.
    void ViewCaptureFolderClick(object sender, RoutedEventArgs e) => ViewCaptureFolder(); // main page, beside Choose capture folder
    void ViewCaptureFolder()
    {
        string folder = settings.OutputDirectory;
        try
        {
            if (!Directory.Exists(folder)) { Notify("The capture folder does not exist yet (" + folder + "). It is created with the next saved clip."); return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "\"" + folder + "\"", UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException) { Notify("The capture folder could not be opened: " + e.Message); }
    }
    void ShowPage(FrameworkElement page, string title, string kicker)
    {
        // 0.6.0: in every mode except Original GUI the Capture page is the mode's layout (CaptureHost) instead of HomePage.
        currentPage = page; bool layoutCapture = page == HomePage && activeLayout != null;
        CaptureHost.Visibility = layoutCapture ? Visibility.Visible : Visibility.Collapsed; PageScroll.Visibility = layoutCapture ? Visibility.Collapsed : Visibility.Visible;
        SceneScrim.Opacity = page == HomePage ? 0 : 1; activeScene?.PageChanged(page == HomePage);
        foreach (var (view, button) in new[] { (HomePage, CaptureNav), (LibraryPage, LibraryNav), (SettingsPage, SettingsNav) })
        {
            bool active = view == page; view.Visibility = active && !(view == HomePage && layoutCapture) ? Visibility.Visible : Visibility.Collapsed;
            button.Tag = active ? "Selected" : null;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "Current page" : "");
        }
        PageHeading.Visibility = page == HomePage ? Visibility.Collapsed : Visibility.Visible; PageTitle.Text = page == LibraryPage ? "Library" : page == SettingsPage ? "Settings" : ""; PageKicker.Text = ""; PageScroll.ScrollToTop();
        // Capture keeps the -32 scroll margin (room for its glow). Pages with a heading start the scroll area at the heading row,
        // and end above the status row, so scrolled content never runs under the title, the Apply button or the status line;
        // the content's start position is unchanged.
        bool headed = page != HomePage; PageScroll.Margin = headed ? new Thickness(-32, 0, -32, 0) : new Thickness(-32); PageContent.Margin = headed ? new Thickness(32, 0, 32, 32) : new Thickness(32);
        UpdateApplyState(); // the heading's Apply button belongs to Settings only
        if (page != LibraryPage) StopPreviews(); // previews are made only while Library is shown (LibraryView)
    }
    void HomeClick(object sender, RoutedEventArgs e) => ShowPage(HomePage, "Capture", "");
    async void LibraryClick(object sender, RoutedEventArgs e) { ShowPage(LibraryPage, "Your moments, collected.", "CAPTURE LIBRARY"); await RefreshLibrary(); }
    void SettingsClick(object sender, RoutedEventArgs e) { BuildSettings(); ShowPage(SettingsPage, "Tuned to you.", "PREFERENCES"); }
    static Button ActionButton(string title, Action action)
    {
        var b = new Button { Content = title, Margin = new Thickness(0, 0, 8, 0) };
        TooltipPolish.Describe(b, title switch {
            "Choose ffmpeg.exe" => "Browse for the installed capture engine",
            "Refresh audio devices" => "Find available playback and microphone devices",
            "Apply settings" => "Save and apply capture preferences",
            "Recovery files" => "Open retained segments from interrupted captures",
            "Diagnostics" => "Open FRAME settings and diagnostic logs",
            "Cancel · Esc" => "Keep the current shortcut unchanged",
            "Open" => "Open capture in your default viewer",
            "Crop for social" => "Make a copy formatted for a social platform (vertical, square, crop, fit or blur); nothing is uploaded",
            "Trim" => "Cut out the best moment",
            "Rename" => "Save a new capture file name",
            _ => "Set replay duration to " + title
        });
        b.Click += (_, _) => action(); return b;
    }
}
