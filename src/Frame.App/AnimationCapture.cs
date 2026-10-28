using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Frame.App;

// --render <png> --render-animation <folder> (isolated --data; no capture): renders the real window frame by frame while
// stepping CaptureAtmosphere's clock deterministically (the same Step the screen uses), for video review of the atmosphere.
// Options: --anim-seconds <s> (10), --anim-fps <n> (30), --anim-warmup <s> (4, simulated before the first frame so grains
// and gusts are established), --anim-hide-sand / --anim-hide-dunelight (review one layer), --anim-active (Shadow presentation active from the start), --anim-activate <s> and
// --anim-deactivate <s> (presentation switches at that clip time), --anim-scale <f> (1). Writes f00000.png… and
// animation.json (frame count, timings, stone angle/glow per frame). Encode with ffmpeg -framerate <fps> -i f%05d.png.
public partial class MainWindow
{
    async Task RenderAnimation(string folder)
    {
        Directory.CreateDirectory(folder);
        double Option(string name, double fallback) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
        double seconds = Option("--anim-seconds", 10), fps = Option("--anim-fps", 30), warmup = Option("--anim-warmup", 4), scale = Option("--anim-scale", 1);
        double activate = Option("--anim-activate", -1), deactivate = Option("--anim-deactivate", -1);
        void Present(bool on) { UpdateRecordingPresentation(on, RecordingState.Idle); ShadowTitle.Text = on ? "Stop shadow recording" : "Start shadow recording"; PushCaptureView(); } // 0.6.0: the active mode's layout too
        atmosphere.BeginManualClock();
        if (args.Contains("--anim-hide-sand")) SandDrift.Opacity = 0;   // review one layer at a time
        if (args.Contains("--anim-hide-dunelight")) DuneFlow.Opacity = 0;
        Present(args.Contains("--anim-active"));
        double step = 1 / fps;
        for (double t = 0; t < warmup; t += step) atmosphere.ManualStep(step);
        var clock = System.Diagnostics.Stopwatch.StartNew(); var stepping = new System.Diagnostics.Stopwatch(); var stone = new List<object>();
        int frames = (int)Math.Round(seconds * fps);
        for (int f = 0; f < frames; f++)
        {
            double t = f * step;
            if (activate >= 0 && t >= activate && t - step < activate) Present(true);
            if (deactivate >= 0 && t >= deactivate && t - step < deactivate) Present(false);
            stepping.Start(); atmosphere.ManualStep(step); UpdateLayout(); stepping.Stop(); // UI-thread cost of one step incl. layout/OnRender
            SaveRender(Path.Combine(folder, $"f{f:00000}.png"), scale);
            stone.Add(new { t = Math.Round(t, 3), angle = Math.Round(atmosphere.Stone?.Angle ?? 0, 2), level = Math.Round(atmosphere.Stone?.Level ?? 0, 3), glow = Math.Round(atmosphere.Stone?.Glow ?? 0, 3) });
            if (f % 15 == 0) await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "animation.json"), JsonSerializer.Serialize(new { frames, fps, seconds, warmup, renderSeconds = clock.Elapsed.TotalSeconds, stepMsAverage = stepping.Elapsed.TotalMilliseconds / Math.Max(1, frames), terrainMs = DesertTerrain.Milliseconds, updates = atmosphere.Updates, stone }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
