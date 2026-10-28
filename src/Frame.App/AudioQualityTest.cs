using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;

namespace Frame.App;

public partial class MainWindow
{
    // Isolated diagnostic: owned windows and digital sources only. Retain raw PCM for an independent codec control.
    async Task AudioQualityTest(string report)
    {
        Left = -20000; ShowInTaskbar = false;
        if (!ready || !Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Audio quality diagnostics require isolated output and a ready engine.");
        string exe = Backend.Resolve(settings.FfmpegPath);
        var results = new List<object>(); var artifacts = new List<object>();
        async Task Save() => await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { results, artifacts }, new JsonSerializerOptions { WriteIndented = true }));
        async Task Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail }); await Save();
            if (!passed) DiagnosticLog.Warning("audio-quality", name + ": " + JsonSerializer.Serialize(detail));
        }
        try
        {
            if (Array.IndexOf(args, "--quality-fixtures") is int fi and >= 0)
            {
                string source = Path.GetFullPath(args[fi + 1]);
                if (!source.StartsWith(Path.GetDirectoryName(data)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixtures must be under the isolated test root.");
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(source, "result.json")));
                var fixtures = document.RootElement.GetProperty("artifacts").EnumerateArray().Where(x => x.TryGetProperty("amplitude", out _) && x.GetProperty("name").GetString()!.Contains("-replay-")).ToArray();
                foreach (var group in fixtures.GroupBy(x => x.GetProperty("name").GetString()!.Split("-replay-")[0]))
                {
                    var profile = Profile(); var encoder = ChooseEncoder();
                    var p = new CapturePipeline(exe, profile, encoder, [], Path.Combine(data, "fixtures"), group.Key) { HasAudioOverride = true };
                    string old = Path.Combine(source, group.Key + "-segments");
                    foreach (string file in Directory.GetFiles(old, "part-*.mkv").Append(Path.Combine(old, "segments.csv"))) File.Copy(file, Path.Combine(p.Folder, Path.GetFileName(file)));
                    p.Refresh();
                    foreach (var fixture in group)
                    {
                        string name = fixture.GetProperty("name").GetString()!;
                        double from = fixture.GetProperty("from").GetDouble(), to = fixture.GetProperty("to").GetDouble();
                        string file = await MediaExport.ExportReplay(exe, profile, p, new LiveWindow(new(from, to), 0), name);
                        artifacts.Add(new { name, file, amplitude = fixture.GetProperty("amplitude").GetDouble(), paced = fixture.GetProperty("paced").GetBoolean(), from, to, raw = fixture.GetProperty("raw").Clone(), before = fixture.GetProperty("file").GetString() });
                        await Check(name + ": deterministic recorded-source replay exported", File.Exists(file));
                    }
                }
                return;
            }
            if (!args.Contains("--quality-sync-only"))
                foreach (bool paced in new[] { true, false }) foreach (double amplitude in new[] { .5, .95 }) await Tone(paced, amplitude);
            if (!args.Contains("--quality-peaks-only"))
            {
                await Sync("short-still-stop", 4, 1800, false);
                await Sync("long-paused", 18, 4700, true);
            }
        }
        finally { AudioPipe.SourceFactory = null; await Save(); }

        Settings Profile(bool microphone = false)
        {
            var s = CloneSettings(); s.DesktopAudio = true; s.Microphone = microphone; s.Fps = 30;
            s.OutputHeight = 0; s.BitrateMbps = 6; s.ReplaySeconds = 30; s.Cursor = false; return s;
        }
        async Task Tone(bool paced, double amplitude)
        {
            string name = (paced ? "paced" : "exact") + "-" + Backend.F(amplitude);
            AudioPipe.SourceFactory = (kind, _) =>
            {
                if (!paced) return new ExactToneSource(amplitude);
                var s = new SyntheticAudioSource { Kind = kind, Amplitude = amplitude }; s.Beep(3600); return s;
            };
            var (target, motion) = TestSourceWindow("FRAME audio " + name, 480, 300); target.Show(); motion.Start();
            try
            {
                await Task.Delay(400); testSource = [TestTarget(target, name)]; engine.ResetFault();
                var profile = Profile(); var encoder = ChooseEncoder(); await engine.SetReplay(true, profile, encoder, testSource);
                await Task.Delay(8000);
                for (int i = 0; i < 3; i++)
                {
                    engine.SetReplaySeconds(i == 0 ? 30 : 5);
                    await Task.Delay(137 + i * 83);
                    double end = engine.Pipelines[0].LiveEdge;
                    string file = (await engine.SaveReplay())[0];
                    await Evidence(name + "-replay-" + i, file, Math.Max(0, end - (i == 0 ? 30 : 5)), end);
                }
                await engine.StartRecording(profile, encoder, testSource); double start = engine.Pipelines[0].LiveEdge;
                await Task.Delay(5100); double last = engine.Pipelines[0].LiveEdge;
                string manual = (await engine.StopRecording())[0]; await Evidence(name + "-manual", manual, start, last);
                // Preserve the encoded source too: distinguish a source/AAC transient from a new export defect.
                var p = engine.Pipelines[0]; p.Refresh(); string parts = Path.Combine(data, name + "-segments"); Directory.CreateDirectory(parts);
                foreach (var segment in p.Segments) File.Copy(segment.Path, Path.Combine(parts, Path.GetFileName(segment.Path)));
                File.Copy(Path.Combine(p.Folder, "segments.csv"), Path.Combine(parts, "segments.csv"));
                await engine.SetReplay(false, profile, encoder, testSource);
                await Check(name + ": all capture sources stopped", !engine.Busy);
            }
            finally { motion.Stop(); target.Close(); }

            async Task Evidence(string label, string file, double from, double to)
            {
                var raw = await engine.Pipelines[0].Audio!.SnapshotAsync(Path.Combine(data, label + ".f32"));
                artifacts.Add(new { name = label, file, amplitude, paced, from, to, raw });
                await Check(label + ": export verified and raw control retained", raw != null && File.Exists(file));
            }
        }
        async Task Sync(string name, int pulses, int quietMilliseconds, bool pause)
        {
            var sources = new Dictionary<AudioSourceKind, SyntheticAudioSource>();
            AudioPipe.SourceFactory = (kind, _) => { var s = new SyntheticAudioSource { Kind = kind }; lock (sources) sources[kind] = s; return s; };
            var (target, motion) = TestSourceWindow("FRAME audio sync " + name, 480, 300);
            var canvas = (Canvas)target.Content; canvas.Background = Brushes.Black; target.Show(); motion.Start();
            try
            {
                await Task.Delay(400); testSource = [TestTarget(target, name)]; engine.ResetFault();
                var profile = Profile(true); var encoder = ChooseEncoder(); await engine.StartRecording(profile, encoder, testSource);
                await Task.Delay(1000);
                for (int i = 0; i < pulses; i++)
                {
                    if (pause && i == pulses / 2)
                    {
                        await engine.PauseResume();
                        canvas.Background = Brushes.White; lock (sources) sources[AudioSourceKind.Desktop].Beep(1);
                        await Task.Delay(1500); canvas.Background = Brushes.Black; await Task.Delay(1500);
                        await engine.PauseResume(); await Task.Delay(700);
                    }
                    canvas.Background = Brushes.White;
                    lock (sources) sources[i % 2 == 0 ? AudioSourceKind.Desktop : AudioSourceKind.Microphone].Beep(.3);
                    await Task.Delay(300); canvas.Background = Brushes.Black; await Task.Delay(quietMilliseconds);
                }
                if (!pause) { motion.Stop(); await Task.Delay(4000); }
                double expected = engine.RecordedSeconds;
                string file = (await engine.StopRecording())[0];
                await Inspect(name, file, expected, pulses);
                var info = await ShareExportJob.Probe(exe, file);
                var range = new TimeRange(.73, info.Duration - .81);
                var request = new ExportRequest(file, info, 0, 0, 0, Framing.Original, .5, .5, ExportQuality.High, 0, false, Path.Combine(data, "trims"), range);
                string trimmed = await ShareExportJob.Run(exe, request, encoder, name + " trimmed", new Progress<(ExportStage, double)>(), CancellationToken.None, lowImpact: true);
                await Inspect(name + "-trimmed", trimmed, range.End - range.Start, pulses);
                await Check(name + ": capture fully stopped", !engine.Busy);
            }
            finally { motion.Stop(); target.Close(); }
        }
        async Task Inspect(string name, string file, double expected, int events)
        {
            var sync = await AudioQualityMeasurements.Sync(exe, file);
            artifacts.Add(new { name, file, sync });
            await Check(name + ": all flash/beep events retained in sync, paused event excluded",
                sync.Flashes.Length == events && sync.Offsets.Length == events && sync.Offsets.All(o => Math.Abs(o) <= .05)
                && Math.Abs(MediaContinuity.Trend(sync.Offsets)) < .03, sync);
            await Check(name + ": matching audio/video duration", Math.Abs(sync.Video - expected) < .15 && Math.Abs(sync.Audio - sync.Video) < .1, new { expected, sync.Video, sync.Audio });
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", file, "-f", "null", "-"], 120);
            await Check(name + ": full decode", decode.Code == 0, decode.Text);
        }
    }
}

