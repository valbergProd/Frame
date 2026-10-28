using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Frame.App;

// Real-capture harnesses for the 0.5.5 recording changes (isolated --data only; App.xaml.cs gives them their own mutex). They
// capture a synthetic test window FRAME creates itself (testSource), never a display, never audio devices.
//   --quality-change-test <report.json>: quality changes while Shadow Recording runs (ShadowRestart.cs).
//   --close-test <report.json>: Close (X) exits completely; the calling script checks the exit, FFmpeg and the recording.
public partial class MainWindow
{
    async Task<bool> RunRecordingHarness()
    {
        if (!args.Contains("--data")) return false;
        if (Array.IndexOf(args, "--audio-quality-test") is int aq and >= 0)
        {
            try { await AudioQualityTest(args[aq + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[aq + 1] + ".error.txt", error.ToString()); }
            await Quit(); return true;
        }
        if (Array.IndexOf(args, "--still-stop-test") is int st and >= 0)
        {
            try { await StillStopTest(args[st + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[st + 1] + ".error.txt", error.ToString()); }
            await Quit(); return true;
        }
        if (Array.IndexOf(args, "--quality-change-test") is int q and >= 0)
        {
            try { await QualityChangeTest(args[q + 1]); }
            catch (Exception error) { await File.WriteAllTextAsync(args[q + 1] + ".error.txt", error.ToString()); }
            await Quit(); return true;
        }
        if (Array.IndexOf(args, "--close-test") is int c and >= 0)
        {
            try { await CloseTest(args[c + 1]); } // ends with the real Close button; FRAME exits through Quit
            catch (Exception error) { await File.WriteAllTextAsync(args[c + 1] + ".error.txt", error.ToString()); await Quit(); }
            return true;
        }
        return false;
    }

    // Moving test source (a bar and a counter on a dark canvas). Physical size: up to 1280 x 1160 px within the primary work
    // area, so 720p, 1080p and native output sizes differ; never activated, no taskbar button.
    (Window Window, DispatcherTimer Motion) TestSourceWindow(string title, int widthPx, int heightPx)
    {
        nint primary = Native.PrimaryMonitor(); double scale = Native.Scale(primary); var work = Native.WorkArea(primary) ?? new Box(0, 0, 1920, 1080);
        int w = Math.Min(widthPx, work.Width - 40), h = Math.Min(heightPx, work.Height - 40);
        var bar = new Rectangle { Width = 60, Height = 240, Fill = Brushes.Orange };
        var label = new TextBlock { FontSize = 40, Foreground = Brushes.White, Margin = new Thickness(16) };
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(20, 30, 50)) }; canvas.Children.Add(bar); canvas.Children.Add(label);
        var window = new Window { Title = title, Width = w / scale, Height = h / scale, Left = (work.Right - w - 20) / scale, Top = (work.Bottom - h - 20) / scale, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Content = canvas };
        int tick = 0; var motion = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        motion.Tick += (_, _) => { tick++; Canvas.SetLeft(bar, tick * 9 % Math.Max(1, (int)(window.ActualWidth - 60))); label.Text = tick.ToString(CultureInfo.InvariantCulture); };
        return (window, motion);
    }
    CaptureTarget TestTarget(Window window, string id)
    {
        var hwnd = new WindowInteropHelper(window).Handle; Native.GetWindowRect(hwnd, out var r);
        return new CaptureTarget(id, window.Title, r.Box, 0, hwnd);
    }
    // FFmpeg processes started by this FRAME process (capture pipelines; exports and probes are short-lived and not running here).
    static int[] ChildProcesses(string exeName)
    {
        var result = new List<int>(); nint snapshot = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == -1) return [];
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                if (entry.ParentProcessId == Environment.ProcessId && entry.ExeFile.Equals(exeName, StringComparison.OrdinalIgnoreCase) && Alive((int)entry.ProcessId)) result.Add((int)entry.ProcessId);
        }
        finally { CloseHandle(snapshot); }
        return result.ToArray();
    }
    static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; } }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ProcessEntry { public uint Size, Usage, ProcessId; public nuint DefaultHeapId; public uint ModuleId, Threads, ParentProcessId; public int BasePriority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);

    async Task QualityChangeTest(string report)
    {
        var results = new List<object>(); var clock = Stopwatch.StartNew();
        void Check(string name, bool ok, object? detail = null) => results.Add(new { name, passed = ok, at = Math.Round(clock.Elapsed.TotalSeconds, 1), detail });
        Left = -20000; ShowInTaskbar = false;
        string exe = Backend.Resolve(settings.FfmpegPath), probe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        var (target, motion) = TestSourceWindow("FRAME quality change test", 1280, 1160);
       try
        {
            bool isolated = Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase) && !settings.DesktopAudio && !settings.Microphone;
            Check("Isolated test settings (capture folder inside the data folder, no audio devices) and engine ready", isolated && ready, new { settings.OutputDirectory, ready, encoder = ChooseEncoder().Id });
            if (!isolated || !ready) return;
            target.Show(); motion.Start(); await Task.Delay(600);
            testSource = [TestTarget(target, "quality-test")]; var bounds = testSource[0].Bounds; DrawMonitors();

            // Start: Lightweight 720p30 with a 15-minute replay length, saved like the user's own choice.
            var start = CloneSettings(); start.OutputHeight = 720; start.Fps = 30; start.BitrateMbps = 6; start.ReplaySeconds = 900;
            SettingsStore.Save(configPath, start); settings = start; SyncPreset(); SyncDuration(); UpdateState();
            await Act(ToggleShadow);
            Check("Shadow Recording starts with Lightweight 720p30 and a 15-minute replay length", engine.Replay && PresetCombo.SelectedIndex == 2 && ReplayKept() && OneFfmpegPerPipeline(), Snapshot());
            Check("Quality stays changeable while Shadow Recording runs; the tooltip says what a change does", PresetCombo.IsEnabled && (PresetCombo.ToolTip as string ?? "").Contains("replay length is kept"), PresetCombo.ToolTip);
            await Task.Delay(4000);
            var first = await SaveClip("Lightweight", 720, 30);
            Check("Save before the change: 720p / 30 FPS clip", first.Ok, first);

            foreach (var (index, name, height, fps) in new[] { (0, "Balanced", 1080, 60), (1, "Original", 0, 60) })
            {
                var (seconds, old) = await ChangeQuality(index);
                var restartedAt = Stopwatch.StartNew();
                Check($"{name}: Shadow Recording restarted with {(height == 0 ? "native" : height + "p")}/{fps} (settings, saved file, combo, running session) in {seconds:0.0} s",
                    engine.Replay && !shadowTransition && settings.OutputHeight == height && settings.Fps == fps && SettingsStore.Load(configPath) is { } f && f.OutputHeight == height && f.Fps == fps && PresetCombo.SelectedIndex == index && engine.SessionProfile.OutputHeight == height && engine.SessionProfile.Fps == fps && ShadowTitle.Text == "Stop shadow recording", Snapshot());
                Check($"{name}: replay length still 15 minutes everywhere (setting, file, slider, labels, rolling buffer)", ReplayKept(), Snapshot());
                Check($"{name}: exactly one FFmpeg per pipeline; the previous FFmpeg has exited", OneFfmpegPerPipeline() && old.All(pid => !Alive(pid)) && !old.Intersect(engine.ProcessIds).Any(), new { old, now = engine.ProcessIds.ToArray(), children = ChildProcesses("ffmpeg.exe") });
                Check($"{name}: honest status text (replay length kept, earlier footage cleared)", Message.Text.Contains("replay length stays 15 minutes") && Message.Text.Contains("earlier footage was cleared"), Message.Text);
                await Task.Delay(4000);
                var clip = await SaveClip(name, height, fps);
                Check($"{name}: save after the change succeeds with the new size and frame rate and holds only footage since the restart", clip.Ok && clip.Duration <= restartedAt.Elapsed.TotalSeconds + 1, new { clip, sinceRestart = restartedAt.Elapsed.TotalSeconds });
            }

            // Rapid repeated changes: the first starts the restart, later ones during it are ignored (combo reverts), a save
            // request and a Shadow toggle during it are refused; one restart, one FFmpeg, no race.
            {
                var old = engine.ProcessIds.ToArray(); presetChange = null; SaveOverlay.History.Clear();
                PresetCombo.SelectedIndex = 2; var running = presetChange;
                PresetCombo.SelectedIndex = 0; PresetCombo.SelectedIndex = 1; bool ignored = presetChange == running;
                await RequestSaveReplay(); bool saveRefused = SaveOverlay.History.LastOrDefault() == "Replay not saved";
                await RequestShadowToggle();
                if (running != null) await running;
                Check("Rapid changes: one restart (the first request), later changes ignored, save and toggle refused meanwhile; one FFmpeg per pipeline",
                    running != null && ignored && saveRefused && engine.Replay && settings.OutputHeight == 720 && settings.Fps == 30 && SettingsStore.Load(configPath).Fps == 30 && PresetCombo.SelectedIndex == 2 && engine.SessionProfile.Fps == 30 && OneFfmpegPerPipeline() && old.All(pid => !Alive(pid)) && ReplayKept(),
                    new { ignored, saveRefused, History = SaveOverlay.History.ToArray(), snapshot = Snapshot() });
            }

            // The new profile fails, the previous one starts again (the safe retry): the new profile names ffprobe.exe (beside
            {
               string ffmpegPath = settings.FfmpegPath; var broken = CloneSettings(); broken.FfmpegPath = probe; broken.OutputHeight = 1080; broken.Fps = 60; broken.BitrateMbps = 20;
                bool restarted = true; await Act(async () => restarted = await RestartShadowWith(broken, "Diagnostics: profile with a broken FFmpeg path."));
                Check("A new profile that cannot start: previous profile restored (settings, file, combo) and Shadow Recording continues with it",
                    !restarted && engine.Replay && settings.FfmpegPath == ffmpegPath && SettingsStore.Load(configPath).FfmpegPath == ffmpegPath && settings.Fps == 30 && PresetCombo.SelectedIndex == 2 && OneFfmpegPerPipeline() && ReplayKept() && Message.Text.Contains("continues with"), new { Message = Message.Text, snapshot = Snapshot() });
            }

            // Forced start failure (an encoder FFmpeg rejects, also for the retry): FRAME ends up not recording, previous profile kept.
            {
                SaveOverlay.History.Clear();
                forcedEncoder = new("h264_frame_test_missing", "Missing encoder (quality change test)", true, false, false);
                try { await ChangeQuality(0); } finally { forcedEncoder = null; }
                var logged = DiagnosticLog.ReadLines().Where(l => l.Contains("\"Component\":\"shadow-restart\"") && l.Contains("\"Level\":\"Error\"")).ToArray();
                Check("Forced start failure: not recording (no misleading state), previous profile restored in settings, file and combo, nothing left running, logged",
                    !engine.Replay && !engine.Busy && ChildProcesses("ffmpeg.exe").Length == 0 && settings.Fps == 30 && settings.OutputHeight == 720 && SettingsStore.Load(configPath) is { Fps: 30, OutputHeight: 720, ReplaySeconds: 900 } && PresetCombo.SelectedIndex == 2
                    && ShadowTitle.Text == "Start shadow recording" && !StatusBadge.Text.Contains("Shadow active") && TrayStateName != "Active" && settings.ReplaySeconds == 900 && HistorySlider.Seconds == 900 && Message.Text.Contains("Shadow Recording is off") && logged.Any(l => l.Contains("did not start")) && SaveOverlay.History.Contains("Shadow Recording did not start"),
                    new { Message = Message.Text, StatusBadge = StatusBadge.Text, tray = TrayStateName, History = SaveOverlay.History.ToArray(), logged = logged.Length });
                Check("Idle quality change after the failure: saved at once, no capture started", ChangeIdle(0) && settings.Fps == 60 && SettingsStore.Load(configPath).Fps == 60 && !engine.Busy && settings.ReplaySeconds == 900);
            }
            await Act(ToggleShadow);
            await Task.Delay(1500);
            Check("Shadow Recording starts again normally after the failure (Balanced) and stops cleanly; no FFmpeg left", engine.Replay && engine.SessionProfile.Fps == 60 && OneFfmpegPerPipeline(), Snapshot());
            await Act(ToggleShadow);
            Check("After the final stop: not busy, no FFmpeg child process", !engine.Busy && ChildProcesses("ffmpeg.exe").Length == 0);
        }
        catch (Exception e) { Check("Exception: " + e.Message, false, e.ToString()); }
        finally
        {
            forcedEncoder = null; motion.Stop(); if (target.IsVisible) target.Close(); testSource = [];
           await File.WriteAllTextAsync(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }

        bool ReplayKept() => settings.ReplaySeconds == 900 && SettingsStore.Load(configPath).ReplaySeconds == 900 && HistorySlider.Seconds == 900 && ReplaySaveTitle.Text == "Save last 15 minutes" && SaveHotkeyLabel.Text == "Hotkey for saving the 15 minutes" && (!engine.Busy || engine.SessionProfile is { ReplaySeconds: 900, RetainSeconds: 900 });
        bool OneFfmpegPerPipeline() { var children = ChildProcesses("ffmpeg.exe"); return engine.Pipelines.Count >= 1 && children.Length == engine.Pipelines.Count && children.Order().SequenceEqual(engine.ProcessIds.Order()); }
        object Snapshot() => new { engine.Replay, state = engine.State.ToString(), session = engine.SessionProfile.ToString(), settings.OutputHeight, settings.Fps, settings.ReplaySeconds, combo = PresetCombo.SelectedIndex, slider = HistorySlider.Seconds, save = ReplaySaveTitle.Text, hotkeyLabel = SaveHotkeyLabel.Text, title = ShadowTitle.Text, badge = StatusBadge.Text, message = Message.Text, pids = engine.ProcessIds.ToArray(), children = ChildProcesses("ffmpeg.exe") };
        async Task<(double Seconds, int[] Old)> ChangeQuality(int index)
        {
            var old = engine.ProcessIds.ToArray(); presetChange = null; var watch = Stopwatch.StartNew();
            PresetCombo.SelectedIndex = index;
            if (presetChange is { } running) await running;
            return (watch.Elapsed.TotalSeconds, old);
        }
        bool ChangeIdle(int index) { presetChange = null; PresetCombo.SelectedIndex = index; return presetChange == null && PresetCombo.SelectedIndex == index; }
        async Task<ClipCheck> SaveClip(string name, int height, int fps)
        {
            var files = await engine.SaveReplay(engine.RequestEdges());
            var r = await Backend.Run(probe, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,avg_frame_rate,r_frame_rate:format=duration", "-of", "json", files[0]]);
            using var doc = JsonDocument.Parse(r.Text); var s = doc.RootElement.GetProperty("streams")[0];
            int w = s.GetProperty("width").GetInt32(), h = s.GetProperty("height").GetInt32();
            static double Rate(string v) { var p = v.Split('/'); return p.Length == 2 && double.TryParse(p[0], CultureInfo.InvariantCulture, out var a) && double.TryParse(p[1], CultureInfo.InvariantCulture, out var b) && b > 0 ? a / b : 0; }
            double avg = Rate(s.GetProperty("avg_frame_rate").GetString() ?? ""), real = Rate(s.GetProperty("r_frame_rate").GetString() ?? "");
            double duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString() ?? "0", CultureInfo.InvariantCulture);
            var b = testSource[0].Bounds; int eh = height > 0 ? Math.Min(height, b.Height) / 2 * 2 : b.Height, ew = height > 0 ? (int)((double)b.Width / b.Height * eh) / 2 * 2 : b.Width;
            bool size = Math.Abs(w - ew) <= 2 && Math.Abs(h - eh) <= 2, rate = Math.Abs(real - fps) < .5 && Math.Abs(avg - fps) < 2;
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", files[0], "-f", "null", "-"], 120);
            return new ClipCheck(name, Path.GetFileName(files[0]), w, h, ew, eh, Math.Round(real, 2), Math.Round(avg, 2), Math.Round(duration, 2), size && rate && decode.Code == 0 && duration > 1);
        }
    }
    sealed record ClipCheck(string Profile, string File, int Width, int Height, int ExpectedWidth, int ExpectedHeight, double FrameRate, double AverageFrameRate, double Duration, bool Ok);

    // --close-test: Shadow Recording plus a manual recording of a moving test window, then the title-bar Close button (UI Automation
    // Invoke → CloseClick → Close → OnClosing → Quit). Before closing it writes what the script must find afterwards.
    async Task CloseTest(string report)
    {
        Left = -20000; ShowInTaskbar = false;
        var (target, motion) = TestSourceWindow("FRAME close test", 480, 300);
        bool isolated = Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase) && !settings.DesktopAudio && !settings.Microphone;
        if (!isolated || !ready) throw new InvalidOperationException("The close test needs isolated settings (capture folder inside --data, no audio) and a ready engine.");
        if (args.Contains("--close-stuck-audio"))
        {
            settings.DesktopAudio = true;
            AudioPipe.SourceFactory = (kind, _) => new SyntheticAudioSource { Kind = kind, DisposeWait = new TaskCompletionSource().Task };
        }
        target.Show(); motion.Start(); await Task.Delay(500);
        testSource = [TestTarget(target, "close-test")]; settings.ReplaySeconds = 30; settings.Fps = 30; settings.OutputHeight = 0; settings.BitrateMbps = 6;
        await Act(ToggleShadow); await Act(ToggleRecord); await Task.Delay(4000);
        if (args.Contains("--close-still")) { motion.Stop(); await Task.Delay(2000); }
        if (args.Contains("--close-stuck-action")) await actions.WaitAsync(); // deliberately never released: exercises the real quit deadline
        if (args.Contains("--close-stuck-export"))
        {
            await actions.WaitAsync();
            _ = Backend.Run(Backend.Resolve(settings.FfmpegPath), ["-v", "error", "-re", "-f", "lavfi", "-i", "color=size=32x32:rate=1", "-t", "3600", "-f", "null", "-"], 3600);
        }
        var close = Descendants<Button>(this).First(b => AutomationProperties.GetName(b) == "Close FRAME");
        var info = new { pid = Environment.ProcessId, ffmpeg = engine.ProcessIds.ToArray(), children = ChildProcesses("ffmpeg.exe"), output = settings.OutputDirectory, recovery = Path.Combine(data, "Recovery"), shadow = engine.Replay, state = engine.State.ToString(), tray = tray != null, hotkeys = hotkeys.Count, closeTooltip = close.ToolTip, recordedSeconds = engine.RecordedSeconds, closedAtUtc = DateTime.UtcNow.ToString("O") };
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        DiagnosticLog.Write("close-test", "Invoking the title-bar Close button");
        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(close).GetPattern(PatternInterface.Invoke)!).Invoke();
    }
}
