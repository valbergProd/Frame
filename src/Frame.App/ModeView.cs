using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Frame.App;

// Visual modes (0.6.0): switching, persistence and the host side of the layout/scene contract (CaptureLayouts.cs).
// A switch is instant (no Apply, no restart): it recolours the app (ModePalettes), shows the mode's scene and Capture layout
// and saves Settings.VisualMode. Recording state, replay buffer, hotkeys, audio and capture settings are never touched, and
// the Capture/Library/Settings page that is open stays open.
public partial class MainWindow : ICaptureCommands
{
    VisualMode mode = VisualMode.Original;
    bool modeApplied;
    ICaptureLayout? activeLayout;
    IModeScene? activeScene;
    readonly Dictionary<VisualMode, ICaptureLayout> layouts = [];
    readonly Dictionary<VisualMode, IModeScene?> scenes = [];
    ModeContext? modeContext;
    FrameworkElement currentPage = null!;
    internal VisualMode Mode => mode;
    internal ICaptureLayout? ActiveLayout => activeLayout;
    internal IModeScene? ActiveScene => activeScene;

    ModeContext Context => modeContext ??= new(this, atmosphere, this, () => settings, change => {
        var next = CloneSettings(); change(next);
        try { SettingsStore.Save(configPath, next); settings = next; }
        catch (Exception e) { Notify("Settings were not saved: " + e.Message); DiagnosticLog.Error("settings", "Visual mode choice was not saved", e); }
    }) { Seed = Array.IndexOf(args, "--seed") is int i and >= 0 ? int.Parse(args[i + 1]) : null, UiRects = VisibleUiRects };

