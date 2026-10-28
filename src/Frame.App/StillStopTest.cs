using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Frame.App;

public partial class MainWindow
{
    // Real WGC capture of owned test windows only; synthetic PCM never opens or changes a Windows audio endpoint.
    async Task StillStopTest(string report)
    {
        Left = -20000; ShowInTaskbar = false;
        if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !ready)
            throw new InvalidOperationException("Still-stop test needs an isolated output directory and a ready engine.");
        var results = new List<object>();
        async Task Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail });
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException(name + ": " + JsonSerializer.Serialize(detail));
        }
        string exe = Backend.Resolve(settings.FfmpegPath), probeExe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        try
        {
            await Run("static-from-start", false, false, false, false, false);
            await Run("static-from-start-audio", true, false, false, false, false);
            await Run("motion-then-still-audio", true, false, false, true, false);
            await Run("paused-with-shadow-audio", true, true, true, true, false);
            await Run("combined-still", false, false, false, true, true);
            await Run("separate-still-audio", true, false, false, true, false, separate: true);
            await Run("long-still-audio", true, false, false, true, false, stillMilliseconds: 35000);
            await Run("moving-control", false, false, false, true, false, stayMoving: true);
            await Run("failed-output-retains-recovery", false, false, false, true, false, failOutput: true);
        }
        finally { AudioPipe.SourceFactory = null; }

        async Task Run(string name, bool sound, bool shadow, bool pause, bool moving, bool combined, bool stayMoving = false, bool separate = false, int stillMilliseconds = 1800, bool failOutput = false)
        {
            var sources = new List<SyntheticAudioSource>();
            AudioPipe.SourceFactory = (kind, _) => { var s = new SyntheticAudioSource { Kind = kind }; lock (sources) sources.Add(s); return s; };
            var (target, motion) = TestSourceWindow("FRAME " + name, 480, 300);
            var (other, otherMotion) = TestSourceWindow("FRAME second source", 480, 300);
            if (!moving) ((Canvas)target.Content).Background = Brushes.Green;
            target.Show();
            if (combined || separate) { other.Left = target.Left - other.Width - 12; other.Show(); }
            if (moving) { motion.Start(); if (combined || separate) otherMotion.Start(); }
            await Task.Delay(400);
            var profile = CloneSettings(); profile.DesktopAudio = sound; profile.Microphone = false; profile.Cursor = false;
            profile.Fps = 30; profile.OutputHeight = 0; profile.BitrateMbps = 6; profile.ReplaySeconds = 30; profile.Combined = combined;
            var targets = combined || separate ? new[] { TestTarget(target, name), TestTarget(other, name + "-2") } : new[] { TestTarget(target, name) };
            testSource = targets; engine.ResetFault();
            var encoder = name == "static-from-start" ? new Encoder("libx264", "CPU", false, false, true) : ChooseEncoder();
            if (shadow) await engine.SetReplay(true, profile, encoder, targets);
            await engine.StartRecording(profile, encoder, targets);
            await Task.Delay(1500);
            if (!stayMoving)
            {
                motion.Stop(); otherMotion.Stop();
                ((Canvas)target.Content).Background = Brushes.Green;
                if (combined || separate) ((Canvas)other.Content).Background = Brushes.Green;
            }
            lock (sources) foreach (var source in sources) source.Beep(90);
            await Task.Delay(stillMilliseconds);
            if (pause)
            {
                await engine.PauseResume(); double pausedAt = engine.RecordedSeconds;
                await Task.Delay(1500);
                await Check(name + ": paused timer is stable", Math.Abs(engine.RecordedSeconds - pausedAt) < .01);
                await engine.PauseResume(); await Task.Delay(1200);
            }
            long frames = engine.Pipelines[0].Frames; double before = engine.RecordedSeconds;
            await Task.Delay(1100);
            if (!stayMoving) await Check(name + ": no new frames, recording timer still advances", frames == engine.Pipelines[0].Frames && engine.RecordedSeconds - before > .9, new { frames, now = engine.Pipelines[0].Frames, before, nowSeconds = engine.RecordedSeconds });
            double expected = engine.RecordedSeconds; int[] captureIds = engine.ProcessIds.ToArray(); var watch = Stopwatch.StartNew();
            if (failOutput)
            {
                string recoveryFolder = engine.Pipelines[0].Folder;
                profile.OutputDirectory = Path.Combine(data, "blocked-output"); await File.WriteAllTextAsync(profile.OutputDirectory, "test: a file cannot be an output directory");
                Exception? failure = null;
                try { await engine.StopRecording(); } catch (IOException error) { failure = error; }
                await Check(name + ": export failure is reported, capture stops, original segments and snapshot remain",
                    failure != null && engine.State == RecordingState.Faulted && !engine.Busy && Directory.GetFiles(recoveryFolder, "part-*.mkv").Length > 0
                    && Directory.GetFiles(recoveryFolder, "still.png", SearchOption.AllDirectories).Length > 0, failure?.Message);
                target.Close(); other.Close(); return;
            }
            var files = await engine.StopRecording(); double stopSeconds = watch.Elapsed.TotalSeconds;
            await Check(name + ": stop finishes and saves every selected source", files.Count == (separate ? 2 : 1) && stopSeconds < 25 && engine.State == RecordingState.Idle, new { stopSeconds, files });
            foreach (var file in files)
            {
            var probe = await Backend.Run(probeExe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file]);
            double duration = double.Parse(probe.Text.Trim(), CultureInfo.InvariantCulture);
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", file, "-f", "null", "-"], 60);
            await Check(name + ": entire clip decodes and includes the still time, excluding pauses", decode.Code == 0 && Math.Abs(duration - expected) < .35, new { expected, duration, decode.Text });
            if (!stayMoving)
            {
                string pixel = Path.Combine(data, name + "-last.rgb");
                await Backend.Run(exe, ["-v", "error", "-y", "-sseof", "-0.2", "-i", file, "-update", "1", "-vf", "scale=1:1:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", pixel]);
                var rgb = File.ReadAllBytes(pixel);
                await Check(name + ": final image is the actual green still", rgb.Length >= 3 && rgb[1] > 85 && rgb[1] > rgb[0] * 2 && rgb[1] > rgb[2] * 2, rgb.Take(3).ToArray());
            }
            if (sound)
            {
                string raw = Path.Combine(data, name + "-tail.f32");
                var audioDecode = await Backend.Run(exe, ["-v", "error", "-y", "-ss", Backend.F(duration - 1), "-i", file, "-t", "0.5", "-map", "0:a:0", "-ac", "1", "-f", "f32le", raw]);
                if (audioDecode.Code != 0) throw new IOException(audioDecode.Text);
                var bytes = File.ReadAllBytes(raw); double energy = 0;
                for (int i = 0; i + 4 <= bytes.Length; i += 4) { double sample = BitConverter.ToSingle(bytes, i); energy += sample * sample; }
                double rms = Math.Sqrt(energy / Math.Max(1, bytes.Length / 4));
                await Check(name + ": pending real PCM reaches the still tail (not silence)", bytes.Length > 10000 && rms > .1, new { rms, bytes = bytes.Length });
            }
            }
            if (shadow)
            {
                await Check(name + ": Shadow keeps the same running pipelines after manual Stop", engine.Replay && captureIds.SequenceEqual(engine.ProcessIds));
                await engine.SetReplay(false, profile, encoder, targets);
            }
            await Check(name + ": all capture processes are gone after final Stop", !engine.Busy && captureIds.All(id => { try { using var p = Process.GetProcessById(id); return p.HasExited; } catch (ArgumentException) { return true; } }));
            motion.Stop(); otherMotion.Stop(); target.Close(); other.Close();
        }
    }
}
