using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Frame.App;

// --game-test <json> (isolated --data): real-world stress test while the user plays. FRAME records the primary display
// (Shadow Recording, the user's normal path) with the real desktop audio (the game) plus a synthetic tone source in the
// microphone slot (the real microphone is never opened). A small top-most window flashes white every 10 s while the tone
// beeps (6 kHz, measurable under game audio with a narrow band-pass), so every clip carries A/V reference events. Saves
// go through RequestSaveReplay (the Alt+Z path, including the on-screen toasts) at fixed times; the user may also press
// the Replay hotkey. Every phase is time-stamped (UTC) for joining with an external performance-counter log. Before the
// engine stops, the session's segments are copied to <json>.segments for old/new export comparisons on real footage.
public partial class MainWindow
{
    async Task GameStressTest(string report)
    {
        var results = new List<object>(); var timeline = new List<object>(); var started = Stopwatch.StartNew();
        string exe = Backend.Resolve(settings.FfmpegPath), probeExe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        void Note(string what, object? detail = null) => timeline.Add(new { utc = DateTime.UtcNow.ToString("O"), t = Math.Round(started.Elapsed.TotalSeconds, 2), what, detail });
        int seconds = Array.IndexOf(args, "--game-seconds") is int gi and >= 0 ? int.Parse(args[gi + 1]) : 330;
        if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase)) settings.OutputDirectory = Path.Combine(data, "Clips");
        Directory.CreateDirectory(settings.OutputDirectory);
        var primary = displays.FirstOrDefault(d => d.Bounds.X == 0 && d.Bounds.Y == 0) ?? displays.First();
        testSource = [primary];
        settings.Fps = 60; settings.BitrateMbps = 20; settings.OutputHeight = 0; settings.ReplaySeconds = 300; settings.DesktopAudio = true; settings.Microphone = true; settings.ShowSaveNotifications = true; settings.MinimizeOnGameStart = false;
        SyntheticAudioSource? tone = null;
        AudioPipe.SourceFactory = (kind, id) => kind == AudioSourceKind.Desktop ? new WasapiSource(true, id) : tone = new SyntheticAudioSource { Kind = kind };
        // Reference flash: small, top-most, never activated (the game keeps focus), bottom-left of the primary display.
        var area = SystemParameters.WorkArea; var flash = new Border { Background = Brushes.Black };
        var marker = new Window { Title = "FRAME sync marker", Width = 140, Height = 90, Left = area.Left + 20, Top = area.Bottom - 110, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowActivated = false, ShowInTaskbar = false, Content = flash };
        marker.Show(); await Task.Delay(300);
        GetWindowRect(new WindowInteropHelper(marker).Handle, out var mr);
        string crop = $"crop={mr.Right - mr.Left - 20}:{mr.Bottom - mr.Top - 20}:{mr.Left - primary.Bounds.X + 10}:{mr.Top - primary.Bounds.Y + 10}";
        var saves = new List<object>(); var clips = new List<string>();
        var stop = new CancellationTokenSource(); Task? events = null;
        try
        {
            Note("start", new { primary.Name, primary.Bounds, crop, settings.Fps, settings.BitrateMbps, settings.ReplaySeconds });
            Note("phase:baseline (FRAME idle, Shadow off)"); await Task.Delay(40_000);
            Note("phase:shadow-start");
            var startWatch = Stopwatch.StartNew(); await Act(ToggleShadow);
            if (!engine.Replay) throw new InvalidOperationException("Shadow Recording did not start: " + Message.Text);
            Note("phase:shadow-on (steady, no saves)", new { startSeconds = Math.Round(startWatch.Elapsed.TotalSeconds, 2) });
            var shadowClock = Stopwatch.StartNew();
            events = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    await Dispatcher.InvokeAsync(() => { flash.Background = Brushes.White; tone?.Beep(.3, 6000); });
                    var at = DateTime.UtcNow; await Task.Delay(300); await Dispatcher.InvokeAsync(() => flash.Background = Brushes.Black);
                    lock (timeline) timeline.Add(new { utc = at.ToString("O"), t = Math.Round(started.Elapsed.TotalSeconds, 2), what = "flash+beep", detail = (object?)null });
                    try { await Task.Delay(9700, stop.Token); } catch (OperationCanceledException) { }
                }
            });
            async Task Save(string name)
            {
                int before = Directory.Exists(settings.OutputDirectory) ? Directory.EnumerateFiles(settings.OutputDirectory, "*.mp4", SearchOption.AllDirectories).Count() : 0;
                Note("save-request " + name); var w = Stopwatch.StartNew(); double cpu = Backend.RunCpuSeconds;
                await RequestSaveReplay();
                double total = w.Elapsed.TotalSeconds;
                var file = Directory.EnumerateFiles(settings.OutputDirectory, "*.mp4", SearchOption.AllDirectories).Where(f => !f.EndsWith(".partial.mp4")).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                string log = string.Join(" | ", DiagnosticLog.ReadLines().Where(l => l.Contains("replay-")).TakeLast(2).Select(l => Regex.Match(l, "\"Message\":\"(.*?)\"").Groups[1].Value));
                Note("save-done " + name, new { seconds = Math.Round(total, 2), ffmpegCpu = Math.Round(Backend.RunCpuSeconds - cpu, 2), file = file == null ? null : Path.GetFileName(file), toast = SaveOverlay.History.LastOrDefault(), log });
                saves.Add(new { name, seconds = Math.Round(total, 2), ffmpegCpu = Math.Round(Backend.RunCpuSeconds - cpu, 2), file = file == null ? null : Path.GetFileName(file), toast = SaveOverlay.History.LastOrDefault(), log });
                if (file != null) clips.Add(file);
            }
            async Task Until(double t) { while (shadowClock.Elapsed.TotalSeconds < t) await Task.Delay(200); }
            await Until(Math.Min(135, seconds - 60)); await Save("A (~2 min history)");
            await Until(Math.Min(200, seconds - 30)); Note("phase:consecutive saves"); await Save("B1"); await Save("B2"); await Save("B3");
            await Until(seconds); await Save("C (5 min history)");
            stop.Cancel(); if (events != null) await events;
            // Real gameplay segments for identical-material export comparisons (copied before the engine removes them).
            var p = engine.Pipelines.FirstOrDefault();
            if (p != null)
            {
                Note("phase:copy segments"); p.Refresh(); string dest = report + ".segments"; Directory.CreateDirectory(dest);
                foreach (var s in p.Segments) File.Copy(s.Path, Path.Combine(dest, Path.GetFileName(s.Path)), true);
                await File.WriteAllLinesAsync(Path.Combine(dest, "segments.csv"), p.Segments.OrderBy(s => s.Start).Select(s => $"{Path.GetFileName(s.Path)},{s.Start.ToString("0.000000", CultureInfo.InvariantCulture)},{s.End.ToString("0.000000", CultureInfo.InvariantCulture)}"));
                Note("segments copied", new { count = p.Segments.Count, dest });
            }
            Note("phase:shadow-stop"); await Act(ToggleShadow); Note("phase:after (Shadow off)"); await Task.Delay(15_000);
            Note("phase:analysis (decoding clips; not part of the gameplay measurement)");
            foreach (var clip in clips.Distinct()) results.Add(await Analyze(clip));
        }
        catch (Exception e) { results.Add(new { error = e.ToString() }); }
        finally
        {
            stop.Cancel(); AudioPipe.SourceFactory = null; marker.Close();
            try { if (engine.Replay) await Act(ToggleShadow); } catch { }
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { saves, clips = results, timeline }, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Offsets: flash onset (luma of the marker region) vs 6 kHz tone onset (band-passed); continuity from packets.
        async Task<object> Analyze(string file)
        {
            var luma = await Backend.Run(exe, ["-hide_banner", "-i", file, "-an", "-vf", crop + ",signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-f", "null", "-"], 900);
            var flashes = new List<double>(); double? lastT = null; bool white = false;
            foreach (var line in luma.Text.Split('\n'))
            {
                var tm = Regex.Match(line, @"pts_time:([0-9.]+)"); if (tm.Success) lastT = double.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture);
                var y = Regex.Match(line, @"YAVG=([0-9.]+)");
                if (y.Success && lastT is double at) { double lum = double.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture); if (lum > 150 && !white) flashes.Add(at); white = lum > 150; }
            }
            var sil = await Backend.Run(exe, ["-hide_banner", "-i", file, "-vn", "-af", "highpass=f=5500,lowpass=f=6500,highpass=f=5500,lowpass=f=6500,silencedetect=n=-32dB:d=0.12", "-f", "null", "-"], 900);
            var tones = Regex.Matches(sil.Text, @"silence_end: ([0-9.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            var offsets = flashes.Select(f => tones.Where(t => t - f > -.3 && t - f < .6).Select(t => Math.Round(t - f, 3)).DefaultIfEmpty(double.NaN).First()).ToArray();
            async Task<double[]> Times(string stream) { var r = await Backend.Run(probeExe, ["-v", "error", "-select_streams", stream, "-show_entries", "packet=pts_time", "-of", "csv=p=0", file], 300); return r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => double.TryParse(x.Trim().TrimEnd(','), CultureInfo.InvariantCulture, out var x2) ? x2 : double.NaN).Where(double.IsFinite).Order().ToArray(); }
            var v = await Times("v:0"); var a = await Times("a:0");
            var vs = v.Zip(v.Skip(1), (x, y) => y - x).ToArray(); var steps = a.Zip(a.Skip(1), (x, y) => y - x).ToArray();
            var info = await Backend.Run(probeExe, ["-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "csv=p=0", file]);
            var good = offsets.Where(double.IsFinite).ToArray();
            return new { file = Path.GetFileName(file), streams = info.Text.Trim().Replace("\r", "").Split('\n'), flashes = flashes.Count, matched = good.Length, offsetsMs = good.Select(o => Math.Round(o * 1000)).ToArray(), meanMs = good.Length == 0 ? double.NaN : Math.Round(good.Average() * 1000, 1), minMs = good.Length == 0 ? double.NaN : Math.Round(good.Min() * 1000), maxMs = good.Length == 0 ? double.NaN : Math.Round(good.Max() * 1000), videoFrames = v.Length, videoGapsOver25ms = vs.Count(x => x > .025), longestVideoStepMs = vs.Length == 0 ? 0 : Math.Round(vs.Max() * 1000, 1), audioPackets = a.Length, longestAudioStepMs = steps.Length == 0 ? 0 : Math.Round(steps.Max() * 1000, 1) };
        }
    }
}
