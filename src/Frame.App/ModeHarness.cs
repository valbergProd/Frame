using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace Frame.App;

// --mode-switch-test <report.json> (0.6.0; isolated --data only, real capture of FRAME's own synthetic test window, no audio
// devices, the window stays off-screen and is never activated). Checks that visual modes are presentation only:
//   - Shadow Recording keeps running while the title-bar button goes through all five modes (twice, with the title-bar links): same FFmpeg processes, buffer keeps growing, capture settings / replay length / hotkeys unchanged, and a
//     save afterwards still holds footage from before the first switch (nothing was reset);
//   - every layout shows the same state (CaptureSnapshot) and manual recording works from a new layout (start, stop, file);
//   - decorative work per mode: Dark / No Bullshit and Light never run the clock; every mode stops it completely while FRAME
//     is minimized to the tray and resumes without catching up.
public partial class MainWindow
{
    async Task<bool> RunModeHarness()
    {
        if (!args.Contains("--data") || Array.IndexOf(args, "--mode-switch-test") is not (int i and >= 0)) return false;
        try { await ModeSwitchTest(args[i + 1]); }
        catch (Exception error) { await File.WriteAllTextAsync(args[i + 1] + ".error.txt", error.ToString()); }
        await Quit(); return true;
    }
    async Task ModeSwitchTest(string report)
    {
        var results = new List<object>(); var clock = Stopwatch.StartNew();
        void Check(string name, bool ok, object? detail = null) => results.Add(new { name, passed = ok, at = Math.Round(clock.Elapsed.TotalSeconds, 1), detail });
        Left = -20000; ShowInTaskbar = false;
        string exe = Backend.Resolve(settings.FfmpegPath), probe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        var (target, motion) = TestSourceWindow("FRAME mode switch test", 1280, 1160);
        try
        {
            bool isolated = Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase) && !settings.DesktopAudio && !settings.Microphone;
            Check("Isolated test settings (capture folder inside the data folder, no audio devices) and engine ready", isolated && ready, new { settings.OutputDirectory, ready });
            if (!isolated || !ready) return;
            target.Show(); motion.Start(); await Task.Delay(600);
            testSource = [TestTarget(target, "mode-test")]; DrawMonitors();
            ApplyMode(VisualMode.Original, persist: true);
            await Act(ToggleShadow);
            var pids = engine.ProcessIds.Order().ToArray(); var before = CloneSettings(); var started = Stopwatch.StartNew();
            Check("Shadow Recording runs (Original GUI)", engine.Replay && pids.Length > 0 && CaptureSnapshot().ShadowActive, new { pids });
            await Task.Delay(3000);

            // Cycle twice through all modes with the real title-bar button, then the Original GUI shortcut from Emo mode.
            var seen = new List<object>(); bool stable = true, views = true;
            for (int k = 0; k < 10; k++)
            {
                double buffered = engine.BufferedSeconds;
                ModeLinkButtons[VisualModes.All[(k + 1) % VisualModes.All.Length]].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); // next mode's title-bar link
                UpdateLayout(); await Task.Delay(700);
                var v = CaptureSnapshot();
                bool same = engine.Replay && engine.ProcessIds.Order().SequenceEqual(pids) && engine.BufferedSeconds >= buffered && engine.State == RecordingState.Idle;
                stable &= same;
                views &= v.ShadowActive && v.ShadowTitle == "Stop shadow recording" && v.ReplaySeconds == before.ReplaySeconds && CaptureHost.IsVisible && activeLayout != null;
                seen.Add(new { mode = VisualModes.Name(mode), same, buffered = Math.Round(engine.BufferedSeconds, 1), frames = engine.Pipelines.Sum(p => p.Frames), layoutShown = CaptureHost.IsVisible });
            }
            ApplyMode(VisualMode.Emo, persist: true); ModeLinkButtons[VisualMode.Original].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Task.Delay(500);
            stable &= engine.Replay && engine.ProcessIds.Order().SequenceEqual(pids) && mode == VisualMode.Original;
            var file = SettingsStore.Load(configPath);
            Check("10 mode switches with the title-bar links + Original GUI from Emo mode while recording: Shadow Recording never stopped or restarted (same FFmpeg processes, buffer kept growing)", stable, seen);
            Check("Every mode's Capture layout showed the live state (active, Stop label, replay length)", views);
            Check("Mode switches saved only the mode: capture settings, replay length and hotkeys unchanged", !CaptureSettings.Differ(before, file) && file.ReplaySeconds == before.ReplaySeconds && JsonSerializer.Serialize(file.Hotkeys) == JsonSerializer.Serialize(before.Hotkeys) && file.VisualMode == "original", new { file.VisualMode });
            double sinceStart = started.Elapsed.TotalSeconds;
            var files = await engine.SaveReplay(engine.RequestEdges());
            var r = await Backend.Run(probe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", files[0]], 20);
            double duration = double.TryParse(r.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            Check("A save after the switches holds the footage from before the first switch (no reset)", duration >= sinceStart - 3 && duration <= sinceStart + 3, new { duration = Math.Round(duration, 1), sinceStart = Math.Round(sinceStart, 1) });

            // Manual recording from a new layout (the same commands its controls use), while Shadow Recording runs.
            ApplyMode(VisualMode.Dark, persist: false); ICaptureCommands commands = this;
            await commands.ToggleManual(); await Task.Delay(3500);
            bool manualShown = CaptureSnapshot() is { ManualActive: true, ManualTitle: "Stop manual recording" } mv && mv.ManualElapsed.Length > 0;
            var existing = Directory.EnumerateFiles(settings.OutputDirectory, "*.*", SearchOption.AllDirectories).ToHashSet();
            await commands.ToggleManual(); await Task.Delay(500);
            var made = Directory.EnumerateFiles(settings.OutputDirectory, "*.*", SearchOption.AllDirectories).Where(f => !existing.Contains(f) && f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)).ToArray();
            double manualDuration = 0;
            if (made.Length > 0) { var m = await Backend.Run(probe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", made[0]], 20); double.TryParse(m.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out manualDuration); }
            Check("Manual recording from the Dark / No Bullshit layout: shown while recording, finalized into a playable file; Shadow Recording continues", manualShown && made.Length == 1 && manualDuration > 2 && engine.Replay && engine.State == RecordingState.Idle, new { made = made.Select(Path.GetFileName), manualDuration });

            // Decorative work per mode: visible, then minimized to the tray (clock must stop), then shown again (no catch-up).
            var modes = new List<object>(); bool quietPlain = true, stops = true, resumes = true;
            foreach (var m in VisualModes.All)
            {
                ApplyMode(m, persist: false); await Task.Delay(800);
                bool running = atmosphere.Running; int updates = atmosphere.Updates, clients = atmosphere.ClientUpdates;
                if (!VisualModes.Decorative(m)) quietPlain &= !running && atmosphere.Updates == updates;
                WindowState = WindowState.Minimized; await Task.Delay(300);
                int u0 = atmosphere.Updates, c0 = atmosphere.ClientUpdates; await Task.Delay(1200);
                bool stopped = !atmosphere.Running && atmosphere.Updates == u0 && atmosphere.ClientUpdates == c0 && !IsVisible;
                stops &= stopped;
                ShowActivated = false; Show(); WindowState = WindowState.Normal; atmosphere.ResetLargestStep(); await Task.Delay(900);
                bool resumed = atmosphere.Running == running && atmosphere.LargestStep <= .1;
                resumes &= resumed;
                modes.Add(new { mode = VisualModes.Name(m), visibleRunning = running, stopped, resumed, largestStep = Math.Round(atmosphere.LargestStep, 3) });
            }
            Check("Dark / No Bullshit and Light run no decorative clock while visible", quietPlain, modes);
            Check("Every mode: minimized to the tray stops all decorative updates (clock unsubscribed, no client updates)", stops, modes);
            Check("Every mode: shown again resumes as before without catching up (largest step ≤ 0.1 s)", resumes, modes);
            Check("Shadow Recording still the same session after all mode and visibility changes", engine.Replay && engine.ProcessIds.Order().SequenceEqual(pids), new { pids = engine.ProcessIds.ToArray() });
            await Act(ToggleShadow);
            Check("Shadow Recording stops cleanly; no FFmpeg left", !engine.Replay && ChildProcesses("ffmpeg.exe").Length == 0);
        }
        finally
        {
            motion.Stop(); target.Close(); testSource = [];
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
