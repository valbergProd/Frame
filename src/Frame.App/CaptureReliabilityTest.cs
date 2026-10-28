using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;

namespace Frame.App;

// --capture-test <json> (with an isolated --data folder): real Windows Graphics Capture through FRAME's own pipeline,
// against a small window this test controls (moving, then completely still). Needs a desktop session that allows
// screen capture. Writes results; no user files or settings are touched.
public partial class MainWindow
{
    async Task CaptureReliabilityTest(string report)
    {
        var results = new List<object>(); string probeExe = Path.Combine(Path.GetDirectoryName(Backend.Resolve(settings.FfmpegPath))!, "ffprobe.exe"); string exe = Backend.Resolve(settings.FfmpegPath);
        void Check(string name, bool ok, object? detail = null) { results.Add(new { name, passed = ok, detail }); if (!ok) throw new InvalidOperationException(name + " " + JsonSerializer.Serialize(detail)); }
        var bar = new Rectangle { Width = 40, Height = 120, Fill = Brushes.Orange };
        var label = new TextBlock { FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(10) };
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(20, 30, 50)) }; canvas.Children.Add(bar); canvas.Children.Add(label);
        var area = SystemParameters.WorkArea;
        var target = new Window { Title = "FRAME capture test", Width = 360, Height = 220, Left = area.Right - 380, Top = area.Bottom - 240, WindowStyle = WindowStyle.ToolWindow, ShowActivated = false, ShowInTaskbar = false, Content = canvas, ResizeMode = ResizeMode.NoResize };
        target.Show();
        int tick = 0; var motion = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        motion.Tick += (_, _) => { tick++; Canvas.SetLeft(bar, tick * 7 % 320); label.Text = tick.ToString(); };
        // Moving content ends on a distinctive green still frame, so a clip can prove it ends on the true final image.
        async Task Move(double seconds) { canvas.Background = new SolidColorBrush(Color.FromRgb(20, 30, 50)); motion.Start(); await Task.Delay(TimeSpan.FromSeconds(seconds)); motion.Stop(); canvas.Background = Brushes.Green; }
        try
        {
            Check("Isolated test settings loaded", Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase), settings.OutputDirectory);
            await Task.Delay(500);
            var hwnd = new WindowInteropHelper(target).Handle; GetWindowRect(hwnd, out var r);
            testSource = [new CaptureTarget("capture-test", "Capture test window", new Box(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), 0, hwnd)];
            settings.ReplaySeconds = 8; settings.Fps = 30; settings.BitrateMbps = 6; settings.OutputHeight = 0; settings.DesktopAudio = settings.Microphone = false; settings.ShowSaveNotifications = true;
            Check("Engine ready", ready, encoders.Select(e => e.Id));

            // A. Start: the confirmation comes only after frames are flowing, once.
            SaveOverlay.History.Clear();
            await Act(ToggleShadow);
            Check("Shadow Recording starts on real capture and confirms once", engine.Replay && SaveOverlay.History.Count(h => h == "Shadow Recording active") == 1 && TrayStateName == "Active", SaveOverlay.History.ToArray());

            // B. 5 s moving, 7 s completely still, then save: the clip must play through the still part to the request.
            await Move(5); await Task.Delay(7000);
            var stillEdge = engine.RequestEdges().Values.First();
            var saved = await TimedSave();
            var still = await Inspect(saved.File);
            Check("Save after 7 s still: full 8 s window ending at the request on the true final image", Math.Abs(still.Duration - 8) < .4 && still.Frozen is { } fz && Math.Abs(fz - (8 - 7)) < .8 && still.EndsGreen && still.Decodes, new { still, stillEdge, saved.Seconds });

            // C. Save during motion (no still tail): ends at the request, nothing frozen at the end.
            await Move(9.5); var moving = await TimedSave(withMotion: true); var live = await Inspect(moving.File);
            Check("Save during motion: 8 s window, no frozen tail", Math.Abs(live.Duration - 8) < .4 && (live.Frozen == null || live.Frozen < 7.2) && live.Decodes, new { live, moving.Seconds });

            // C2 (0.5.4). The user's save path (hotkey/button/tray entry): "Saving replay…" before any export work, then "Clip saved".
            {
                bool notes = settings.ShowSaveNotifications; settings.ShowSaveNotifications = true; SaveOverlay.History.Clear();
                var watch = Stopwatch.StartNew(); var request = RequestSaveReplay();
                double shownMs = SaveOverlay.History.FirstOrDefault() == "Saving replay…" ? watch.Elapsed.TotalMilliseconds : -1;
                await request; double totalSeconds = watch.Elapsed.TotalSeconds; settings.ShowSaveNotifications = notes;
                Check("Real save via the user path: immediate 'Saving replay…', then 'Clip saved' after export", shownMs is >= 0 and < 100 && SaveOverlay.History.SequenceEqual(["Saving replay…", "Clip saved"]), new { shownMs, totalSeconds, History = SaveOverlay.History.ToArray() });
            }

            // D. Several minutes would be the real case; here 25 s still then save: whole 8 s window is the held frame.
            await Task.Delay(25000); var longStill = await Inspect((await TimedSave()).File);
            Check("Save after a long still: whole window is the held final frame and still recording", engine.Replay && Math.Abs(longStill.Duration - 8) < .4 && longStill.Frozen is { } lf && lf < .6 && longStill.EndsGreen && longStill.Decodes, longStill);

            // E. Encoder process dies: detected, classified, restarted once automatically, reported as resumed.
            SaveOverlay.History.Clear(); var pid = engine.ProcessIds.First(); Process.GetProcessById(pid).Kill();
            for (int i = 0; i < 80 && !(fault != null && fault.AutoRetryTried); i++) await Task.Delay(250);
            Check("Killed capture engine: fault classified, auto-restart succeeded, user told it resumed", fault is { AutoRetryTried: true, Recovered: true } && engine.Replay && SaveOverlay.History.Contains("Shadow Recording resumed") && !SaveOverlay.History.Contains("Shadow Recording stopped") && TrayStateName == "Active", new { fault?.Kind, fault?.Explanation, History = SaveOverlay.History.ToArray() });
            await Move(3); var afterRecovery = await Inspect((await TimedSave(withMotion: true)).File);
            Check("Buffer usable after recovery", afterRecovery.Decodes && afterRecovery.Duration > 1, afterRecovery);

            // F. Intentional stop: no warning, tray idle.
            SaveOverlay.History.Clear(); await Act(ToggleShadow);
            Check("Intentional stop shows no warning", !engine.Replay && !SaveOverlay.History.Any(h => h.Contains("stopped")) && TrayStateName == "Idle", SaveOverlay.History.ToArray());

            // G. Source disappears: real failure, retry fails, user warned, panel offers restart, tray shows error.
            await Act(ToggleShadow); await Move(2); SaveOverlay.History.Clear(); fault = null;
            target.Close();
            for (int i = 0; i < 120 && !(fault != null && (fault.AutoRetryTried || !fault.Retryable)); i++) await Task.Delay(250);
            await Task.Delay(500);
            Check("Closed source window: stop detected, explained as source loss, warned once, tray error", fault is { Kind: FaultKind.Source } && !engine.Replay && !fault.Recovered && SaveOverlay.History.Count(h => h == "Shadow Recording stopped") == 1 && TrayStateName == "Error" && FailurePanel.Visibility == Visibility.Visible, new { fault?.Kind, fault?.Explanation, fault?.Details, History = SaveOverlay.History.ToArray(), tray = TrayStateName });
            File.WriteAllText(Path.ChangeExtension(report, ".panel.txt"), string.Join(Environment.NewLine, Descendants<TextBlock>(FailurePanel).Select(t => t.Text)));
            ShowPage(HomePage, "Capture", ""); UpdateLayout(); SaveRender(Path.ChangeExtension(report, ".panel.png"));
        }
        finally
        {
            motion.Stop(); if (target.IsVisible) target.Close();
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task<(string File, double Seconds)> TimedSave(bool withMotion = false)
        {
            if (withMotion) motion.Start();
            var watch = Stopwatch.StartNew(); var edges = engine.RequestEdges(); var files = await engine.SaveReplay(edges); motion.Stop();
            return (files[0], watch.Elapsed.TotalSeconds);
        }
        async Task<CaptureClip> Inspect(string file)
        {
            var probe = await Backend.Run(probeExe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file]);
            double duration = double.Parse(probe.Text.Trim(), CultureInfo.InvariantCulture);
            var freeze = await Backend.Run(exe, ["-hide_banner", "-i", file, "-vf", "freezedetect=n=0.003:d=0.5", "-map", "0:v", "-f", "null", "-"], 120);
            var starts = Regex.Matches(freeze.Text, @"freeze_start: ([0-9.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
            var ends = Regex.Matches(freeze.Text, @"freeze_end: ([0-9.]+)").Count;
            double? frozenFrom = starts.Length > ends ? starts[^1] : null; // an open freeze reaches the end of the clip
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", file, "-f", "null", "-"], 120);
            string pixel = Path.ChangeExtension(file, ".last.rgb");
            await Backend.Run(exe, ["-v", "error", "-y", "-sseof", "-0.2", "-i", file, "-update", "1", "-vf", "scale=1:1:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", pixel], 60);
            var rgb = File.Exists(pixel) ? File.ReadAllBytes(pixel) : []; bool green = rgb.Length >= 3 && rgb[1] > 90 && rgb[1] > rgb[0] * 2 && rgb[1] > rgb[2] * 2;
            return new CaptureClip(Path.GetFileName(file), duration, frozenFrom, decode.Code == 0, green, rgb.Length >= 3 ? $"{rgb[0]},{rgb[1]},{rgb[2]}" : "n/a");
        }
    }
    sealed record CaptureClip(string File, double Duration, double? Frozen, bool Decodes, bool EndsGreen, string LastPixel);
    [StructLayout(LayoutKind.Sequential)] struct WinRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out WinRect rect);
}