    // Visible UI (window space) except the scene/backdrop layers: controls, text, images and panels with a background.
    Rect[] VisibleUiRects()
    {
        var found = new List<Rect>(); var root = (Grid)Content;
        void Walk(DependencyObject node)
        {
            if (node is UIElement { IsVisible: false } || node == SceneHost || node == SceneScrim || node == Sidebar || node == DesertBackdrop || node == DesertShade || node == DuneFlow || node == SandDrift || node == WindowOutline) return;
            if (node is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } e && (e is Control or TextBlock or Image or System.Windows.Shapes.Shape || e is Border { Background: not null } b && b.Background != Brushes.Transparent))
            {
                try { var r = e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); if (r.Width < ActualWidth * .95 || r.Height < ActualHeight * .95) found.Add(r); } catch (InvalidOperationException) { }
                if (e is Control) return; // a control's own parts are inside its rectangle
            }
            for (int k = 0; k < VisualTreeHelper.GetChildrenCount(node); k++) Walk(VisualTreeHelper.GetChild(node, k));
        }
        Walk(root); return [.. found];
    }

    // Layout and scene per mode. 0.6.1: every mode has its own layout; Original GUI is the former Cinematic D design. The 0.5.5
    // HomePage is no longer shown: it stays hidden as the state holder the layouts read (CaptureSnapshot) and the harnesses use.
    ICaptureLayout? LayoutFor(VisualMode m)
    {
        if (!layouts.TryGetValue(m, out var layout)) layouts[m] = layout = m switch
        {
            VisualMode.Original => new CinematicCaptureLayout(Context),
            _ => new SimpleCaptureLayout(Context, m),
        };
        return layout;
    }
    IModeScene? SceneFor(VisualMode m)
    {
        if (!scenes.TryGetValue(m, out var scene)) scenes[m] = scene = m switch
        {
            VisualMode.Original => ((CinematicCaptureLayout)LayoutFor(m)!).Scene, // the layout owns the light that drives the scene
            VisualMode.Pink => new PinkScene(Context),
            VisualMode.Emo => new EmoScene(Context),
            _ => null,
        };
        return scene;
    }
    static bool DesertMode(VisualMode m) => m == VisualMode.Original;

    // --mode <key> (diagnostics): show that mode without saving it (renders and harnesses in an isolated --data folder).
    // A saved key of a former mode ("cinematic-d", 0.6.0) is rewritten as the current key at the first start (OnLoaded).
    bool StartupNeedsMigration() => Array.IndexOf(args, "--mode") < 0 && settings.VisualMode is { } saved && VisualModes.IsKnown(saved) && saved != VisualModes.Key(VisualModes.Parse(saved));
    VisualMode StartupMode() => Array.IndexOf(args, "--mode") is int i and >= 0 && i + 1 < args.Length ? VisualModes.Parse(args[i + 1]) : VisualModes.Parse(settings.VisualMode);

    internal void ApplyMode(VisualMode next, bool persist)
    {
        var previous = mode; bool changed = !modeApplied || next != previous;
        if (persist && settings.VisualMode != VisualModes.Key(next)) Context.Update(s => s.VisualMode = VisualModes.Key(next));
        mode = next; modeApplied = true;
        ModePalettes.Apply(next);
        ApplyDesertBrushes();
        // Scene (behind every page). Leaving a mode stops its scene completely before the next one starts.
        var scene = SceneFor(next);
        if (activeScene != scene)
        {
            if (activeScene != null) { atmosphere.Detach(activeScene); activeScene.Leave(); }
            SceneHost.Children.Clear(); ModeToolsHost.Content = null;
            activeScene = scene;
            if (scene != null) { SceneHost.Children.Add(scene.Root); ModeToolsHost.Content = scene.Tools; scene.Enter(); atmosphere.Attach(scene); }
        }
        bool desert = DesertMode(next);
        DesertBackdrop.Visibility = DesertShade.Visibility = DuneFlow.Visibility = desert ? Visibility.Visible : Visibility.Collapsed;
        atmosphere.Desert = desert;
        // Capture layout. Shared status elements follow the visible layout (re-parented), so there is exactly one of each.
        var layout = LayoutFor(next);
        if (activeLayout != layout)
        {
            activeLayout?.Hidden();
            if (activeLayout?.HeroHost != null) activeLayout.HeroHost.Content = null;
            CaptureHost.Children.Clear();
            activeLayout = layout;
            if (layout != null)
            {
                CaptureHost.Children.Add(layout.Root);
                if (layout.HeroHost != null) layout.HeroHost.Content = scene?.Hero;
            }
            MoveSharedElements(layout);
        }
        else if (layout?.HeroHost != null) layout.HeroHost.Content = scene?.Hero;
        UpdateModeChrome();
        atmosphere.Enable(VisualModes.Decorative(next));
        double offset = PageScroll.VerticalOffset; ShowPage(currentPage ?? HomePage, "", ""); PageScroll.ScrollToVerticalOffset(offset); // Settings/Library keep their place
        DrawMonitors(); UpdateFailurePanel(); UpdateApplyState(); FitRecordButton();
        if (engineProblem != null) ShowEngineProblem(engineProblem); // its panel is built with the palette of the moment
        UpdateState();
        if (changed && modeApplied) DiagnosticLog.Write("visual-mode", $"{VisualModes.Name(previous)} → {VisualModes.Name(next)}{(persist ? "" : " (not saved)")}");
    }
    // Original GUI / Cinematic D brushes of the 0.5.5 Capture page (were SettingsView.ApplyTheme): always the dark desert look.
    void ApplyDesertBrushes()
    {
        var activeFace = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B078391C")); activeFace.Freeze();
        Resources["ActiveCaptureFace"] = activeFace;
        var hint = new SolidColorBrush(((SolidColorBrush)FindResource("Raised")).Color) { Opacity = .5 }; hint.Freeze(); ReplaySaveButton.Background = hint;
        var face = new SolidColorBrush(((SolidColorBrush)FindResource("Raised")).Color) { Opacity = .32 }; face.Freeze(); RecordButton.Background = face;
        var edge = new SolidColorBrush(((SolidColorBrush)FindResource("Line")).Color) { Opacity = .85 }; edge.Freeze(); RecordButton.BorderBrush = edge;
        DesertShade.Opacity = 1; DesertBackdrop.Opacity = .88;
        HistorySlider.ValueBrush = HistorySliderControl.WarmGrey; HistorySlider.CaptionText = HistorySliderControl.DarkCaption; HistorySlider.InvalidateVisual();
        TitleBarSurface.Background = Brushes.Transparent; TitleBarEdge.Fill = Brushes.Transparent;
    }
    // Title bar (0.6.1): all five mode names side by side left of the caption buttons; a click activates that mode at once.
    // The active one is marked only by brighter text (Tag "Active", ModeLink style) and its automation item status.
    readonly Dictionary<VisualMode, Button> modeLinks = [];
    internal IReadOnlyDictionary<VisualMode, Button> ModeLinkButtons => modeLinks;
    void BuildModeLinks()
    {
        foreach (var m in VisualModes.All)
        {
            // The label follows the button's Foreground (Muted, Text when active or hovered); a plain string would get the app's
            // implicit TextBlock style (always Text) and hide the active marking.
            var link = new Button { Style = (Style)FindResource("ModeLink") }; var label = new TextBlock { Text = VisualModes.Name(m) };
            label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = link }); link.Content = label;
            System.Windows.Automation.AutomationProperties.SetName(link, VisualModes.Name(m));
            link.Click += (_, _) => { if (mode != m || !modeApplied) ApplyMode(m, persist: true); };
            modeLinks[m] = link; ModeLinks.Children.Add(link);
        }
    }
    void UpdateModeChrome()
    {
        if (modeLinks.Count == 0) BuildModeLinks();
        foreach (var (m, link) in modeLinks)
        {
            link.Tag = m == mode ? "Active" : null;
            System.Windows.Automation.AutomationProperties.SetItemStatus(link, m == mode ? "Current mode" : "");
        }
        foreach (var nav in new[] { CaptureNav, LibraryNav, SettingsNav }) nav.Style = (Style)FindResource("ModeNavigationButton");
        Sidebar.Background = (Brush)FindResource("SidebarBackground");
    }

    // Shared status elements: their 0.5.5 places in HomePage are remembered once; a layout gets them in its hosts.
    (Panel Parent, int Index)[]? homeSlots;
    FrameworkElement[] SharedNotes => [FailurePanel, EnginePanel, UnexpectedExitNote, ReplayInfo];
    void MoveSharedElements(ICaptureLayout? layout)
    {
        var shared = SharedNotes.Append(MonitorCanvas).ToArray();
        homeSlots ??= shared.Select(e => ((Panel)e.Parent, ((Panel)e.Parent).Children.IndexOf(e))).ToArray();
        foreach (var e in shared) Detach(e);
        if (layout == null)
        {
            for (int i = 0; i < shared.Length; i++) { var (parent, index) = homeSlots[i]; parent.Children.Insert(Math.Min(index, parent.Children.Count), shared[i]); }
            return;
        }
        foreach (var note in SharedNotes) layout.NotesHost.Children.Add(note);
        layout.DisplayHost.Child = MonitorCanvas;
    }
    static void Detach(FrameworkElement e)
    {
        switch (e.Parent) { case Panel p: p.Children.Remove(e); break; case Decorator d: d.Child = null; break; case ContentControl c: c.Content = null; break; }
    }

    // Everything a layout shows, read from the Original controls that UpdateState keeps current (one source of truth).
    internal CaptureView CaptureSnapshot()
    {
        int seconds = settings.ReplaySeconds;
        string duration = seconds < 60 ? $"{seconds} seconds" : $"{seconds / 60d:0.##} " + (seconds == 60 ? "minute" : "minutes");
        return new CaptureView
        {
            Ready = ready, ShadowTitle = ShadowTitle.Text, ShadowActive = ShadowButton.Tag as string == "Active", ShadowDot = ShadowDotState,
            ShadowEnabled = ShadowButton.IsEnabled, ShadowTooltip = ShadowButton.ToolTip as string ?? "",
            ManualActive = engine.State is RecordingState.Recording or RecordingState.Paused, ManualPaused = engine.State == RecordingState.Paused,
            ManualEnabled = RecordButton.IsEnabled, ManualTitle = RecordTitle.Text, ManualTooltip = RecordButton.ToolTip as string ?? "", ManualElapsed = RecordTimer.Text, PauseLabel = PauseButton.Content as string ?? "Pause",
            ReplaySeconds = seconds, ReplayChoices = HistoryDial.Values(seconds), ReplayLengthEnabled = HistorySlider.IsEnabled,
            ReplayLengthTooltip = engine.Replay ? "Change anytime: saves use the new length at once; a longer history fills up as Shadow Recording continues" : "How much recent footage Shadow Recording keeps. Arrow keys, Home and End adjust",
            Duration = duration, SaveTitle = ReplaySaveTitle.Text, SaveEnabled = ReplaySaveButton.IsEnabled, SaveTooltip = ReplaySaveButton.ToolTip as string ?? "",
            ShadowHotkey = ShadowHotkeyText.Text, SaveHotkey = SaveHotkeyText.Text, SaveHotkeyLabel = SaveHotkeyLabel.Text,
            PresetIndex = Math.Max(0, PresetCombo.SelectedIndex), PresetEnabled = PresetCombo.IsEnabled, PresetTooltip = PresetCombo.ToolTip as string ?? "",
            DesktopAudio = DesktopCheck.IsChecked == true, Microphone = MicCheck.IsChecked == true, AudioEnabled = DesktopCheck.IsEnabled,
            SourceTitle = SourceExpander.Header as string ?? "", SourceDetail = SourceLabel.Text, SourceLocked = cardsLocked, StatusText = StatusBadge.Text,
        };
    }
    void PushCaptureView() { if (activeLayout != null && modeApplied) activeLayout.Update(CaptureSnapshot()); }

    Task ICaptureCommands.ToggleShadow() => RequestShadowToggle();
    Task ICaptureCommands.ToggleManual() => Act(ToggleRecord);
    Task ICaptureCommands.PauseResume() => Act(engine.PauseResume);
    Task ICaptureCommands.SaveReplay() => RequestSaveReplay();
    void ICaptureCommands.SetReplaySeconds(int seconds) => CommitHistory(seconds);
    void ICaptureCommands.SetPreset(int index) { if (index is >= 0 and <= 2 && PresetCombo.IsEnabled) PresetCombo.SelectedIndex = index; PushCaptureView(); }
    void ICaptureCommands.SetAudio(bool desktop, bool microphone) { if (!DesktopCheck.IsEnabled) { PushCaptureView(); return; } DesktopCheck.IsChecked = desktop; MicCheck.IsChecked = microphone; AudioChanged(this, new RoutedEventArgs()); PushCaptureView(); }
    void ICaptureCommands.ChangeHotkey(string action) => ShowHotkeys(action, edit: true);
    void ICaptureCommands.ChooseCaptureFolder() => CaptureFolderClick(this, new RoutedEventArgs());
    void ICaptureCommands.ViewCaptureFolder() => ViewCaptureFolder();
}
