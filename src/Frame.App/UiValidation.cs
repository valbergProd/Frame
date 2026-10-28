using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Frame.App;

public partial class MainWindow
{
    async Task ValidateDesertUi(string path)
    {
        var results = new List<object>();
        void Check(string name, bool success) { results.Add(new { name, passed = success }); if (!success) throw new InvalidOperationException(name); }
        try
        {
            // Never run against real settings: the test data folder must have loaded (else defaults point at the user's Videos\FRAME).
            Check("Isolated test settings loaded", Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase));
            Check("Idle Save disabled; manual recording control present", !ReplaySaveButton.IsEnabled && !engine.Busy && RecordButton.Visibility == Visibility.Visible);
            bool wasReady = ready, wasChecking = checkingEngine;
            ready = false; checkingEngine = true; loadingCharacters = 0; UpdateState();
            Check("Full loading label starts empty with disabled shadow action", !ShadowButton.IsEnabled && ShadowLoadingText.Visibility == Visibility.Visible && ShadowLoadingText.Text == "");
            await Task.Delay(180);
            Check("Engine label reveals progressively", ShadowLoadingText.Text.Length > 0 && ShadowLoadingText.Text.Length < EngineLoadingLabel.Length);
            loadingCharacters = EngineLoadingLabel.Length; UpdateLoadingText();
            // The full label pauses ~0.7 s, then types again; wait for the wrap (up to 3 s: timer ticks share the UI thread with the atmosphere frames).
            for (int i = 0; i < 60 && ShadowLoadingText.Text.Length >= EngineLoadingLabel.Length; i++) await Task.Delay(50);
            Check("Full loading label loops while engine is pending", ShadowLoadingText.Text.Length < EngineLoadingLabel.Length && loadingTimer.IsEnabled);
            Message.Text = "Loading-state appearance preview."; SaveRender(Path.ChangeExtension(path, ".loading.png"));
            checkingEngine = false; UpdateState();
            Check("Failed engine check is not shown as perpetual loading", !ShadowButton.IsEnabled && ShadowLoadingText.Text.Contains("unavailable") && !loadingTimer.IsEnabled);
            ready = wasReady; checkingEngine = wasChecking; UpdateState();
            Check("Engine-ready state removes loading text", ready && ShadowButton.IsEnabled && ShadowLoadingText.Visibility == Visibility.Collapsed && !loadingTimer.IsEnabled);
            Check("No sound-cue, cue-volume or Reduce visual motion setting remains (0.5.5)", typeof(Settings).GetProperty("Sound") == null && typeof(Settings).GetProperty("Volume") == null && typeof(Settings).GetProperty("ReduceMotion") == null);
            Check("Five minute initial dial and configured default label", HistorySlider.Seconds == settings.ReplaySeconds && ReplaySaveTitle.Text == "Save last 5 minutes" && SaveHotkeyText.Text == FormatHotkey(settings.Hotkeys.First(h => h.Action == "Replay")) && SaveHotkeyLabel.Text == "Hotkey for saving the 5 minutes");
            Check("Capture shows both shortcut rows: Shadow on/off (not set by default) and save, each with Change hotkey", ShadowHotkeyText.Text == FormatHotkey(settings.Hotkeys.First(h => h.Action == "Shadow")) && ChangeShadowHotkeyButton.Content as string == "Change hotkey" && ChangeHotkeyButton.Content as string == "Change hotkey" && ReferenceEquals(ChangeShadowHotkeyButton.Style, ChangeHotkeyButton.Style) && Grid.GetRow(ChangeShadowHotkeyButton) == 0 && Grid.GetRow(ChangeHotkeyButton) == 1);
            ChangeShadowHotkeyClick(this, new RoutedEventArgs()); bool editingShadow = editingHotkey?.Tag is Hotkey { Action: "Shadow" } && SettingsPage.Visibility == Visibility.Visible && hotkeyCaptureActive;
            EndHotkeyEdit(); ShowPage(HomePage, "Capture", ""); UpdateLayout();
            Check("Capture › Change hotkey (Shadow) opens Settings › Hotkeys editing the same setting; Esc-style cancel restores", editingShadow && !hotkeyCaptureActive && HomePage.Visibility == Visibility.Visible);
            var peer = UIElementAutomationPeer.CreatePeerForElement(HistorySlider)!;
            var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
            int original = settings.ReplaySeconds;
            range.SetValue(600); Check("Accessible dial commits and persists", settings.ReplaySeconds == 600 && SettingsStore.Load(configPath).ReplaySeconds == 600 && ReplaySaveTitle.Text.Contains("10 min") && SaveHotkeyLabel.Text == "Hotkey for saving the 10 minutes");
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(HistorySlider), 0, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent };
            HistorySlider.RaiseEvent(input); Check("Arrow key decrements and commits", settings.ReplaySeconds == 300 && input.Handled);
            HistorySlider.IsEnabled = false;
            bool rejected = false; try { range.SetValue(900); } catch (InvalidOperationException) { rejected = true; }
            Check("Read-only dial rejects automation during capture lock", rejected && range.IsReadOnly && settings.ReplaySeconds == 300);
            HistorySlider.IsEnabled = true;
            UpdateLayout();
            HistorySlider.BeginDrag(HistorySlider.PointFor(3)); Check("Mouse capture acquired for slider drag", HistorySlider.IsDragging);
            HistorySlider.MoveDrag(HistorySlider.PointFor(4)); Check("Drag previews without persisting", HistorySlider.Seconds == 600 && settings.ReplaySeconds == 300);
            HistorySlider.FinishDrag(); Check("Mouse release commits and releases capture", !HistorySlider.IsDragging && settings.ReplaySeconds == 600 && SettingsStore.Load(configPath).ReplaySeconds == 600);
            range.SetValue(60); HistorySlider.BeginDrag(HistorySlider.PointFor(0)); HistorySlider.MoveDrag(HistorySlider.PointFor(0) + new Vector(0, 60));
            Check("Dragging below the minimum stays at the minimum", HistorySlider.Seconds == 60 && settings.ReplaySeconds == 60);
            HistorySlider.FinishDrag(); range.SetValue(300);
            HistorySlider.BeginDrag(HistorySlider.PointFor(4));
            var cancel = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(HistorySlider), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent };
            HistorySlider.RaiseEvent(cancel);
            Check("Escape cancels drag and restores committed history", !HistorySlider.IsDragging && HistorySlider.Seconds == 300 && settings.ReplaySeconds == 300 && cancel.Handled);
            range.SetValue(300);
            // An exclusive settings-file lock simulates persistence failure without touching user data.
            using (var locked = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.None)) range.SetValue(900);
            Check("Failed persistence rolls back both dial and save label", settings.ReplaySeconds == 300 && HistorySlider.Seconds == 300 && ReplaySaveTitle.Text.Contains("5 min") && SaveHotkeyLabel.Text == "Hotkey for saving the 5 minutes");
            CommitHistory(original);
            ShowPage(LibraryPage, "", ""); Check("Library navigation state", LibraryNav.Tag as string == "Selected" && HomePage.Visibility == Visibility.Collapsed);
            ShowPage(SettingsPage, "", ""); Check("Settings navigation state", SettingsNav.Tag as string == "Selected");
            ShowHotkeys(); UpdateLayout();
            // Hotkeys, Start-up, Games and Storage are open sections (19 px headings); Recording, General and Advanced are collapsible (QuietExpander).
            // Rows are read from the logical tree (Expander.Content), so collapsed sections are searched too.
            Expander SettingsSection(string header) => SettingsPage.Children.OfType<Expander>().Single(x => x.Header as string == header);
            Grid[] SettingsRows(string header) => ((Panel)SettingsSection(header).Content).Children.OfType<Grid>().ToArray();
            static string RowTitle(Grid g) => ((StackPanel)g.Children[0]).Children[0] switch { TextBlock t => t.Text, StackPanel line => ((TextBlock)line.Children[0]).Text, _ => "" }; // a title with an info icon is a row of its own
            var headings = SettingsPage.Children.OfType<TextBlock>().Where(t => t.FontSize == 19).Select(t => t.Text).ToArray();
            var sections = SettingsPage.Children.OfType<Expander>().ToArray();
            Check("Change hotkey opens Settings with Hotkeys first", SettingsPage.Visibility == Visibility.Visible && PageScroll.VerticalOffset == 0 && headings.SequenceEqual(["Hotkeys", "Start-up", "Storage"]) && SettingsPage.Children.IndexOf(sections[0]) > SettingsPage.Children.IndexOf(SettingsPage.Children.OfType<TextBlock>().First(t => t.Text == "Storage")));
            Check("Recording, General and Advanced are collapsible sections, collapsed by default", sections.Select(x => x.Header as string).SequenceEqual(["Recording", "General", "Advanced settings"]) && sections.All(x => !x.IsExpanded && x.Style == FindResource("QuietExpander") && x.Tag is string { Length: > 0 }));
            Check("Recording and General keep their rows in order", SettingsRows("Recording").Select(RowTitle).SequenceEqual(["Frame rate", "Output resolution", "Desktop audio source", "Microphone source", "Audio devices", "Video bitrate", "Replay duration", "Replay disk budget", "Capture"]) && SettingsRows("General").Select(RowTitle).SequenceEqual(["Notifications"]));
            {
                // Start-up and Games sit directly below Hotkeys (before Storage) with their original toggles; hotkeys are shown
                // with Save shadow recording first, then manual recording, without changing the stored order.
                int Index(string heading) => SettingsPage.Children.IndexOf(SettingsPage.Children.OfType<TextBlock>().First(x => x.Text == heading));
                var toggles = SettingsPage.Children.OfType<Panel>().SelectMany(x => x.Children.OfType<CheckBox>()).Concat(SettingsPage.Children.OfType<CheckBox>()).Select(c => ((TextBlock)c.Content).Text).ToArray();
                var startupItems = SettingsPage.Children.OfType<StackPanel>().First(x => x.Children.OfType<CheckBox>().Any()).Children.OfType<CheckBox>().Select(c => ((TextBlock)c.Content).Text).ToArray();
                Check("Start-up comes directly after Hotkeys with all three toggles in order; no Games section", Index("Hotkeys") < Index("Start-up") && Index("Start-up") < Index("Storage") && !SettingsPage.Children.OfType<TextBlock>().Any(x => x.Text == "Games") && startupItems.SequenceEqual(["Start FRAME with Windows (opens quietly in the tray)", "Start shadow recording when FRAME starts", "Minimize FRAME when a game starts"]) && toggles.Length > 0);
                var hotkeyRows = SettingsPage.Children.OfType<Grid>().Take(hotkeyButtons.Count).Select(RowTitle).ToArray();
                Check("Save shadow recording is the first hotkey, Shadow on/off next, manual recording below; stored order unchanged", hotkeyRows.Take(4).SequenceEqual(["Save shadow recording", "Start / stop shadow recording", "Start / stop manual recording", "Pause / resume recording"]) && SettingsStore.Load(configPath).Hotkeys.Select(h => h.Action).SequenceEqual(settings.Hotkeys.Select(h => h.Action)));
            }
            {
                // Chevron colour follows IsExpanded only (no hover/focus colour): expanded = Accent, collapsed = Muted.
                var general = SettingsSection("General"); general.ApplyTemplate(); var toggle = (System.Windows.Controls.Primitives.ToggleButton)general.Template.FindName("Toggle", general); toggle.ApplyTemplate();
                var chevron = (System.Windows.Shapes.Path)toggle.Template.FindName("Chevron", toggle);
                bool wasOpen = general.IsExpanded; general.IsExpanded = true; UpdateLayout(); bool amber = ReferenceEquals(chevron.Stroke, FindResource("Accent"));
                toggle.Focus(); general.IsExpanded = false; UpdateLayout(); bool grey = ReferenceEquals(chevron.Stroke, FindResource("Muted"));
                general.IsExpanded = wasOpen; UpdateLayout();
                Check("Expander chevron: amber when expanded, grey when collapsed (also while focused)", amber && grey && ReferenceEquals(toggle.FocusVisualStyle, FindResource("QuietFocus")));
            }
            var advancedSection = sections[^1];
            Check("Advanced settings is last, collapsed, and holds FFmpeg and encoder", !advancedSection.IsExpanded && SettingsPage.Children.IndexOf(advancedSection) > SettingsPage.Children.IndexOf(SettingsSection("General")) && SettingsRows("Advanced settings").Select(RowTitle).SequenceEqual(["FFmpeg", "Video encoder", "Diagnostics"]));
            Check("Every hotkey is listed once and no screenshot shortcut returns", hotkeyButtons.Count == settings.Hotkeys.Length && hotkeyButtons.All(b => b.Tag is Hotkey h && h.Action != "Screenshot"));
            foreach (var x in sections) x.IsExpanded = true;
            UpdateLayout(); await Task.Delay(300); SaveElement(SettingsPage, Path.ChangeExtension(path, ".settings-full.png")); // after the body fade-in
            foreach (var x in sections) x.IsExpanded = false;
            SettingsSection("Recording").IsExpanded = true; BuildSettings(); var reopened = SettingsPage.Children.OfType<Expander>().ToArray();
            Check("Open sections are remembered across Settings rebuilds", reopened[0] != sections[0] && reopened[0].IsExpanded && !reopened[1].IsExpanded && !reopened[2].IsExpanded);
            reopened[0].IsExpanded = false; BuildSettings(); Check("Collapsed sections stay collapsed after a rebuild", SettingsPage.Children.OfType<Expander>().All(x => !x.IsExpanded));
            // The icon has 16/20/24/32/40/48… px frames; at e.g. 175 % scaling Windows asks for 28 px and the nearest frame is used.
            using (var trayIcon = CreateIcon()) Check("Tray icon loads the shared brand icon at (or nearest to) small-icon size", trayIcon.Width > 0 && Math.Abs(trayIcon.Width - System.Windows.Forms.SystemInformation.SmallIconSize.Width) <= 4);
            Check("Storage shows the capture folder", SettingsPage.Children.OfType<Grid>().Any(g => RowTitle(g) == "Capture folder"));
            {
                // Smart Apply: disabled until an Apply-only value differs from the saved one (also inside collapsed sections);
                // reverting disables it; page rebuilds (navigation) and expand/collapse keep pending values; a failed Apply keeps them.
                Button ApplyBtn() => ApplySettingsButton; bool Shown() => ApplySettingsButton.Visibility == Visibility.Visible && ApplySettingsButton.IsEnabled;
                TextBox Bitrate() => (TextBox)SettingsRows("Recording").First(g => RowTitle(g) == "Video bitrate").Children[1];
                CheckBox ToolbarToggle() => (CheckBox)SettingsRows("General").First(g => RowTitle(g) == "Notifications").Children[1]; // 0.6.0: was On-screen controls
                BuildSettings(); string bitrateSaved = Bitrate().Text; bool cleanStart = !Shown();
                Bitrate().Text = bitrateSaved == "20" ? "21" : "20"; bool dirty = Shown() && ReferenceEquals(ApplyBtn().BorderBrush, FindResource("Accent")) && SettingsPage.Children.OfType<Expander>().All(x => !x.IsExpanded);
                Bitrate().Text = bitrateSaved; bool reverted = !Shown();
                Check("Smart Apply: hidden when clean, shown (accent) by a change in a collapsed section, hidden again on revert", cleanStart && dirty && reverted);
                {
                    // Fixed in the heading row: outside the ScrollViewer, same position after scrolling, not shown on other pages.
                    Bitrate().Text = bitrateSaved == "20" ? "21" : "20"; UpdateLayout();
                    var applyBefore = ApplySettingsButton.TranslatePoint(new Point(), this); PageScroll.ScrollToVerticalOffset(400); UpdateLayout(); await Task.Delay(50);
                    var applyAfter = ApplySettingsButton.TranslatePoint(new Point(), this); bool scrolled = PageScroll.VerticalOffset > 0;
                    var title = PageTitle.TranslatePoint(new Point(0, PageTitle.ActualHeight / 2), this).Y; var middle = ApplySettingsButton.TranslatePoint(new Point(0, ApplySettingsButton.ActualHeight / 2), this).Y;
                    var bar = Descendants<System.Windows.Controls.Primitives.ScrollBar>(PageScroll).First(x => x.Orientation == Orientation.Vertical);
                    double gap = bar.TranslatePoint(new Point(), this).X - ApplySettingsButton.TranslatePoint(new Point(ApplySettingsButton.ActualWidth, 0), this).X;
                    Check("Apply button: far right of the heading row, 12–20 px before the scrollbar, fixed while Settings scrolls" + $" (gap {gap:0.#})", Shown() && scrolled && applyBefore == applyAfter && !PageScroll.IsAncestorOf(ApplySettingsButton) && Math.Abs(title - middle) <= 3 && gap is >= 12 and <= 20 && bar.Visibility == Visibility.Visible);
                    ShowPage(LibraryPage, "", ""); bool hiddenElsewhere = ApplySettingsButton.Visibility != Visibility.Visible; ShowPage(SettingsPage, "", ""); bool back = Shown();
                    Check("Apply button is hidden on other pages and returns on Settings while changes are pending", hiddenElsewhere && back);
                    Bitrate().Text = bitrateSaved; PageScroll.ScrollToTop(); UpdateLayout();
                }
                ToolbarToggle().IsChecked = !settings.ShowSaveNotifications;
                ShowPage(HomePage, "Capture", ""); SettingsClick(this, new RoutedEventArgs()); UpdateLayout();
                SettingsSection("General").IsExpanded = true; SettingsSection("General").IsExpanded = false;
                bool kept = Shown() && ToolbarToggle().IsChecked == !settings.ShowSaveNotifications;
                ToolbarToggle().IsChecked = settings.ShowSaveNotifications; bool revertedAfterRebuild = !Shown();
                Check("Smart Apply: navigation and expand/collapse keep the pending value and dirty state", kept && revertedAfterRebuild);
                Bitrate().Text = "1"; var savedBitrate = SettingsStore.Load(configPath).BitrateMbps;
                ApplyBtn().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                for (int i = 0; i < 100 && actions.CurrentCount == 0; i++) await Task.Delay(50);
                { bool en = Shown(), kept1 = Bitrate().Text == "1", fileSame = SettingsStore.Load(configPath).BitrateMbps == savedBitrate, onPage = SettingsPage.Visibility == Visibility.Visible; Check("Smart Apply: a failed Apply (invalid bitrate) keeps the pending value and the enabled button" + (en && kept1 && fileSame && onPage ? "" : $" [enabled {en}, value kept {kept1}, file unchanged {fileSame}, on page {onPage}, text '{Bitrate().Text}']"), en && kept1 && fileSame && onPage); }
                Bitrate().Text = bitrateSaved; Check("Smart Apply: reverting after the failure hides Apply", !Shown());
            }
            {
                // Apply reads controls inside collapsed sections: change one value in collapsed Recording and General, apply, compare
                // the saved file. Hotkeys are cleared for this apply so the test registers no global hotkey; settings restored after.
                var applyBase = CloneSettings(); var bitrateBox = (TextBox)SettingsRows("Recording").First(g => RowTitle(g) == "Video bitrate").Children[1];
                var toolbarBox = (CheckBox)SettingsRows("General").First(g => RowTitle(g) == "Notifications").Children[1];
                int bitrateNext = applyBase.BitrateMbps == 20 ? 21 : 20; bitrateBox.Text = bitrateNext.ToString(); toolbarBox.IsChecked = !applyBase.ShowSaveNotifications;
                foreach (var b in hotkeyButtons) b.Tag = ((Hotkey)b.Tag) with { Modifiers = 0, Key = 0 };
                var applyButton = ApplySettingsButton;
                bool collapsedWhenApplied = SettingsPage.Children.OfType<Expander>().All(x => !x.IsExpanded);
                applyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                for (int i = 0; i < 300 && actions.CurrentCount == 0; i++) await Task.Delay(100);
                var expected = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(applyBase))!; expected.BitrateMbps = bitrateNext; expected.ShowSaveNotifications = !applyBase.ShowSaveNotifications; expected.Onboarded = true;
                expected.OutputDevice ??= ""; expected.InputDevice ??= ""; expected.Hotkeys = applyBase.Hotkeys.Select(h => h with { Modifiers = 0, Key = 0 }).ToArray();
                Check("Apply reads values from collapsed sections", collapsedWhenApplied && JsonSerializer.Serialize(SettingsStore.Load(configPath)) == JsonSerializer.Serialize(expected));
                Check("Smart Apply: successful Apply hides the button", ApplySettingsButton.Visibility != Visibility.Visible && !ApplySettingsButton.IsEnabled);
                Check("Apply stays on Settings (no jump to Capture, 0.5.5)", SettingsPage.Visibility == Visibility.Visible && HomePage.Visibility != Visibility.Visible && SettingsNav.Tag as string == "Selected");
                SettingsStore.Save(configPath, applyBase); settings = applyBase; SyncPreset(); SyncDuration(); BuildSettings();
            }
            Check("Title bar has no brand mark; main logo is enlarged", !Descendants<Viewbox>(TitleBarSurface).Any() && Descendants<Viewbox>((Grid)Content).Any(v => v.Width == 29));
            Check("Idle history hint removed; the note slot keeps its height when empty", ReplayInfo.Visibility != Visibility.Collapsed && !ReplayInfo.Text.Contains("Start to build your history") && ReplayInfo.Height == 30);
            Check("Display options use the labelled circular toggle", SourceExpander.Style == FindResource("QuietExpander") && SourceExpander.Tag as string == "Display settings");
            string oldFolder = settings.OutputDirectory, newFolder = Path.Combine(data, "folder-choice-test");
            CommitCaptureFolder(newFolder); Check("Home folder choice persists immediately", settings.OutputDirectory == newFolder && SettingsStore.Load(configPath).OutputDirectory == newFolder && !Directory.EnumerateFiles(newFolder).Any());
            bool folderFailed = false;
            using (var locked = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.None)) { try { CommitCaptureFolder(oldFolder); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { folderFailed = true; } }
            Check("Failed folder persistence keeps previous destination", folderFailed && settings.OutputDirectory == newFolder);
            CommitCaptureFolder(oldFolder);
            ShowPage(HomePage, "", "");
            UpdateRecordingPresentation(true, RecordingState.Recording); ShadowTitle.Text = "Stop shadow recording";
            Check("Both recording actions expose active glow and stop labels", ShadowButton.Tag as string == "Active" && RecordButton.Tag as string == "Active" && RecordTitle.Text == "Stop manual recording");
            Message.Text = "Active button appearance preview · no capture started.";
            SaveRender(Path.ChangeExtension(path, ".active.png"));
            UpdateRecordingPresentation(false, RecordingState.Paused);
            Check("Paused manual recording retains stop action without active glow", RecordButton.Tag == null && RecordTitle.Text == "Stop manual recording");
            UpdateState();
            var shownMode = mode; ApplyMode(VisualMode.Light, persist: false); UpdateLayout();
            var fg = ((SolidColorBrush)FindResource("Text")).Color; Check("Light mode uses dark readable text", fg.R < 100 && fg.G < 100);
            UpdateRecordingPresentation(true, RecordingState.Recording); ShadowTitle.Text = "Stop shadow recording"; PushCaptureView();
            SaveRender(Path.ChangeExtension(path, ".active-light.png")); UpdateState();
            ApplyMode(shownMode, persist: false);
            // 0.5.5: Close exits FRAME (checked in its own process by --close-test); Minimize is the way to the tray.
            SetupTray(); Message.Text = "Before hide"; WindowState = WindowState.Minimized;
            Check("Minimize hides to the tray without reminder and keeps the engine alive", !IsVisible && tray != null && Message.Text == "Before hide" && !quitting);
            Restore(); tray!.Dispose(); tray = null;
            atmosphere.Enable(true);
            Activate(); int beforeMotion = atmosphere.Updates; await Task.Delay(1100);
            // Windows may refuse foreground to a test process (e.g. no recent user input); the result name then says focus was not checked.
            bool focused = IsActive;
            Check("Opt-in atmosphere runs while visible" + (focused ? " and focused" : " (focus NOT checked: Windows refused foreground)"), atmosphere.Running && atmosphere.Updates > beforeMotion);
            UpdateRecordingPresentation(true, RecordingState.Recording); atmosphere.Enable(true);
            Check("Recording presentation retains sand and dune animation", atmosphere.Running && atmosphere.SandMoving && SandDrift.Visibility == Visibility.Visible && DuneFlow.Visibility == Visibility.Visible);
            ShadowButton.ApplyTemplate(); RecordButton.ApplyTemplate();
            // Since the obsidian Shadow button (0.5.4) only the manual Record button has a RecordingGlow; the stone uses StoneBreath.
            var manualGlow = (RecordingGlow)RecordButton.Template.FindName("Glow", RecordButton);
            double previousGlow = manualGlow.Intensity;
            int pulseBefore = atmosphere.Updates; await Task.Delay(600);
            Check("Active glow timer advances with sand", atmosphere.Updates > pulseBefore);
            Check("Active manual-recording glow visibly pulses", manualGlow.Visibility == Visibility.Visible && Math.Abs(manualGlow.Intensity - previousGlow) > .05);
            var focusProbe = new Window { Width = 60, Height = 60, Left = -19000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
            try { focusProbe.Show(); focusProbe.Activate(); await Task.Delay(180); Check("Visible effects continue without focus", !IsActive && atmosphere.Running); }
            finally { focusProbe.Close(); Activate(); }
            ShadowTitle.Text = "Stop shadow recording"; Message.Text = "Active appearance preview; no capture started.";
            SaveRender(Path.ChangeExtension(path, ".pulse.png"));
            manualGlow.Intensity = 1; SaveRender(Path.ChangeExtension(path, ".glow-high.png"));
            manualGlow.Intensity = .3; SaveRender(Path.ChangeExtension(path, ".glow-low.png"));
            UpdateRecordingPresentation(false, RecordingState.Idle); atmosphere.Enable(true);
            await Task.Delay(600); SaveRender(Path.ChangeExtension(path, ".sand.png"));
            for (int i = 0; i < 3; i++) { Hide(); Check("Hidden atmosphere timer stopped " + i, !atmosphere.Running); Show(); }
            Hide(); int hiddenTicks = atmosphere.Updates; await Task.Delay(600); Check("No decorative updates while hidden", atmosphere.Updates == hiddenTicks); Show();
            int restoredTicks = atmosphere.Updates; await Task.Delay(1000); int rate = atmosphere.Updates - restoredTicks;
            Check("Repeated hide/show keeps one animation loop (about 30 steps/s, never more than 40)", rate is > 0 and <= 40);
            WindowState = WindowState.Minimized; int minimizedTicks = atmosphere.Updates; await Task.Delay(400); Check("Minimized atmosphere stopped", !atmosphere.Running && atmosphere.Updates == minimizedTicks); WindowState = WindowState.Normal;
            atmosphere.Enable(false); Check("Reduce motion stops source timer", !atmosphere.Running);
            int ticks = atmosphere.Updates; await Task.Delay(1100); Check("No decorative updates while stopped", atmosphere.Updates == ticks);
            // Share: a real synthetic recording in the isolated capture folder; Share appears in Library and the modal reads it.
            string clip = Path.Combine(settings.OutputDirectory, "FRAME ui share.mp4");
            var made = await Backend.Run(Backend.Resolve(settings.FfmpegPath), ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-f", "lavfi", "-i", "sine=sample_rate=48000", "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", clip]);
            Check("Synthetic recording created for Share", made.Code == 0 && File.Exists(clip));
            ShowPage(LibraryPage, "", ""); await RefreshLibrary(); UpdateLayout();
            Check("Library offers Crop for social for videos", LibraryList.Children.OfType<Border>().Select(b => (Grid)b.Child).Any(r => r.Children.OfType<StackPanel>().Any(p => p.Children.OfType<Button>().Any(b => b.Content as string == "Crop for social"))));
            var share = OpenShare(clip, false)!; share.Left = -20000; share.ShowInTaskbar = false;
            await Task.Delay(100); await share.Ready;
            Check("Crop for social uses the FRAME title bar (chrome, caption buttons, subtitle)", share.WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(share) is { CaptionHeight: 36 } && Descendants<Button>((FrameworkElement)share.Content).Count(b => System.Windows.Automation.AutomationProperties.GetName(b) is "Minimize" or "Maximize or restore" or "Close") == 3 && Descendants<TextBlock>((FrameworkElement)share.Content).Any(x => x.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text.Contains("Crop for social"))));
            Check("Share window reads the recording", share.HasMedia && share.Summary.StartsWith("1280 × 720") && share.Summary.Contains("copied"));
            share.Choose("shorts"); share.UpdateLayout(); Check("Shorts preset plans vertical blur export", share.Summary.StartsWith("1080 × 1920") && share.Summary.Contains("H.264"));
            await Task.Delay(150); SaveElement((FrameworkElement)share.Content, Path.ChangeExtension(path, ".share-shorts.png"));
            share.Choose("tiktok"); share.UpdateLayout(); SaveElement((FrameworkElement)share.Content, Path.ChangeExtension(path, ".share-tiktok.png"));
            share.Choose("youtube"); share.UpdateLayout(); SaveElement((FrameworkElement)share.Content, Path.ChangeExtension(path, ".share-youtube.png"));
            share.Close(); Check("Share window closes when idle", !share.IsVisible && !ShareExportJob.Busy);
            // Phase 3 Library: video rows offer Trim/Share, images keep Open only; sorting; big folders realize one page only.
            string picture = Path.Combine(settings.OutputDirectory, "FRAME ui image.png");
            { var bmp = new RenderTargetBitmap(32, 18, 96, 96, PixelFormats.Pbgra32); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var pf = File.Create(picture); png.Save(pf); }
            string trimSource = Path.Combine(settings.OutputDirectory, "FRAME ui trim.mp4");
            var trimMade = await Backend.Run(Backend.Resolve(settings.FfmpegPath), ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-f", "lavfi", "-i", "sine=sample_rate=48000", "-t", "4", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", trimSource]);
            Check("Synthetic 4 s recording created for Trim", trimMade.Code == 0);
            ShowPage(LibraryPage, "", ""); await RefreshLibrary(); UpdateLayout();
            IEnumerable<string> RowButtons(string name) => LibraryList.Children.OfType<Border>().Where(b => ((Grid)b.Child).Children.OfType<StackPanel>().Any(p => p.Children.OfType<TextBlock>().Any(t => t.Text == name))).SelectMany(b => ((Grid)b.Child).Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>())).Select(b => b.Content as string ?? "");
            Check("Video rows offer Trim, Crop for social and more actions", RowButtons("FRAME ui trim").SequenceEqual(["Trim", "Crop for social", "•••"]));
            Check("Image rows keep Open without video actions", RowButtons("FRAME ui image").SequenceEqual(["Open", "•••"]));
            // 0.5.4: the ••• menu was empty (light app text on WPF's light default menu). Open it for real and measure.
            {
                var moreButton = LibraryList.Children.OfType<Border>().Where(b => ((Grid)b.Child).Children.OfType<StackPanel>().Any(p => p.Children.OfType<TextBlock>().Any(t => t.Text == "FRAME ui trim"))).SelectMany(b => ((Grid)b.Child).Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>())).First(b => b.Content as string == "•••");
                moreButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Task.Delay(300); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var menu = moreButton.ContextMenu!;
                var items = menu.Items.OfType<MenuItem>().ToArray();
                var labels = items.Select(i => Descendants<TextBlock>(i).FirstOrDefault()).ToArray();
                Color Solid(Brush? b) => b is SolidColorBrush s ? s.Color : Colors.Transparent;
                double Lum(Color c) => (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255;
                var bg = Solid(menu.Background);
                bool readable = labels.All(t => t != null && t.ActualWidth > 20 && t.ActualHeight > 8 && Math.Abs(Lum(Solid(t.Foreground)) - Lum(bg)) > .45);
                Check("Library ••• menu opens with visible, readable Play clip and Delete clip", menu.IsOpen && items.Select(i => i.Header as string).SequenceEqual(["Play clip", "Show in folder", "Copy file path", "Rename", "Copy file", "Delete clip"]) && readable && menu.ActualWidth >= 150 && menu.ActualHeight > 6 * 24);
                SaveElement(menu, Path.ChangeExtension(path, ".library-menu.png"));
                menu.IsOpen = false;
                // Delete: the same worker the confirmed menu action uses (the confirmation dialog itself is manual).
                string doomed = Path.Combine(settings.OutputDirectory, "FRAME ui delete.mp4"); File.Copy(trimSource, doomed);
                string keep = Path.Combine(settings.OutputDirectory, "FRAME ui keep.mp4"); File.Copy(trimSource, keep);
                bool removed = await DeleteCapture(doomed) == null && !File.Exists(doomed) && File.Exists(keep) && File.Exists(trimSource);
                await RefreshLibrary();
                Check("Delete clip moves only that file to the Recycle Bin and Library drops it", removed && !LibraryList.Children.OfType<Border>().Any(b => Descendants<TextBlock>(b).Any(t => t.Text == "FRAME ui delete")));
                string? missing = await DeleteCapture(doomed);
                string? locked; using (new FileStream(keep, FileMode.Open, FileAccess.Read, FileShare.None)) locked = await DeleteCapture(keep);
                Check("Missing or locked files are not reported as deleted", missing != null && locked != null && File.Exists(keep));
                File.Delete(keep); await RefreshLibrary();
            }
            LibrarySort.SelectedIndex = 2; await RefreshLibrary();
            var names = LibraryList.Children.OfType<Border>().Select(b => ((Grid)b.Child).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First().Text).ToArray();
            Check("Name sort orders rows", names.Length >= 3 && names.SequenceEqual(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)));
            SearchBox.Text = "zz-no-match"; await RefreshLibrary(); Check("Search shows a clear empty state", LibraryList.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("Nothing matches")));
            SearchBox.Text = ""; LibrarySort.SelectedIndex = 0; await RefreshLibrary(); await Task.Delay(2500);
            SaveRender(Path.ChangeExtension(path, ".library.png"));
            string bulk = Path.Combine(data, "bulk-library"); Directory.CreateDirectory(bulk);
            for (int i = 0; i < 400; i++) File.WriteAllText(Path.Combine(bulk, $"FRAME bulk {i:000}.mp4"), "not a real video");
            string realOutput = settings.OutputDirectory; settings.OutputDirectory = bulk; await RefreshLibrary();
            Check("400-file library realizes only one page of rows", LibraryList.Children.Count == 50 && MoreButton.Visibility == Visibility.Visible);
            settings.OutputDirectory = realOutput; await RefreshLibrary();

            // Clip editor: real decode, thumbnails, keyboard precision, clamped dragging, lifecycle and release.
            int baselineThumbJobs = ShareExportJob.ActiveThumbnailJobs;
            var trim = OpenTrim(trimSource, false)!; trim.Left = -20000; trim.ShowInTaskbar = false;
            await Task.Delay(100); await trim.Ready;
            for (int i = 0; i < 50 && !trim.MediaReady; i++) await Task.Delay(100);
            Check("Editor opens the recording and its preview", trim.Selection is { Duration: > 3.9 } && trim.MediaReady);
            {
                var captions = Descendants<Button>(trim).Select(b => System.Windows.Automation.AutomationProperties.GetName(b)).ToArray();
                var brand = Descendants<TextBlock>(trim).FirstOrDefault(t => t.Inlines.OfType<System.Windows.Documents.Run>().FirstOrDefault()?.Text == "FRAME");
                Check("Trim uses the FRAME title bar (no Windows caption, FRAME · Trim, minimize/maximize/close)", trim.WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(trim) is { CaptionHeight: 36 } && brand != null && brand.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text.Contains("Trim")) && new[] { "Minimize", "Maximize or restore", "Close" }.All(captions.Contains));
            }
            await trim.Thumbs; await Task.Delay(150);
            Check("Timeline shows lazily generated thumbnails", trim.Timeline.Thumbnails == 14);
            var sel = trim.Selection!;
            trim.Press(Key.Right, ModifierKeys.Shift); trim.Press(Key.I); trim.Press(Key.Right, ModifierKeys.Shift); trim.Press(Key.Right); trim.Press(Key.Right); trim.Press(Key.Left); trim.Press(Key.O);
            Check("I/O and frame-step keys set exact frame boundaries", Math.Abs(sel.Start - 1) < 1e-6 && Math.Abs(sel.End - (2 + 1 / 30d)) < 1e-6);
            trim.Timeline.SimulateDrag(true, trim.Timeline.XOf(sel.Start), trim.Timeline.ActualWidth + 300, 200);
            Check("Start handle cannot pass the end handle", sel.Start < sel.End && sel.End - sel.Start >= sel.MinLength - 1e-9);
            trim.Timeline.SimulateDrag(true, trim.Timeline.XOf(sel.Start), -500, 50); trim.Timeline.SimulateDrag(false, trim.Timeline.XOf(sel.End), trim.Timeline.ActualWidth + 500, 50);
            Check("Handles clamp to the recording bounds", sel.Start == 0 && sel.End == sel.Duration);
            sel.Set(1, 2.5);
            trim.Press(Key.Space); await Task.Delay(300);
            Check("Space plays with a running playhead clock", trim.Playing && trim.ClockRunning);
            trim.Hide(); Check("Hiding the editor pauses playback and stops its clock", !trim.Playing && !trim.ClockRunning); trim.Show(); trim.Left = -20000;
            trim.UpdateLayout(); SaveElement((FrameworkElement)trim.Content, Path.ChangeExtension(path, ".trim.png"));
            var editorShare = OpenShare(trimSource, false, sel.Range, trim)!; editorShare.Left = -20000; await Task.Delay(100); await editorShare.Ready;
            Check("Editor Share passes the selected range to the export plan", editorShare.HasMedia && editorShare.Summary.StartsWith("1280 × 720") && editorShare.Summary.Contains("H.264"));
            await Task.Delay(400); editorShare.UpdateLayout(); SaveElement((FrameworkElement)editorShare.Content, Path.ChangeExtension(path, ".trim-share.png")); editorShare.Close();
            string trimmedDir = ReplayFolders.TrimmedClips(settings.OutputDirectory);
            if (Directory.Exists(trimmedDir)) Directory.Delete(trimmedDir, true);
            string sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(trimSource)));
            // Real button click (0.5.4 root-cause regression: the click must start a real export).
            var saveButton = Descendants<Button>((FrameworkElement)trim.Content).First(b => b.Content as string == "Save clip");
            saveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(50); bool startedVisibly = trim.Status.StartsWith("Saving clip") || trim.Status.StartsWith("Checking");
            for (int i = 0; i < 300 && trim.SavedClip == null && !trim.Status.StartsWith("Clip not saved"); i++) await Task.Delay(100);
            Check("Save clip button starts a real export and shows that saving started", startedVisibly && trim.SavedClip != null);
            Check("Save clip writes a verified file into <capture folder>\\Trimmed Clips, created on demand, original unchanged", trim.SavedClip is { } savedClip && File.Exists(savedClip) && Path.GetDirectoryName(savedClip) == trimmedDir && trim.Status.Contains("Trimmed Clips") && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(trimSource))) == sourceHash);
            var firstClip = trim.SavedClip!; await trim.SaveForTest();
            Check("A second Save clip never overwrites the first", trim.SavedClip != firstClip && File.Exists(firstClip) && File.Exists(trim.SavedClip));
            Check("Trimmed clip keeps the selection length and audio", await ShareExportJob.Probe(Backend.Resolve(settings.FfmpegPath), firstClip) is { HasAudio: true } m && Math.Abs(m.Duration - sel.Length) < .1);
            // Save to…: Save As target, remembered folder, independent of Save clip.
            string elsewhere = Path.Combine(data, "save-to-" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(elsewhere);
            string? rememberedBefore = settings.TrimSaveDirectory;
            Check("Save to… starts in Trimmed Clips before any choice and suggests a readable .mp4 name", trim.SaveToStart() == trimmedDir && trim.SuggestedName() == "ui trim clip.mp4");
            string named = Path.Combine(elsewhere, "my highlight.mp4");
            await trim.SaveAsForTest(named);
            Check("Save to… writes the exact chosen name and remembers the folder persistently", trim.SavedClip == named && File.Exists(named) && settings.TrimSaveDirectory == elsewhere && SettingsStore.Load(configPath).TrimSaveDirectory == elsewhere && trim.SaveToStart() == elsewhere);
            long namedBytes = new FileInfo(named).Length; await trim.SaveAsForTest(named);
            Check("Save to… never replaces an existing file without confirmation", trim.Status.StartsWith("Clip not saved") && new FileInfo(named).Length == namedBytes);
            await trim.SaveAsForTest(trimSource, true);
            Check("Save to… refuses to replace its own recording", trim.Status.StartsWith("Clip not saved") && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(trimSource))) == sourceHash);
            await trim.SaveForTest();
            Check("Save clip still uses Trimmed Clips after Save to…", Path.GetDirectoryName(trim.SavedClip) == trimmedDir);
            Directory.Delete(elsewhere, true);
            Check("A remembered folder that no longer exists falls back to Trimmed Clips", trim.SaveToStart() == trimmedDir);
            settings.TrimSaveDirectory = rememberedBefore ?? "";
            await Task.Delay(300); await RefreshLibrary();
            Check("Saved clip appears in Library without restart", LibraryList.Children.OfType<Border>().Any(b => ((Grid)b.Child).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First().Text == Path.GetFileNameWithoutExtension(trim.SavedClip)));
            trim.Close(); await Task.Delay(200);
            Check("Closing the editor releases player, timers and workers", trim.Released && ShareExportJob.ActiveThumbnailJobs == baselineThumbJobs);
            for (int i = 0; i < 4; i++) { var again = OpenTrim(trimSource, false)!; again.Left = -20000; await Task.Delay(60); again.Close(); }
            await Task.Delay(1500);
            Check("Repeated open/close leaves no thumbnail workers", ShareExportJob.ActiveThumbnailJobs == baselineThumbJobs);
            var busy = new TrimWindow(this, trimSource, Backend.Resolve(settings.FfmpegPath), Path.Combine(data, "Thumbnails", "busy"), () => true, _ => { }, (_, _, _) => null) { EncoderSource = ShareEncoder, Left = -20000, ShowInTaskbar = false };
            busy.Show(); await Task.Delay(100); await busy.Ready; await Task.Delay(200);
            busy.Selection!.Set(1, 2); await busy.SaveForTest();
            Check("During recording the editor skips thumbnails and saves in low-impact CPU mode (0.5.4)", busy.Timeline.Thumbnails == 0 && !Directory.Exists(Path.Combine(data, "Thumbnails", "busy")) && busy.LowImpactUsed && busy.SavedClip is { } lowClip && File.Exists(lowClip) && Math.Abs((await ShareExportJob.Probe(Backend.Resolve(settings.FfmpegPath), lowClip)).Duration - 1) < .1);
            busy.Close();
            // Save notification: real save path (hotkey message, button handler) while FRAME is hidden and another window has focus.
            bool toastsWere = settings.ShowSaveNotifications; settings.ShowSaveNotifications = true;
            var game = new Window { Title = "Focus probe (game stand-in)", Width = 320, Height = 200, Left = 40, Top = 40, ShowInTaskbar = false };
            game.Show(); game.Activate(); await Task.Delay(200);
            nint gameHandle = new System.Windows.Interop.WindowInteropHelper(game).Handle, before = ForegroundWindow();
            Hide(); await Task.Delay(100);
            int replayId = hotkeys.FirstOrDefault(p => p.Value == "Replay").Key;
            bool registered = replayId != 0; if (!registered) { replayId = 9999; hotkeys[replayId] = "Replay"; } // OS registration may be held by another FRAME; WndProc path is the same
            SendHotkey(new System.Windows.Interop.WindowInteropHelper(this).Handle, replayId);
            for (int i = 0; i < 30 && SaveOverlay.Current == null; i++) await Task.Delay(50);
            var failToast = SaveOverlay.Current;
            Check("Failed save (no shadow history) shows a distinct error toast while FRAME is hidden · via " + (registered ? "WM_HOTKEY to WndProc (registered hotkey)" : "WM_HOTKEY to WndProc (temporary id; Windows registration held elsewhere)"), failToast is { Success: false, IsVisible: true } && !IsVisible);
            bool gameFocused = before == gameHandle; // false when Windows refused foreground to the test process
            Check("Toast never takes focus from the running window" + (gameFocused ? "" : " (stand-in not foreground: checked that the existing foreground window kept focus)"), ForegroundWindow() == before && (game.IsActive || !gameFocused) && !(failToast?.IsActive ?? true));
            Check("Toast is click-through, non-activating, tool window", failToast != null && (failToast.ExtendedStyle & 0x20) != 0 && (failToast.ExtendedStyle & 0x8000000) != 0 && (failToast.ExtendedStyle & 0x80) != 0);
            Check("Toast is excluded from screen capture", failToast?.DisplayAffinity is 0x11u);
            await Task.Delay(300);
            // The edge margin is 24 DIP, i.e. 24 px at 100 % and 42 px at 175 % scaling: compare in the display's own scale.
            double toastScale = failToast == null ? 1 : Native.Scale(Native.MonitorOf(new System.Windows.Interop.WindowInteropHelper(failToast).Handle)), edge = SaveOverlay.EdgeMargin * toastScale + 2;
            Check("Toast sits top-right inside the active display's work area", failToast?.PlacedAt is { } at && failToast.MonitorWork is { } work && at.Right <= work.Right && work.Right - at.Right <= edge && at.Y >= work.Y && at.Y - work.Y <= edge);
            double seconds = await SavedSeconds([trimSource]);
            var ok = SaveOverlay.Present("Clip saved", SaveFeedback.Saved(seconds), true);
            Check("Success toast reports the measured clip length", ok == failToast && ok.Success && Math.Abs(seconds - 4) < .1 && SaveFeedback.Saved(seconds) == "Last 4 seconds saved");
            SaveOverlay.Present("Clip saved", SaveFeedback.Saved(300), true);
            Check("Repeated saves reuse one toast instead of stacking", Application.Current.Windows.OfType<SaveOverlay>().Count() == 1);
            await Task.Delay(1000); SaveElement((FrameworkElement)ok.Content, Path.ChangeExtension(path, ".toast.png"));
            await Task.Delay(2200);
            Check("Toast fades out and closes after ~2.5 s", SaveOverlay.Current == null && !ok.IsVisible && !Application.Current.Windows.OfType<SaveOverlay>().Any());
            settings.ShowSaveNotifications = false; try { await SaveShadow(); } catch (InvalidOperationException) { }
            Check("Disabled preference shows no toast", SaveOverlay.Current == null);
            settings.ShowSaveNotifications = true; await Act(async () => { await SaveShadow(); });
            Check("GUI save path reports failure without a success toast", SaveOverlay.Current is { Success: false });
            // 0.5.4 replay feedback: requests arriving while a save runs or FRAME is busy are answered at once, never stacked.
            SaveOverlay.Current?.Close(); SaveOverlay.History.Clear();
            savingReplay = true; await RequestSaveReplay();
            Check("Repeated save while saving shows one pending 'Still saving replay…' note, no success", SaveOverlay.Current is { Pending: true, Success: false } && SaveOverlay.History.SequenceEqual(["Still saving replay…"]));
            savingReplay = false; await actions.WaitAsync();
            try { await RequestSaveReplay(); } finally { actions.Release(); }
            Check("Save while another action runs is answered with 'Replay not saved', reusing the same note", SaveOverlay.Current is { Pending: false, Success: false } && SaveOverlay.History.Last() == "Replay not saved" && Application.Current.Windows.OfType<SaveOverlay>().Count() == 1);
            var pendingNote = SaveOverlay.Present("Saving replay…", "Processing your clip", true, pending: true);
            Check("Saving replay… note is pending (open ring), click-through and non-activating", pendingNote.Pending && !pendingNote.Success && (pendingNote.ExtendedStyle & 0x8000020) == 0x8000020 && !pendingNote.IsActive);
            SaveOverlay.Present("Clip saved", SaveFeedback.Saved(300), true);
            Check("Completion replaces the pending note in place (no second window)", SaveOverlay.Current == pendingNote && pendingNote.Success && !pendingNote.Pending && Application.Current.Windows.OfType<SaveOverlay>().Count() == 1);
            SaveOverlay.Current?.Close();
            SaveOverlay.Current?.Close(); settings.ShowSaveNotifications = toastsWere; if (!registered) hotkeys.Remove(9999);
            game.Close(); Show(); Activate();
            // 0.5.4 game minimize (policy + real hide path; a real game launch is a manual test).
            {
                SetupTray(); // render diagnostics run without a tray; the real app always has one
                int keys = hotkeys.Count; lastInteraction = 0; bool settingWas = settings.MinimizeOnGameStart; settings.MinimizeOnGameStart = true;
                foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w != this && w is not SaveOverlay && w.IsVisible).ToArray()) w.Close();
                bool hid = ConsiderGame(900001, "cs2.exe");
                Check("A detected game hides FRAME to the tray; hotkeys stay registered and animation stops", hid && !IsVisible && tray?.Visible == true && hotkeys.Count == keys && !atmosphere.Running && !quitting);
                Restore(); await Task.Delay(100); lastInteraction = 0;
                Check("Reopening FRAME while the same game runs never hides it again (no loop)", !ConsiderGame(900001, "cs2.exe") && IsVisible);
                var editor = OpenTrim(trimSource, false)!; editor.Left = -20000; await Task.Delay(100);
                Check("An open editor is never hidden or interrupted by a new game", !ConsiderGame(900002, "cs2.exe") && IsVisible && editor.IsVisible);
                editor.Close(); await Task.Delay(100);
                lastInteraction = Environment.TickCount64;
                Check("Recent input in FRAME prevents hiding", !ConsiderGame(900003, "cs2.exe") && IsVisible);
                lastInteraction = 0; settings.MinimizeOnGameStart = false;
                Check("Disabled setting keeps FRAME visible", !ConsiderGame(900004, "cs2.exe") && IsVisible);
                settings.MinimizeOnGameStart = true; OnForegroundChanged(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                Check("FRAME's own windows never count as a game", IsVisible && GameMinimizeCount == 1);
                Check("Game minimizing is the last Start-up option", SettingsPage.Children.OfType<StackPanel>().Any(x => x.Children.OfType<CheckBox>().LastOrDefault() is { Content: TextBlock { Text: "Minimize FRAME when a game starts" } }));
                settings.MinimizeOnGameStart = settingWas; tray!.Dispose(); tray = null; Activate();
            }
            // Start with Windows: same code path against a private test key (never the real Run key in diagnostics).
            const string testKey = @"Software\FRAME-Test\Run";
            try
            {
                StartupRegistration.Apply(true, @"D:\Portable\FRAME\FRAME.exe", testKey);
                bool on = StartupRegistration.IsEnabled(testKey) && (string?)Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey)!.GetValue("FRAME") == "\"D:\\Portable\\FRAME\\FRAME.exe\" --background";
                StartupRegistration.Apply(true, @"E:\Moved\FRAME.exe", testKey); bool healed = ((string?)Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey)!.GetValue("FRAME"))!.Contains(@"E:\Moved\FRAME.exe");
                StartupRegistration.Apply(false, @"E:\Moved\FRAME.exe", testKey);
                Check("Start with Windows writes, re-points and removes a per-user Run entry", on && healed && !StartupRegistration.IsEnabled(testKey));
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\FRAME-Test", false); }
            Check("Start-up options sit directly below Hotkeys", SettingsPage.Children.OfType<StackPanel>().Any(x => x.Children.OfType<CheckBox>().Any(c => ((TextBlock)c.Content).Text == "Start shadow recording when FRAME starts")));
            // 0.5.3 recording health without capture: a disk fault (not auto-retried) drives overlay, panel, tray and persistence.
            {
                ShowPage(HomePage, "Capture", ""); SaveOverlay.History.Clear(); SaveOverlay.Current?.Close();
                var diskFault = RecordingHealth.Classify(new FaultSignals(true, false, false, 1, "Low disk space: capture stopped."), true, DateTimeOffset.Now);
                OnFault(diskFault); await Task.Delay(300); UpdateLayout();
                var panelText = Descendants<TextBlock>(FailurePanel).Select(t => t.Text).ToArray();
                Check("Unexpected stop: one top-right warning, plain explanation, restart offered", SaveOverlay.History.SequenceEqual(["Shadow Recording stopped"]) && SaveOverlay.Current is { Success: false } && FailurePanel.Visibility == Visibility.Visible && panelText.Contains("Not enough storage space to continue recording.") && Descendants<Button>(FailurePanel).Any(b => b.Content as string == "Restart Shadow Recording") && Descendants<Expander>(FailurePanel).Any(e => e.Header as string == "Technical details" && !e.IsExpanded));
                Check("Tray shows the error state", ComputeTrayState() == TrayState.Error && ExpectedTrayText == "FRAME · Recording stopped – open for details");
                var savedStatus = File.ReadAllText(Path.Combine(data, "recording-status.json"));
                Check("Stop is persisted for the next start", savedStatus.Contains("\"Kind\":0") && savedStatus.Contains("\"Resolved\":false"));
                SaveRender(Path.ChangeExtension(path, ".failure.png"));
                fault = null; LoadRecordingStatus(); UpdateState();
                Check("After a restart the stop is shown as last session, not as the current state", faultFromPreviousSession && Descendants<TextBlock>(FailurePanel).Any(t => t.Text.StartsWith("Last session:")) && ComputeTrayState() != TrayState.Error);
                Descendants<Button>(FailurePanel).First(b => b.Content as string == "Dismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("Dismiss hides the panel and is remembered", FailurePanel.Visibility == Visibility.Collapsed && File.ReadAllText(Path.Combine(data, "recording-status.json")).Contains("\"Dismissed\":true"));
                var icons = Enum.GetValues<TrayStateKind>().Select(k => { using var i = TrayIcon(k); using var b = i.ToBitmap(); var px = new List<int>(); for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) px.Add(b.GetPixel(x, y).ToArgb()); return string.Join(",", px); }).ToArray();
                Check("Four distinct tray icons (idle, starting, active, error)", icons.Distinct().Count() == 4);
                SaveOverlay.Current?.Close(); fault = null; faultDismissed = false; UpdateState();
            }
            {
                // Visual modes (0.6.1): five modes, all named in the title bar at once; a click activates that mode with its own
                // complete palette (no mixture), the open page stays open, only VisualMode is saved (never a capture setting),
                // exactly one link is marked active, the links never reach the caption buttons (also at the minimum size), and
                // Dark / No Bullshit and Light run no decorative clock at all.
                string[] keys = ["Background", "Surface", "Raised", "Line", "Text", "Muted", "Accent", "Hero", "HeroText", "Subtle", "NavActive", "NavFrame", "SidebarBackground", "PageScrim", "Panel", "Recording"];
                string Palette() => string.Join(",", keys.Select(k => ((SolidColorBrush)Application.Current.Resources[k]).Color.ToString()));
                var modeBase = mode; var themeBase = CloneSettings();
                BuildSettings(); ShowPage(SettingsPage, "", ""); UpdateLayout();
                var palettes = new Dictionary<VisualMode, string>(); bool chrome = true, stayed = true, quiet = true, saved = true;
                foreach (var vm in VisualModes.All)
                {
                    ModeLinkButtons[vm].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); UpdateLayout(); palettes[vm] = Palette();
                    var file = SettingsStore.Load(configPath);
                    saved &= mode == vm && file.VisualMode == VisualModes.Key(vm) && !CaptureSettings.Differ(themeBase, file) && file.ReplaySeconds == themeBase.ReplaySeconds && JsonSerializer.Serialize(file.Hotkeys) == JsonSerializer.Serialize(themeBase.Hotkeys);
                    chrome &= ModeLinkButtons.Count == 5 && ModeLinkButtons.All(l => (l.Value.Tag as string == "Active") == (l.Key == vm) && l.Value.IsVisible && (l.Value.Content as TextBlock)?.Text == VisualModes.Name(l.Key));
                    stayed &= SettingsPage.Visibility == Visibility.Visible && SettingsNav.Tag as string == "Selected" && CaptureHost.Visibility != Visibility.Visible;
                    if (!VisualModes.Decorative(vm)) quiet &= !atmosphere.Running && !atmosphere.Desert && ActiveScene == null;
                }
                Check("Visual modes: five links, one click each activates that mode (complete distinct palettes, page kept, one active link) and saves only the mode", palettes.Values.Distinct().Count() == 5 && chrome && stayed && saved);
                Check("Dark / No Bullshit and Light: no decorative clock, no desert, no scene", quiet);
                bool Fits()
                {
                    UpdateLayout(); var caption = Descendants<Button>(TitleBarSurface).Where(x => x.Style == FindResource("CaptionButton") || x.Style == FindResource("CloseCaptionButton")).Min(x => x.TranslatePoint(new Point(), this).X);
                    var links = ModeLinkButtons.Values.Select(l => new Rect(l.TranslatePoint(new Point(), this), l.RenderSize)).OrderBy(r => r.X).ToArray();
                    bool apart = links.Zip(links.Skip(1)).All(p => p.First.Right <= p.Second.Left + .5);
                    return apart && links[^1].Right <= caption && links[0].Left > 120 && links.All(r => r.Top >= 0 && r.Bottom <= TitleBarSurface.ActualHeight + .5);
                }
                double w0 = Width, h0 = Height; bool fitNormal = Fits(); Width = MinWidth; Height = MinHeight; bool fitMinimum = Fits(); Width = w0; Height = h0; UpdateLayout();
                Check("Mode links fit in the title bar left of the window buttons without overlap (normal and minimum size)", fitNormal && fitMinimum);
                SettingsStore.Save(configPath, themeBase); settings = themeBase; settingsDraft.Clear(); BuildSettings(); ApplyMode(modeBase, persist: false); UpdateState();
                var recoveryRow = SettingsPage.Children.OfType<Grid>().First(g => RowTitle(g) == "Recovery");
                var info = ((StackPanel)((StackPanel)recoveryRow.Children[0]).Children[0]).Children[1] as Border;
                var tipText = info?.ToolTip is ToolTip { Content: StackPanel tipBody } ? string.Join("\n", tipBody.Children.OfType<TextBlock>().Select(t => t.Text)) : "";
                Check("Recovery has a small info icon: keyboard-focusable, tooltip with the explanation", info is { Focusable: true, ActualWidth: < 26 } && tipText.StartsWith("What is Recovery?") && tipText.Contains("Recovery is not guaranteed") && System.Windows.Automation.AutomationProperties.GetName(info) == "What is Recovery?");
            }
            ShowPage(HomePage, "Capture", "");
            double width = Width, height = Height; Width = MinWidth; Height = MinHeight; UpdateLayout();
            Check("Minimum layout retains usable native hit targets", HistorySlider.ActualWidth >= 100 && ShadowButton.ActualWidth > 250 && RecordButton.ActualWidth > 150);
            double navBottom = SettingsNav.TranslatePoint(new Point(0, SettingsNav.ActualHeight), this).Y;
            Check("Settings navigation is anchored at the bottom of the sidebar at minimum size", ActualHeight - navBottom is >= 10 and <= 40 && SettingsNav.TranslatePoint(new Point(), this).Y > LibraryNav.TranslatePoint(new Point(), this).Y + 100 && Math.Abs(SettingsNav.TranslatePoint(new Point(), this).X - LibraryNav.TranslatePoint(new Point(), this).X) < .5);
            bool sourceOpen = SourceExpander.IsExpanded; SourceExpander.IsExpanded = true; UpdateLayout();
            Check("Capture source offers displays only: no window/game or region mode buttons (0.5.5)", FindName("WindowButton") == null && FindName("RegionButton") == null && FindName("CaptureModeButtons") == null && MonitorCanvas.Children.Count == displays.Length);
            SourceExpander.IsExpanded = sourceOpen; UpdateLayout();
            Message.Text = ""; SaveRender(Path.ChangeExtension(path, ".minimum.png"));
            Width = width; Height = height; UpdateLayout();
            Notify("UI checks passed.");
        }
        finally { await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
    }
    void SaveRender(string path, double scale = 1) { UpdateLayout(); SaveElement((FrameworkElement)Content, path, scale); }
    static void SaveElement(FrameworkElement visual, string path, double scale = 1)
    {
        var image = new RenderTargetBitmap((int)(visual.ActualWidth * scale), (int)(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var f = File.Create(path); encoder.Save(f);
    }
}

public partial class MainWindow
{
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] static extern nint ForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, int msg, nint wp, nint lp);
    static void SendHotkey(nint hwnd, int id) => SendMessage(hwnd, 0x0312, id, 0);
}