// A second diagnostic control deliberately has no wall-clock pacing: every requested sample is a continuous sine.
internal sealed class ExactToneSource(double amplitude) : IAudioSource
{
    long sample;
    public Exception? Failure => null;
    public Task Start() => Task.CompletedTask;
    public int Limit(int maxSamples) => 0;
    public void Mix(float[] output)
    {
        for (int i = 0; i < output.Length; i += 2) { float v = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * sample++ / 48000)); output[i] += v; output[i + 1] += v; }
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class AudioQualityMeasurements
{
    internal sealed record SyncResult(double Video, double Audio, double[] Flashes, double[] Tones, double[] Offsets);
    internal static async Task<SyncResult> Sync(string exe, string file)
    {
        var probe = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "json", file]);
        using var doc = JsonDocument.Parse(probe.Text);
        double Duration(string type) => doc.RootElement.GetProperty("streams").EnumerateArray().Where(s => s.GetProperty("codec_type").GetString() == type)
            .Select(s => double.Parse(s.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture)).FirstOrDefault();
        var luma = await Backend.Run(exe, ["-hide_banner", "-i", file, "-an", "-vf", "signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-f", "null", "-"], 180);
        var flashes = new List<double>(); double at = 0; bool white = false;
        foreach (string line in luma.Text.Split('\n'))
        {
            var time = Regex.Match(line, @"pts_time:([0-9.]+)"); if (time.Success) at = double.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = Regex.Match(line, @"YAVG=([0-9.]+)");
            if (y.Success) { bool now = double.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture) > 150; if (now && !white) flashes.Add(at); white = now; }
        }
        var sil = await Backend.Run(exe, ["-hide_banner", "-i", file, "-vn", "-af", "silencedetect=n=-30dB:d=0.1", "-f", "null", "-"], 180);
        var tones = Regex.Matches(sil.Text, @"silence_end: ([0-9.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        var offsets = flashes.Select(f => tones.Where(t => Math.Abs(t - f) < .5).Select(t => t - f).DefaultIfEmpty(double.NaN).First()).Where(double.IsFinite).ToArray();
        return new(Duration("video"), Duration("audio"), flashes.ToArray(), tones, offsets);
    }
}
