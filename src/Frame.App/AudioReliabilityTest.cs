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

// --audio-test <json> (with an isolated --data folder): real Windows Graphics Capture of a small test window through
// FRAME's own Shadow pipeline with audio enabled. Audio comes from SyntheticAudioSource (wall-clock paced, digital
// silence unless a test beep is scheduled; no Windows audio device is opened or changed). Device loss, failed restarts
// and default-device changes are simulated through the same code paths the real WASAPI/notification events use.
// "--real-audio" instead runs a short check with the real Windows default devices (read-only shared-mode capture,
// exactly what FRAME does in normal use; no device, volume or default is changed) and deletes its clips afterwards.
public partial class MainWindow
{
    async Task AudioReliabilityTest(string report)
    {
        var results = new List<object>(); var timeline = new List<object>(); var started = Stopwatch.StartNew();
        string exe = Backend.Resolve(settings.FfmpegPath), probeExe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        void Note(string what, object? detail = null) => timeline.Add(new { t = Math.Round(started.Elapsed.TotalSeconds, 2), what, detail });
        void Check(string name, bool ok, object? detail = null) { results.Add(new { name, passed = ok, detail }); Note((ok ? "PASS " : "FAIL ") + name); }
        var bar = new Rectangle { Width = 30, Height = 60, Fill = Brushes.DimGray };
        var canvas = new Canvas { Background = Brushes.Black }; canvas.Children.Add(bar);
        var area = SystemParameters.WorkArea;
        var target = new Window { Title = "FRAME audio test", Width = 360, Height = 220, Left = area.Right - 380, Top = area.Bottom - 240, WindowStyle = WindowStyle.ToolWindow, ShowActivated = false, ShowInTaskbar = false, Content = canvas, ResizeMode = ResizeMode.NoResize };
        target.Show();
        int tick = 0; var motion = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        motion.Tick += (_, _) => { tick++; Canvas.SetLeft(bar, tick * 7 % 320); };
        var faults = new List<RecordingFault>(); engine.FaultRaised += f => { lock (faults) faults.Add(f); };
        var created = new List<SyntheticAudioSource>(); int failStarts = 0;
        bool realAudio = args.Contains("--real-audio");
        AudioPipe? Pipe() => engine.Pipelines.FirstOrDefault()?.Audio;
        SyntheticAudioSource Latest(AudioSourceKind kind) { lock (created) return created.Last(s => s.Kind == kind && !s.Disposed); }
        object Stats() { var p = Pipe(); return new { replay = engine.Replay, faults = faults.Count, frames = engine.Pipelines.FirstOrDefault()?.Frames ?? -1, produced = p?.ChunksProduced, written = p?.ChunksWritten, backlog = p?.BacklogChunks, evicted = p?.EvictedChunks, longestWriteMs = p == null ? -1 : Math.Round(p.LongestWriteMs), blockedS = p == null ? -1 : Math.Round(p.WriteBlockedSeconds, 2), interruptions = p?.Interruptions, recoveries = p?.Recoveries, glitches = p?.Glitches, down = audioDown.Select(a => a.Kind.ToString()).ToArray() }; }
        async Task Start(bool desktop, bool mic)
        {
            settings.DesktopAudio = desktop; settings.Microphone = mic; settings.InputDevice = settings.OutputDevice = "";
            SaveOverlay.History.Clear(); lock (faults) faults.Clear(); fault = null;
            await Act(ToggleShadow); await Task.Delay(300);
            if (!engine.Replay) throw new InvalidOperationException("Shadow Recording did not start: " + Message.Text);
        }
        async Task Stop() { motion.Start(); try { if (engine.Replay) await Act(ToggleShadow); } finally { motion.Stop(); } }
        async Task<bool> WaitFor(Func<bool> condition, double seconds) { var w = Stopwatch.StartNew(); while (w.Elapsed.TotalSeconds < seconds) { if (condition()) return true; await Task.Delay(50); } return condition(); }
        async Task FlashAndBeepVideoOnly() { canvas.Background = Brushes.White; await Task.Delay(300); canvas.Background = Brushes.Black; }
        async Task FlashAndBeep()
        {
            canvas.Background = Brushes.White; if (!realAudio) Latest(AudioSourceKind.Desktop).Beep(.3);
            await Task.Delay(300); canvas.Background = Brushes.Black;
        }
        object Segs() { var p = engine.Pipelines.FirstOrDefault(); if (p == null) return "none"; p.Refresh(); var sg = p.Segments.OrderBy(x => x.Start).ToArray(); var gaps = sg.Zip(sg.Skip(1)).Where(z => z.Second.Start - z.First.End > .05).Select(z => $"{z.First.End:0.00}->{z.Second.Start:0.00}").ToArray(); return new { count = sg.Length, first = sg.FirstOrDefault()?.Start, last = sg.LastOrDefault()?.End, gaps, longest = sg.Length == 0 ? 0 : Math.Round(sg.Max(x => x.End - x.Start), 2), position = Math.Round(p.Position, 2), liveEdge = Math.Round(p.LiveEdge, 2), frames = p.Frames, csvTail = ReadShared(Path.Combine(p.Folder, "segments.csv")) }; }
        static string ReadShared(string path) { if (!File.Exists(path)) return ""; using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var rd = new StreamReader(f); return string.Join(" | ", rd.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(6)); }
        async Task<string> Save(int seconds)
        {
            settings.ReplaySeconds = seconds; var edges = engine.RequestEdges(); var files = await engine.SaveReplay(edges); return files[0];
        }
        try
        {
            // Clips must never land in the user's folders: an empty --data folder has the default capture folder.
            if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase)) settings.OutputDirectory = Path.Combine(data, "Clips");
            Directory.CreateDirectory(settings.OutputDirectory);
            Check("Isolated test settings loaded", Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase), settings.OutputDirectory);
            await Task.Delay(500);
            var hwnd = new WindowInteropHelper(target).Handle; GetWindowRect(hwnd, out var r);
            testSource = [new CaptureTarget("audio-test", "Audio test window", new Box(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), 0, hwnd)];
            settings.ReplaySeconds = 30; settings.Fps = 30; settings.BitrateMbps = 6; settings.OutputHeight = 0;
            if (realAudio) { await RealAudio(); return; }
            bool syncOnly = args.Contains("--sync-only");
            AudioPipe.SourceFactory = (kind, _) =>
            {
                var s = new SyntheticAudioSource { Kind = kind, FailStart = Interlocked.Decrement(ref failStarts) >= 0 ? new COMException("Element not found.", unchecked((int)0x80070490)) : null };
                if (failStarts < 0) Interlocked.Exchange(ref failStarts, 0);
                lock (created) created.Add(s); return s;
            };

            // N. Diagnostic comparison: the same still -> motion -> save sequence with audio OFF.
            if (args.Contains("--no-audio-compare"))
            {
                bool withAudio = args.Contains("--with-audio");
                await Start(withAudio, false); motion.Start(); await Task.Delay(3000); if (withAudio) await FlashAndBeep(); else await FlashAndBeepVideoOnly(); await Task.Delay(2000); motion.Stop();
                int stillMs = Array.IndexOf(args, "--still") is int si and >= 0 ? int.Parse(args[si + 1]) * 1000 : 10000;
                if (args.Contains("--save-during-still")) { await Task.Delay(4000); var mid = await Inspect(await Save(8)); Note("N: saved during still", new { mid, segs = Segs() }); stillMs = Math.Max(0, stillMs - 4000); }
                await Task.Delay(stillMs); Note("N: before resume", Segs()); Note("N: audio", Stats()); motion.Start();
                for (int k = 0; k < 6; k++) { await Task.Delay(500); Note("N: resume +" + (k + 1) * .5, new { segs = Segs(), stats = Stats() }); }
                if (withAudio) await FlashAndBeep(); else await FlashAndBeepVideoOnly(); await Task.Delay(2500); motion.Stop();
                Note("N: before save", Segs());
                var clip = await Inspect(await Save(25));
                Check("N Window spanning still -> motion: the flash after the resume is in the clip" + (withAudio ? ", in sync, with matching A/V lengths" : ""), clip.Flashes.Length >= 1 && clip.Flashes[^1] > clip.Video - 4 && (!withAudio || (clip.Offsets.Length >= 1 && Math.Abs(clip.Video - clip.Audio) < .15)), new { clip, segs = Segs() });
                await Stop(); return;
            }
            // T. 0.5.4 sync + fast save: flash + beep every 5 s (alternating desktop / microphone source) during motion;
            // the same footage saved by the stream-copy path and by the previous full re-encode; then a still-screen
            // tail save and repeated saves. Measures offsets along the clip, drift, continuity, duration, time and CPU.
            if (args.Contains("--sync-test"))
            {
                int length = Array.IndexOf(args, "--sync-seconds") is int li and >= 0 ? int.Parse(args[li + 1]) : 60;
                settings.ReplaySeconds = length; // the engine takes the history length at start
                await Start(true, true); motion.Start();
                bool sourcesLive = await WaitFor(() => Pipe()?.Sources.All(s => s.Live) == true, 5);
                await Task.Delay(2500);
                var events = new List<string>(); var clock = Stopwatch.StartNew();
                for (int k = 0; clock.Elapsed.TotalSeconds < length + 8; k++)
                {
                    var kind = k % 2 == 0 ? AudioSourceKind.Desktop : AudioSourceKind.Microphone;
                    canvas.Background = Brushes.White; Latest(kind).Beep(.3); events.Add(kind.ToString());
                    await Task.Delay(300); canvas.Background = Brushes.Black; await Task.Delay(4700);
                }
                await Task.Delay(1500);
                var p0 = engine.Pipelines[0];
                Note("T: start alignment", new { audioPipeOpened = p0.AudioConnectedAfter, firstVideoFrame = p0.VideoStartedAfter, legacyOffsetWouldBe = p0.VideoStartedAfter - p0.AudioConnectedAfter, trimmedSamples = p0.Audio?.TrimmedSamples });
                // New path (through the normal save command, while recording continues).
                double cpu = Backend.RunCpuSeconds; var timer = Stopwatch.StartNew();
                var edges = engine.RequestEdges(); var fastFile = (await engine.SaveReplay(edges))[0];
                double fastSeconds = timer.Elapsed.TotalSeconds, fastCpu = Backend.RunCpuSeconds - cpu;
                // Previous path on the same footage (forced full re-encode); 10 s shorter so retention cannot remove its first segment meanwhile.
                p0.Refresh(); var window = ReplayWindow.Live(p0.Segments, length - 10, edges[p0.Label])!;
                cpu = Backend.RunCpuSeconds; timer.Restart();
                var oldFile = window.NeedsTail ? await MediaExport.ExportLive(exe, settings, p0, window, "Replay old-path") : await MediaExport.Export(exe, settings, p0, [window.Range], "Replay old-path");
                double oldSeconds = timer.Elapsed.TotalSeconds, oldCpu = Backend.RunCpuSeconds - cpu;
                var fast = await Inspect(fastFile); var old = await Inspect(oldFile);
                var fastFlow = await Continuity(fastFile); var oldFlow = await Continuity(oldFile);
                // Drift = linear trend of the offsets over the clip (least squares), not last minus first: single events jitter by about one captured frame.
                double Trend(double[] o) => Math.Round(MediaContinuity.Trend(o), 3); // Frame.Core: least-squares drift over the clip
                object Summary(AudioClip c) => new { trend = Trend(c.Offsets), mean = c.Offsets.Length == 0 ? double.NaN : Math.Round(c.Offsets.Average(), 3), first = c.Offsets.FirstOrDefault(), middle = c.Offsets.Length > 0 ? c.Offsets[c.Offsets.Length / 2] : double.NaN, last = c.Offsets.LastOrDefault(), min = c.Offsets.DefaultIfEmpty().Min(), max = c.Offsets.DefaultIfEmpty().Max(), drift = c.Offsets.Length > 1 ? Math.Round(c.Offsets[^1] - c.Offsets[0], 3) : double.NaN, events = c.Offsets.Length };
                Note("T: events", events);
                Check("T1 Stream-copy replay: every flash has its beep within ±50 ms (desktop and microphone), no drift", fast.Offsets.Length >= fast.Flashes.Count(f => f > .5) && fast.Flashes.Length >= length / 5 - 2 && fast.Offsets.All(o => Math.Abs(o) <= .05) && Math.Abs(Trend(fast.Offsets)) < .03 && sourcesLive, new { file = fast.File, sync = Summary(fast), fast.Offsets, fast.Video, fast.Audio, seconds = Math.Round(fastSeconds, 2), ffmpegCpu = Math.Round(fastCpu, 2), flow = fastFlow });
                Check("T2 Stream-copy replay: continuous video (no frame gaps), contiguous audio, requested length", fastFlow.VideoGaps == 0 && fastFlow.AudioProblem() == null && Math.Abs(fast.Video - fast.Audio) < .1 && fast.Video >= length - .1 && fast.Video <= length + 1.1, new { fastFlow, audioProblem = fastFlow.AudioProblem(), fast.Video, fast.Audio });
                Check("T3 Full re-encode (fallback) on the same footage: exact segment lengths, joined before decoding: in sync, continuous video and audio", old.Offsets.Length >= old.Flashes.Count(f => f > .5) && old.Offsets.All(o => Math.Abs(o) <= .05) && oldFlow.VideoGaps == 0 && oldFlow.AudioProblem(allowedStartOverlaps: 0) == null, new { file = old.File, sync = Summary(old), old.Offsets, old.Video, old.Audio, seconds = Math.Round(oldSeconds, 2), ffmpegCpu = Math.Round(oldCpu, 2), flow = oldFlow });
                Note("T: speed", new { fastSeconds = Math.Round(fastSeconds, 2), oldSeconds = Math.Round(oldSeconds, 2), fastCpu = Math.Round(fastCpu, 2), oldCpu = Math.Round(oldCpu, 2) });
                // Still-screen tail: last event, then no screen change for 6 s, save 10 s (needs the encoded tail).
                motion.Stop(); await Task.Delay(500);
                canvas.Background = Brushes.White; Latest(AudioSourceKind.Desktop).Beep(.3); await Task.Delay(300); canvas.Background = Brushes.Black;
                await Task.Delay(6000);
                p0.Refresh(); var tailWindow = ReplayWindow.Live(p0.Segments, length, p0.LiveEdge);
                var tailFile = (await engine.SaveReplay())[0]; double tailExpected = tailWindow == null ? 0 : tailWindow.Range.End - tailWindow.Range.Start;
                var tail = await Inspect(tailFile); var tailFlow = await Continuity(tailFile);
                string path = DiagnosticLog.ReadLines().LastOrDefault(l => l.Contains("replay-save")) ?? "";
                Check("T4 Still-screen save: tail handled, ends at the request, flash/beep in sync, continuous", tailWindow?.NeedsTail == true && tail.Offsets.Length >= tail.Flashes.Count(f => f > .5) && tail.Offsets.All(o => Math.Abs(o) <= .05) && tailFlow.VideoGaps == 0 && tailFlow.AudioProblem() == null && tail.Video >= tailExpected - .1 && tail.Video <= tailExpected + 1.1 && Math.Abs(tail.Video - tail.Audio) < .1, new { tail, tailFlow, tailExpected, needsTail = tailWindow?.NeedsTail, tailSeconds = tailWindow?.Tail, path });
                // Repeated saves while recording continues.
                motion.Start(); var repeated = new List<object>();
                for (int i = 0; i < 3; i++) { timer.Restart(); var f = (await engine.SaveReplay())[0]; var c = await Inspect(f); repeated.Add(new { c.File, c.Video, c.Audio, seconds = Math.Round(timer.Elapsed.TotalSeconds, 2) }); }
                motion.Stop();
                Check("T5 Three saves in a row while recording: all valid, no fault", repeated.Count == 3 && faults.Count == 0 && engine.Replay, repeated);
                await Stop(); return;
            }
            // S. Absolute A/V offset right after start (flash + beep during motion), used to compare pump designs.
            if (syncOnly)
            {
                await Start(true, false); motion.Start(); await Task.Delay(3000); await FlashAndBeep(); await Task.Delay(3000); await FlashAndBeep(); await Task.Delay(2000); motion.Stop();
                var clip = await Inspect(await Save(8));
                Check("S Absolute A/V offset (beep onset minus flash), seconds", clip.Offsets.Length >= 2 && Math.Abs(clip.Offsets[1] - clip.Offsets[0]) < .05, clip);
                await Stop(); return;
            }
            // A. Root cause regression: desktop audio (digital silence), still screen, then motion again (the user's sequence).
            await Start(true, false);
            motion.Start(); await Task.Delay(3000); await FlashAndBeep(); await Task.Delay(2000); motion.Stop();
            Note("A: still begins", Stats());
            double maxBlocked = 0; var still = Stopwatch.StartNew();
            while (still.Elapsed.TotalSeconds < 10) { await Task.Delay(200); maxBlocked = Math.Max(maxBlocked, Pipe()?.WriteBlockedSeconds ?? 0); }
            Note("A: after 10 s still", Stats());
            var stillSave = await Save(8); var stillClip = await Inspect(stillSave);
            Check("A1 FFmpeg stops reading audio during a still screen (backpressure observed), and it is absorbed", maxBlocked > 5 && faults.Count == 0 && engine.Replay, new { maxBlocked, stats = Stats() });
            Check("A2 Replay saved during the still period has audio and matching lengths", stillClip.HasAudio && Math.Abs(stillClip.Video - stillClip.Audio) < .15 && stillClip.Video > 7.5, stillClip);
            motion.Start(); await Task.Delay(3000); await FlashAndBeep(); await Task.Delay(1500); motion.Stop(); // the clip ends at the request (0.5.4): keep the first flash inside the 30 s window
            await WaitFor(() => (Pipe()?.BacklogChunks ?? 99) < 10, 10);
            Note("A: after motion resumed", Stats()); Note("A: segments before save", Segs());
            Check("A3 Motion after the still period: no fault (0.5.3/0.5.4 stopped here), backlog drained", faults.Count == 0 && engine.Replay && (Pipe()?.BacklogChunks ?? 99) < 10, Stats());
            var syncClip = await Inspect(await Save(25));
            bool syncOk = syncClip.Flashes.Length >= 2 && syncClip.Tones.Length >= 2 && syncClip.Offsets.Length >= 2 && Math.Abs(syncClip.Offsets[^1] - syncClip.Offsets[0]) < .05;
            Check("A4 A/V offset is unchanged across the still period (flash vs beep, relative)", syncOk && Math.Abs(syncClip.Video - syncClip.Audio) < .15, syncClip);
            await Stop();

            // B. Long still screen: memory stays bounded (30 s of real audio), the timeline stays exact.
            await Start(true, false);
            motion.Start(); await Task.Delay(2500); await FlashAndBeep(); await Task.Delay(1500);
            var beforeLong = await Inspect(await Save(8)); motion.Stop(); // this session's own A/V offset before the still
            long maxBacklog = 0; long privateBefore = Process.GetCurrentProcess().PrivateMemorySize64; still.Restart();
            while (still.Elapsed.TotalSeconds < 45) { await Task.Delay(500); maxBacklog = Math.Max(maxBacklog, Pipe()?.RealBacklogChunks ?? 0); }
            long privateDuring = Process.GetCurrentProcess().PrivateMemorySize64;
            Note("B: after 45 s still", Stats());
            motion.Start(); await Task.Delay(2000); await FlashAndBeep(); await Task.Delay(2000); motion.Stop();
            await WaitFor(() => (Pipe()?.BacklogChunks ?? 99) < 10, 20);
            var longClip = await Inspect(await Save(25));
            Check("B1 45 s still: no fault, real-audio backlog capped at 30 s, older audio turned into timed silence", faults.Count == 0 && engine.Replay && maxBacklog <= 1500 && (Pipe()?.EvictedChunks ?? 0) > 0, new { maxBacklog, privateMbDelta = Math.Round((privateDuring - privateBefore) / 1048576d, 1), stats = Stats() });
            Check("B2 After the long still: audio and video lengths match and the new beep is in sync", longClip.HasAudio && Math.Abs(longClip.Video - longClip.Audio) < .15 && longClip.Offsets.Length >= 1 && beforeLong.Offsets.Length >= 1 && Math.Abs(longClip.Offsets[^1] - beforeLong.Offsets[0]) < .06, new { before = beforeLong, after = longClip });
            await Stop();

            // C. Device problems with both sources: video continues, audio degrades to silence, recovers, user told once.
            await Start(true, true);
            motion.Start();
            await Task.Delay(2000);
            int framesBefore = (int)(engine.Pipelines[0].Frames);
            Interlocked.Exchange(ref failStarts, 3); // device gone: the next three restarts fail (0.5 s, 1 s, 2 s backoff)
            Latest(AudioSourceKind.Desktop).Fail(new COMException("The audio endpoint device has been invalidated.", unchecked((int)0x88890004)));
            bool noted = await WaitFor(() => SaveOverlay.History.Contains("Desktop audio interrupted"), 4);
            Note("C: desktop lost", Stats());
            Check("C1 Desktop device lost: recording continues, one 'Desktop audio interrupted' note, tray and status say so", noted && engine.Replay && faults.Count == 0 && audioDown.Any(a => a.Kind == AudioSourceKind.Desktop) && (TrayText ?? "").Contains("audio interrupted") && ReplayInfo.Text.Contains("Desktop audio interrupted") && engine.Pipelines[0].Frames > framesBefore, new { history = SaveOverlay.History.ToArray(), tray = TrayText, status = ReplayInfo.Text, stats = Stats() });
            var outageClip = await Inspect(await Save(8));
            Check("C2 Replay saved during the outage is valid, with audio track and matching lengths", outageClip.HasAudio && Math.Abs(outageClip.Video - outageClip.Audio) < .15, outageClip);
            bool restored = await WaitFor(() => audioDown.Count == 0 && SaveOverlay.History.Contains("Audio restored"), 15);
            Note("C: desktop back", Stats());
            Check("C3 Device back: audio restored automatically, 'Audio restored' once, no fault, no restart of the engine", restored && faults.Count == 0 && engine.Replay && (Pipe()?.Recoveries ?? 0) >= 1 && SaveOverlay.History.Count(h => h == "Desktop audio interrupted") == 1 && SaveOverlay.History.Count(h => h == "Audio restored") == 1 && !(TrayText ?? "").Contains("audio interrupted"), new { history = SaveOverlay.History.ToArray(), stats = Stats() });
            // Brief glitch (recovers before the 1.5 s grace): logged, not announced.
            int notesBefore = SaveOverlay.History.Count; Latest(AudioSourceKind.Microphone).Fail(new COMException("Device invalidated.", unchecked((int)0x88890004)));
            await WaitFor(() => audioDown.Any(), 1); bool back = await WaitFor(() => audioDown.Count == 0, 3); await Task.Delay(1500);
            Check("C4 Brief microphone glitch recovers silently (no note), recording continues", back && SaveOverlay.History.Count == notesBefore && faults.Count == 0 && engine.Replay, new { history = SaveOverlay.History.ToArray(), stats = Stats() });
            // Windows default device changed: switch without an outage.
            var oldDesktop = Latest(AudioSourceKind.Desktop); int sourcesBefore; lock (created) sourcesBefore = created.Count;
            bool anyDown = false; Pipe()!.SimulateDefaultChange(AudioSourceKind.Desktop);
            var sw = Stopwatch.StartNew(); while (sw.Elapsed.TotalSeconds < 1.5) { anyDown |= Pipe()!.Sources.Any(s => !s.Live); await Task.Delay(20); }
            int sourcesAfter; lock (created) sourcesAfter = created.Count;
            Check("C5 Default device change: new default opened, old released, no outage, no note", oldDesktop.Disposed && sourcesAfter == sourcesBefore + 1 && !anyDown && SaveOverlay.History.Count == notesBefore && engine.Replay, new { sourcesBefore, sourcesAfter, anyDown, stats = Stats() });
            // Everything unavailable for a while: "Audio interrupted · Recording continues without audio".
            Interlocked.Exchange(ref failStarts, 4);
            Latest(AudioSourceKind.Desktop).Fail(new COMException("Audio service stopped.", unchecked((int)0x88890010))); Latest(AudioSourceKind.Microphone).Fail(new COMException("Audio service stopped.", unchecked((int)0x88890010)));
            bool allNote = await WaitFor(() => SaveOverlay.History.Contains("Audio interrupted"), 4);
            bool allBack = await WaitFor(() => audioDown.Count == 0, 20);
            Check("C6 All audio unavailable: 'Audio interrupted · Recording continues without audio', then restored, video never stopped", allNote && allBack && faults.Count == 0 && engine.Replay, new { history = SaveOverlay.History.ToArray(), stats = Stats() });
            motion.Stop();
            await Stop();

            // D. Microphone only, muted (digital silence) over a still screen.
            await Start(false, true);
            motion.Start(); await Task.Delay(2000); motion.Stop(); await Task.Delay(8000); motion.Start(); await Task.Delay(2000); motion.Stop();
            Check("D Microphone only, muted, still screen: no fault, microphone live", faults.Count == 0 && engine.Replay && Pipe()!.Sources.All(s => s.Live), Stats());
            await Stop();

            // E. A microphone that is missing when recording starts: video starts anyway, audio follows when available.
            Interlocked.Exchange(ref failStarts, 1);
            await Start(true, false);
            bool live = await WaitFor(() => Pipe()?.Sources.All(s => s.Live) == true, 5);
            Check("E Audio device unavailable at start: Shadow Recording starts with video, audio joins after retry", engine.Replay && live && faults.Count == 0, Stats());
            await Stop();

            // F. Repeated start/stop cycles with both sources.
            var stops = new List<double>();
            for (int i = 0; i < 4; i++)
            {
                await Start(true, true); motion.Start(); await Task.Delay(2000);
                var w = Stopwatch.StartNew(); await Act(ToggleShadow); stops.Add(Math.Round(w.Elapsed.TotalSeconds, 2)); motion.Stop();
            }
            await Task.Delay(500);
            Check("F Four start/stop cycles with audio: clean stops, no fault, no warning, no leftover engine", faults.Count == 0 && !engine.Replay && !engine.ProcessIds.Any() && !SaveOverlay.History.Any(h => h.Contains("stopped")), new { stops, history = SaveOverlay.History.ToArray() });
        }
        catch (Exception e) { Check("Exception", false, e.ToString()); }
        finally
        {
            motion.Stop(); AudioPipe.SourceFactory = null;
            try { if (engine.Replay) await Act(ToggleShadow); } catch { }
            if (target.IsVisible) target.Close();
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = results.Count(x => (bool)x.GetType().GetProperty("passed")!.GetValue(x)!), total = results.Count, results, timeline }, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Real Windows default devices (desktop loopback + microphone), read-only. No playback is started; whatever the
        // PC plays (usually nothing) is captured. The recordings are deleted after measuring.
        async Task RealAudio()
        {
            await Start(true, true);
            Check("R1 Real default devices opened (desktop loopback + microphone) and the default-device watcher registered", Pipe()!.WatcherActive && await WaitFor(() => Pipe()!.Sources.All(s => s.Live), 8), new { sources = Pipe()!.Sources, stats = Stats() });
            motion.Start(); await Task.Delay(4000); motion.Stop(); await Task.Delay(10000); motion.Start(); await Task.Delay(4000); motion.Stop();
            await WaitFor(() => (Pipe()?.BacklogChunks ?? 99) < 10, 10);
            var clip = await Inspect(await Save(15));
            Check("R2 Real audio with a still period: no fault, both sources live, clip has audio with matching length", faults.Count == 0 && engine.Replay && Pipe()!.Sources.All(s => s.Live) && clip.HasAudio && Math.Abs(clip.Video - clip.Audio) < .15, new { clip.Video, clip.Audio, ringDrops = Pipe()!.DeviceGlitches.Dropped, discontinuities = Pipe()!.DeviceGlitches.Discontinuities, stats = Stats() });
            await Stop();
            foreach (var f in Directory.EnumerateFiles(settings.OutputDirectory, "*", SearchOption.AllDirectories)) try { File.Delete(f); } catch (IOException) { }
            Note("real-audio clips deleted");
        }

        // Packet timestamps: video steps longer than 1.5 frames are gaps; audio packets must follow each other exactly.
        async Task<Flow> Continuity(string file)
        {
            async Task<double[]> Times(string stream) { var r = await Backend.Run(probeExe, ["-v", "error", "-select_streams", stream, "-show_entries", "packet=pts_time", "-of", "csv=p=0", file], 120); return r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => double.TryParse(x.Trim().TrimEnd(','), CultureInfo.InvariantCulture, out var v) ? v : double.NaN).Where(double.IsFinite).Order().ToArray(); }
            var v = await Times("v:0"); var a = await Times("a:0");
            var vs = v.Zip(v.Skip(1), (x, y) => y - x).ToArray(); var steps = a.Zip(a.Skip(1), (x, y) => y - x).ToArray();
            double frame = 1d / settings.Fps, aac = 1024 / 48000d;
            return new Flow(v.Length, vs.Count(s => s > frame * 1.5), vs.Length == 0 ? 0 : Math.Round(vs.Max() * 1000, 1), vs.Count(s => s <= 0), a.Length, steps.Count(s => Math.Abs(s - aac) > .002), steps.Length == 0 ? 0 : Math.Round(steps.Max() * 1000, 1), MediaContinuity.Audio(a));
        }
        async Task<AudioClip> Inspect(string file)
        {
            var probe = await Backend.Run(probeExe, ["-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "json", file]);
            using var doc = JsonDocument.Parse(probe.Text);
            double Dur(string type) => doc.RootElement.GetProperty("streams").EnumerateArray().Where(s => s.GetProperty("codec_type").GetString() == type).Select(s => double.Parse(s.GetProperty("duration").GetString() ?? "0", CultureInfo.InvariantCulture)).FirstOrDefault();
            double video = Dur("video"), audio = Dur("audio");
            var luma = await Backend.Run(exe, ["-hide_banner", "-i", file, "-an", "-vf", "signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-f", "null", "-"], 120);
            var flashes = new List<double>(); double? lastT = null; bool white = false;
            foreach (var line in luma.Text.Split('\n'))
            {
                var t = Regex.Match(line, @"pts_time:([0-9.]+)"); if (t.Success) lastT = double.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
                var y = Regex.Match(line, @"YAVG=([0-9.]+)");
                if (y.Success && lastT is double at) { double v = double.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture); if (v > 150 && !white) flashes.Add(at); white = v > 150; }
            }
            var tones = new List<double>();
            if (audio > 0)
            {
                var sil = await Backend.Run(exe, ["-hide_banner", "-i", file, "-vn", "-af", "silencedetect=n=-30dB:d=0.1", "-f", "null", "-"], 120);
                tones = Regex.Matches(sil.Text, @"silence_end: ([0-9.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            }
            var offsets = flashes.Select(f => tones.Where(t => t - f > -.5 && t - f < 1.5).Select(t => Math.Round(t - f, 3)).DefaultIfEmpty(double.NaN).First()).Where(double.IsFinite).ToArray();
            return new AudioClip(Path.GetFileName(file), Math.Round(video, 3), Math.Round(audio, 3), audio > 0, flashes.Select(f => Math.Round(f, 3)).ToArray(), tones.Select(t => Math.Round(t, 3)).ToArray(), offsets);
        }
    }
    sealed record AudioClip(string File, double Video, double Audio, bool HasAudio, double[] Flashes, double[] Tones, double[] Offsets);
    sealed record Flow(int VideoPackets, int VideoGaps, double LongestVideoStepMs, int NonMonotonic, int AudioPackets, int AudioGaps, double LongestAudioStepMs, AudioContinuity Audio)
    {
        // Frame.Core rules: no hole, no duplicate; at most one partial overlap, only at the encoded start join (first second).
        public string? AudioProblem(int allowedStartOverlaps = 1) => MediaContinuity.AudioProblem(Audio, allowedStartOverlaps: allowedStartOverlaps);
    }
}
