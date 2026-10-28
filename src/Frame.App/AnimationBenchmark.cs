using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace Frame.App;

// --anim-benchmark <json> (isolated --data, no capture): decorative-animation cost per phase: visible with Shadow inactive,
// visible with the active presentation (lit stone + pulse, no capture started), hidden (tray), minimized, and the first
// seconds after restoring. Per phase: process CPU, simulation steps and raw CompositionTarget.Rendering callbacks, per-layer
// update counters (sand, dune light, stone), managed allocation and gen-0 collections, the largest clock step (a resume
// must not catch up: <= 0.1 s), and Windows' animation preference (motion is off when "Show animations" is off). GPU use
// is sampled from outside with Windows GPU-engine counters using the phase times (r1-agentB/bench/anim-benchmark-b.ps1).
public partial class MainWindow
{
    async Task AnimationBenchmark(string report)
    {
        WindowStartupLocation = WindowStartupLocation.Manual; Left = 80; Top = 60; Width = 1280; Height = 800; ShowInTaskbar = true; Show();
        await Task.Delay(3000);
        using var self = Process.GetCurrentProcess();
        async Task<object> Phase(string name, int seconds)
        {
            self.Refresh(); double cpu = self.TotalProcessorTime.TotalSeconds; var start = DateTime.UtcNow; var w = Stopwatch.StartNew();
            int updates = atmosphere.Updates, frames = atmosphere.Frames, sand = atmosphere.SandUpdates, dunes = atmosphere.DuneUpdates, stone = atmosphere.StoneUpdates, gc0 = GC.CollectionCount(0);
            long bytes = GC.GetTotalAllocatedBytes(); atmosphere.ResetLargestStep();
            await Task.Delay(seconds * 1000); self.Refresh(); double elapsed = w.Elapsed.TotalSeconds;
            return new
            {
                name, startUtc = start.ToString("O"), endUtc = DateTime.UtcNow.ToString("O"), seconds = Math.Round(elapsed, 2),
                cpuPercentOfMachine = Math.Round((self.TotalProcessorTime.TotalSeconds - cpu) / elapsed / Environment.ProcessorCount * 100, 3),
                cpuPercentOfOneCore = Math.Round((self.TotalProcessorTime.TotalSeconds - cpu) / elapsed * 100, 2),
                simulationSteps = atmosphere.Updates - updates, stepsPerSecond = Math.Round((atmosphere.Updates - updates) / elapsed, 1), renderCallbacks = atmosphere.Frames - frames,
                sandUpdates = atmosphere.SandUpdates - sand, duneLightUpdates = atmosphere.DuneUpdates - dunes, stoneUpdates = atmosphere.StoneUpdates - stone,
                allocatedKBPerSecond = Math.Round((GC.GetTotalAllocatedBytes() - bytes) / 1024d / elapsed, 1), gen0Collections = GC.CollectionCount(0) - gc0,
                clockRunning = atmosphere.Running, visible = IsVisible, windowState = WindowState.ToString(), largestStepSeconds = Math.Round(atmosphere.LargestStep, 4)
            };
        }
        var inactive = await Phase("visible, Shadow inactive", 20);
        UpdateRecordingPresentation(true, RecordingState.Idle); ShadowTitle.Text = "Stop shadow recording"; await Task.Delay(1500);
        var active = await Phase("visible, active presentation (pulse; no capture)", 20);
        Hide(); await Task.Delay(1500);
        var hidden = await Phase("hidden (tray)", 20);
        Show(); await Task.Delay(1000); WindowState = WindowState.Minimized; await Task.Delay(1500);
        var minimized = await Phase("minimized" + (IsVisible ? "" : " (moved to tray)"), 10);
        if (!IsVisible) Show(); WindowState = WindowState.Normal; await Task.Delay(200);
        var restored = await Phase("restored (first 5 s)", 5);
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { pid = Environment.ProcessId, windowsAnimationsOff = atmosphere.ReducedMotion, terrainAnalysisMs = Math.Round(DesertTerrain.Milliseconds, 1), phases = new[] { inactive, active, hidden, minimized, restored } }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
