using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Frame.App;

// --resource-test <json> (isolated --data, 0.5.5): what FRAME costs (1) visible with Shadow Recording off, (2) visible with
// Shadow Recording on (stone pulse), (3) minimized to the tray with Shadow Recording on, (4) visible again with it off.
// FRAME is placed on the primary display behind the other windows (not activated: it never covers what the user is doing;
// WPF renders it all the same). Per phase: FRAME's own CPU, the
// FFmpeg engine's CPU, decorative updates (CaptureAtmosphere.Updates) and whether the decorative clock runs. GPU use is
// sampled outside the process (performance counters, see work\measure-resources.ps1) against the phase times written here.
// Capture source: a small test window FRAME controls (no user content, no audio device).
public partial class MainWindow
{
    async Task ResourceTest(string report)
    {
        var phases = new List<object>(); double seconds = Array.IndexOf(args, "--phase-seconds") is int ps and >= 0 ? double.Parse(args[ps + 1], System.Globalization.CultureInfo.InvariantCulture) : 20;
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(20, 30, 50)) }; var bar = new Rectangle { Width = 40, Height = 120, Fill = Brushes.Orange }; canvas.Children.Add(bar);
        var area = SystemParameters.WorkArea;
        var target = new Window { Title = "FRAME resource test", Width = 360, Height = 220, Left = area.Right - 380, Top = area.Bottom - 240, WindowStyle = WindowStyle.ToolWindow, ShowActivated = false, ShowInTaskbar = false, Content = canvas, ResizeMode = ResizeMode.NoResize };
        int tick = 0; var motion = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) }; motion.Tick += (_, _) => { tick++; Canvas.SetLeft(bar, tick * 7 % 320); };
        async Task Phase(string name)
        {
            await Task.Delay(2000); // settle
            using var self = Process.GetCurrentProcess(); var engines = engine.ProcessIds.Select(id => { try { return Process.GetProcessById(id); } catch (ArgumentException) { return null; } }).OfType<Process>().ToArray();
            double cpu0 = self.TotalProcessorTime.TotalSeconds, eng0 = engines.Sum(p => p.TotalProcessorTime.TotalSeconds); int updates0 = atmosphere.Updates; var from = DateTimeOffset.Now; var watch = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            self.Refresh(); foreach (var p in engines) p.Refresh();
            double wall = watch.Elapsed.TotalSeconds, cores = Environment.ProcessorCount;
            phases.Add(new
            {
                name, from, to = DateTimeOffset.Now, seconds = Math.Round(wall, 2), visible = IsVisible, state = WindowState.ToString(), shadow = engine.Replay,
                frameCpuPercentOfPc = Math.Round((self.TotalProcessorTime.TotalSeconds - cpu0) / wall / cores * 100, 3), frameCpuPercentOfOneCore = Math.Round((self.TotalProcessorTime.TotalSeconds - cpu0) / wall * 100, 2),
                engineCpuPercentOfPc = Math.Round((engines.Sum(p => { try { return p.TotalProcessorTime.TotalSeconds; } catch (InvalidOperationException) { return 0; } }) - eng0) / wall / cores * 100, 3),
                decorativeUpdates = atmosphere.Updates - updates0, decorativeUpdatesPerSecond = Math.Round((atmosphere.Updates - updates0) / wall, 1), decorativeClockRunning = atmosphere.Running,
                workingSetMb = Math.Round(self.WorkingSet64 / 1048576d), pid = Environment.ProcessId
            });
            foreach (var p in engines) p.Dispose();
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { phases }, new JsonSerializerOptions { WriteIndented = true }));
        }
        try
        {
            if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The capture folder must be inside the test data folder.");
            target.Show(); await Task.Delay(500); var hwnd = new WindowInteropHelper(target).Handle; GetWindowRect(hwnd, out var wr);
            testSource = [new CaptureTarget("resource-test", "Resource test window", new Box(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top), 0, hwnd)];
            settings.ReplaySeconds = 300; settings.Fps = 60; settings.BitrateMbps = 20; settings.OutputHeight = 1080; settings.DesktopAudio = settings.Microphone = false; settings.ShowSaveNotifications = false;
            // On the primary display, centred, not activated (the user's foreground window keeps focus).
            void Behind() { Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2; Native.SetWindowPos(new WindowInteropHelper(this).Handle, 1 /* HWND_BOTTOM */, 0, 0, 0, 0, Native.SwpNoSize | 0x0002 /* NOMOVE */ | Native.SwpNoActivate); }
            ShowInTaskbar = true; Behind(); ShowPage(HomePage, "Capture", ""); motion.Start();
            await Phase("visible · Shadow Recording off");
            await Act(ToggleShadow); if (!engine.Replay) throw new InvalidOperationException("Shadow Recording did not start: " + Message.Text);
            await Phase("visible · Shadow Recording on (pulse)");
            WindowState = WindowState.Minimized; // minimize = to the tray (hidden), as for the user
            await Phase("tray (hidden) · Shadow Recording on");
            ShowActivated = false; Show(); WindowState = WindowState.Normal; Behind(); await RequestShadowToggle();
            await Phase("visible again · Shadow Recording off");
        }
        catch (Exception e) { phases.Add(new { name = "failure", error = e.ToString() }); await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { phases }, new JsonSerializerOptions { WriteIndented = true })); }
        finally { motion.Stop(); if (target.IsVisible) target.Close(); }
    }
}
