using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Frame.App;

// --capture-ui-test <json> (isolated --data, 0.5.5): real Shadow Recording of a small window this test controls, with
// synthetic digital audio (no audio device is opened or changed and nothing is played). Checks
//  - the Capture page does not move: positions of its main controls in idle, starting, active, saving, a replay-length
//    change, manual recording, an audio-interruption note and stopping, and again at the minimum window size;
//  - the Shadow status dot follows the engine (off / starting-stopping / active);
//  - recorded level: a 1 kHz tone at a known digital level through FRAME's mixer, FFmpeg/AAC and the replay and manual
//    recording exports, compared with the same tone rendered by FFmpeg directly (peak, RMS, integrated loudness);
//  - FRAME makes no sound: afterwards the default playback device has no audio session of this process.
public partial class MainWindow
{
    async Task CaptureLayoutTest(string report)
    {
        var results = new List<object>(); string exe = Backend.Resolve(settings.FfmpegPath);
        int passedCount = 0; void Check(string name, bool ok, object? detail = null) { results.Add(new { name, passed = ok, detail }); if (ok) passedCount++; }
        var bar = new Rectangle { Width = 40, Height = 120, Fill = Brushes.Orange };
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(20, 30, 50)) }; canvas.Children.Add(bar);
        var area = SystemParameters.WorkArea;
        var target = new Window { Title = "FRAME layout test", Width = 360, Height = 220, Left = area.Right - 380, Top = area.Bottom - 240, WindowStyle = WindowStyle.ToolWindow, ShowActivated = false, ShowInTaskbar = false, Content = canvas, ResizeMode = ResizeMode.NoResize };
        target.Show();
        int tick = 0; var motion = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        motion.Tick += (_, _) => { tick++; Canvas.SetLeft(bar, tick * 7 % 320); };
        double toneLevel = .5; var created = new List<SyntheticAudioSource>();
        AudioPipe.SourceFactory = (kind, _) => { var s = new SyntheticAudioSource { Kind = kind, Amplitude = toneLevel }; if (kind == AudioSourceKind.Desktop) s.Beep(3600); lock (created) created.Add(s); return s; };
        async Task<bool> WaitFor(Func<bool> condition, double seconds) { var w = Stopwatch.StartNew(); while (w.Elapsed.TotalSeconds < seconds) { if (condition()) return true; await Task.Delay(20); } return condition(); }
        Dictionary<string, Point> Snapshot()
        {
            UpdateLayout();
            var stone = (FrameworkElement)ShadowButton.Template.FindName("Stone", ShadowButton);
            var items = new (string Name, FrameworkElement Element)[] { ("dot", ShadowStatusDot), ("slider", HistorySlider), ("stone", stone), ("folderButtons", CaptureFolderButton), ("record", RecordButton), ("save", ReplaySaveButton), ("shortcuts", ShortcutRows), ("note", ReplayInfo), ("source", SourceExpander), ("quality", PresetCombo), ("desktopAudio", DesktopCheck) };
            return items.ToDictionary(i => i.Name, i => { var p = i.Element.TranslatePoint(new Point(), this); return new Point(Math.Round(p.X, 2), Math.Round(p.Y, 2)); });
        }
        var states = new Dictionary<string, Dictionary<string, Point>>();
        double Moved(Dictionary<string, Point> a, Dictionary<string, Point> b) => a.Keys.Max(k => Math.Max(Math.Abs(a[k].X - b[k].X), Math.Abs(a[k].Y - b[k].Y)));
        string Newest() => new DirectoryInfo(settings.OutputDirectory).EnumerateFiles("*.mp4", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).First().FullName;
        try
        {
            if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase)) settings.OutputDirectory = Path.Combine(data, "Clips");
            Directory.CreateDirectory(settings.OutputDirectory);
            Check("Isolated test settings", Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase) && ready, settings.OutputDirectory);
            await Task.Delay(500);
            var hwnd = new WindowInteropHelper(target).Handle; GetWindowRect(hwnd, out var r);
            testSource = [new CaptureTarget("layout-test", "Layout test window", new Box(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), 0, hwnd)];
            settings.ReplaySeconds = 30; settings.Fps = 30; settings.BitrateMbps = 6; settings.OutputHeight = 0; settings.DesktopAudio = true; settings.Microphone = false; settings.ShowSaveNotifications = false;
            ShowPage(HomePage, "Capture", ""); UpdateState(); await Task.Delay(300);
            states["idle"] = Snapshot(); Check("Dot: grey while Shadow Recording is off", ShadowDotState == "off");

            // Start: snapshot while starting (dot = transition), then active.
            var starting = Act(ToggleShadow);
            bool sawStarting = await WaitFor(() => shadowTransition, 3); if (sawStarting) { states["starting"] = Snapshot(); }
            string startingDot = ShadowDotState; await starting; motion.Start(); await Task.Delay(400);
            states["active"] = Snapshot();
            Check("Dot: starting/stopping ring while starting, amber once the engine confirms", engine.Replay && (!sawStarting || startingDot == "transition") && ShadowDotState == "active", new { sawStarting, startingDot, now = ShadowDotState });

            // Level 1: replay save (stream copy + exact head) of a -6 dBFS tone.
            await Task.Delay(8000);
            var saving = RequestSaveReplay(); bool sawSaving = await WaitFor(() => savingReplay, 2); if (sawSaving) states["saving"] = Snapshot(); await saving;
            var replayLevel = await Levels(Newest(), toneLevel);
            Check("Replay clip level equals the input tone (no gain change through mixer, FFmpeg/AAC and the replay export)", replayLevel.Matches, replayLevel);

            // Replay length 15 min while recording: labels follow, nothing moves.
            CommitHistory(900); await Task.Delay(200); states["length-15min"] = Snapshot();
            Check("Replay length 15 min kept while recording (slider, labels, settings file)", HistorySlider.Seconds == 900 && settings.ReplaySeconds == 900 && SettingsStore.Load(configPath).ReplaySeconds == 900 && ReplaySaveTitle.Text == "Save last 15 minutes" && SaveHotkeyLabel.Text == "Hotkey for saving the 15 minutes", new { HistorySlider.Seconds, ReplaySaveTitle.Text, label = SaveHotkeyLabel.Text });

            // Level 2: manual recording (re-encoded export), with Pause shown in the record row.
            await Act(ToggleRecord); await Task.Delay(500); states["manual"] = Snapshot(); bool pauseShown = PauseButton.Visibility == Visibility.Visible;
            await Task.Delay(4000); await Act(ToggleRecord);
            var manualLevel = await Levels(Newest(), toneLevel);
            Check("Manual recording level equals the input tone", manualLevel.Matches && pauseShown, manualLevel);

            // Audio interruption: the note appears in its reserved slot.
            lock (created) created.Last(s => s.Kind == AudioSourceKind.Desktop && !s.Disposed).Fail(new COMException("Device invalidated (test).", unchecked((int)0x88890004)));
            bool noted = await WaitFor(() => ReplayInfo.Visibility == Visibility.Visible && ReplayInfo.Text.Length > 0, 8); if (noted) states["audio-note"] = Snapshot();
            Check("Audio interruption note is shown in the reserved slot", noted, ReplayInfo.Text);
            await WaitFor(() => ReplayInfo.Visibility != Visibility.Visible, 10);

            // Stop: ring while stopping, grey after.
            var stopping = RequestShadowToggle(); bool sawStopping = await WaitFor(() => stopRequested || shadowTransition, 2); string stoppingDot = ShadowDotState; if (sawStopping) states["stopping"] = Snapshot();
            await stopping; await Task.Delay(300); states["stopped"] = Snapshot();
            Check("Dot: ring while stopping, grey after the stop", !engine.Replay && (!sawStopping || stoppingDot == "transition") && ShadowDotState == "off", new { sawStopping, stoppingDot, now = ShadowDotState });

            // Near full scale (-0.4 dBFS): no clipping or level change.
            toneLevel = .95; await Act(ToggleShadow); await Task.Delay(7000);
            await RequestSaveReplay(); var loud = await Levels(Newest(), toneLevel);
            Check("Near-full-scale tone (-0.4 dBFS): same level, no clipping (flat factor 0, peak below 0 dBFS)", loud.Matches && loud.Peak < 0 && loud.Flat < .5, loud);
            await RequestShadowToggle();

            var baseline = states["idle"];
            var moves = states.ToDictionary(s => s.Key, s => Moved(baseline, s.Value));
            Check("Capture page does not move: idle, starting, active, saving, 15 min, manual recording, audio note, stopping, stopped (max shift px)", moves.Values.Max() <= .5, moves);

            // Minimum window size: idle vs active.
            Width = MinWidth; Height = MinHeight; await Task.Delay(300); var minIdle = Snapshot();
            await Act(ToggleShadow); motion.Start(); await Task.Delay(1500); var minActive = Snapshot(); await RequestShadowToggle();
            Check("Minimum window size: starting Shadow Recording moves nothing", Moved(minIdle, minActive) <= .5, new { shift = Moved(minIdle, minActive) });

            // FRAME made no sound: no playback session of this process.
            var sessions = PlaybackSessionsOf(Environment.ProcessId);
            Check("No audio playback session of FRAME on the default output device (FRAME produced no sound)", sessions == 0, new { sessions });
            File.WriteAllText(Path.ChangeExtension(report, ".states.json"), JsonSerializer.Serialize(states.ToDictionary(s => s.Key, s => s.Value.ToDictionary(p => p.Key, p => $"{p.Value.X:0.##},{p.Value.Y:0.##}")), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Check("Unexpected failure", false, e.ToString()); }
        finally
        {
            motion.Stop(); if (target.IsVisible) target.Close(); AudioPipe.SourceFactory = null;
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = passedCount, total = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Peak/RMS (astats) and integrated loudness (ebur128) of 2.0–4.4 s of a file, against FFmpeg's own rendering of the same
        // tone (aevalsrc, stereo 48 kHz, AAC 192k like FRAME and as PCM); "Matches" = within 0.3 dB of the AAC reference. The file
        // is decoded from its start and trimmed by a filter: an input seek (-ss) starts AAC decoding without pre-roll, and that
        // transient alone read 0.3 dB "too loud" in the first run.
        async Task<Level> Levels(string file, double amplitude)
        {
            async Task<(double Peak, double Rms, double Flat, double Lufs)> Measure(string path)
            {
                var m = await Backend.Run(exe, ["-hide_banner", "-i", path, "-map", "0:a", "-af", "atrim=start=2:duration=2.4,astats=measure_perchannel=none,ebur128", "-f", "null", "-"], 120);
                double Get(string pattern) { var x = Regex.Matches(m.Text, pattern); return x.Count == 0 ? double.NaN : double.Parse(x[^1].Groups[1].Value, CultureInfo.InvariantCulture); }
                return (Get(@"Peak level dB: (-?[0-9.]+|-inf)"), Get(@"RMS level dB: (-?[0-9.]+)"), Get(@"Flat factor: (-?[0-9.]+)"), Get(@"I:\s+(-?[0-9.]+) LUFS"));
            }
            string a = amplitude.ToString(CultureInfo.InvariantCulture), refAac = Path.Combine(data, $"ref-{a}.m4a"), refWav = Path.Combine(data, $"ref-{a}.wav");
            string expr = $"{a}*sin(2*PI*1000*t)|{a}*sin(2*PI*1000*t)";
            await Backend.Run(exe, ["-v", "error", "-y", "-f", "lavfi", "-i", $"aevalsrc={expr}:s=48000:d=8", "-c:a", "aac", "-b:a", "192k", refAac], 60);
            await Backend.Run(exe, ["-v", "error", "-y", "-f", "lavfi", "-i", $"aevalsrc={expr}:s=48000:d=8", "-c:a", "pcm_s16le", refWav], 60);
            var got = await Measure(file); var aac = await Measure(refAac); var pcm = await Measure(refWav);
            bool matches = Math.Abs(got.Peak - aac.Peak) <= .3 && Math.Abs(got.Rms - aac.Rms) <= .3 && Math.Abs(got.Lufs - aac.Lufs) <= .3;
            return new Level(Path.GetFileName(file), amplitude, Math.Round(20 * Math.Log10(amplitude), 2), got.Peak, got.Rms, got.Lufs, got.Flat, aac.Peak, aac.Rms, aac.Lufs, pcm.Peak, pcm.Rms, pcm.Lufs, matches);
        }
    }
    sealed record Level(string File, double Amplitude, double InputPeakDb, double Peak, double Rms, double Lufs, double Flat, double RefAacPeak, double RefAacRms, double RefAacLufs, double RefPcmPeak, double RefPcmRms, double RefPcmLufs, bool Matches);

    // Audio sessions of a process on the default playback endpoint (Core Audio session enumeration; read-only). A process
    // that played any sound (SoundPlayer, message-box beep, a render stream) has a session there for its lifetime.
    static int PlaybackSessionsOf(int pid)
    {
        var enumerator = (ISessionDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
        var iid = typeof(IAudioSessionManager2).GUID; Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var manager));
        Marshal.ThrowExceptionForHR(((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions));
        Marshal.ThrowExceptionForHR(sessions.GetCount(out int count)); int mine = 0;
        for (int i = 0; i < count; i++) if (sessions.GetSession(i, out var control) == 0 && control is IAudioSessionControl2 c && c.GetProcessId(out uint owner) == 0 && owner == pid) mine++;
        return mine;
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out ISessionDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessionDevice
    { [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(nint group, uint flags, out nint control); [PreserveSig] int GetSimpleAudioVolume(nint group, uint flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionEnumerator
    { [PreserveSig] int GetCount(out int count); [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session); }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state); [PreserveSig] int GetDisplayName(out nint name); [PreserveSig] int SetDisplayName(nint name, nint context);
        [PreserveSig] int GetIconPath(out nint path); [PreserveSig] int SetIconPath(nint path, nint context); [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(nint group, nint context); [PreserveSig] int RegisterAudioSessionNotification(nint client); [PreserveSig] int UnregisterAudioSessionNotification(nint client);
        [PreserveSig] int GetSessionIdentifier(out nint id); [PreserveSig] int GetSessionInstanceIdentifier(out nint id); [PreserveSig] int GetProcessId(out uint pid);
    }
}
