using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Frame.App;

// --live-length-test <json> (isolated --data): the replay length changes while Shadow Recording runs. Records a small
// test window (no audio) and checks that the slider stays usable, that saves use the new length at once, that a longer
// length fills up as recording continues, and that turning the length down and up again keeps recorded history.
public partial class MainWindow
{
    async Task LiveLengthTest(string report)
    {
        var results = new List<object>(); string probe = Path.Combine(Path.GetDirectoryName(Backend.Resolve(settings.FfmpegPath))!, "ffprobe.exe");
        void Check(string name, bool ok, object? detail = null) => results.Add(new { name, passed = ok, detail });
        async Task<double> Save() { await RequestSaveReplay(); var f = Directory.EnumerateFiles(settings.OutputDirectory, "*.mp4", SearchOption.AllDirectories).Where(x => !x.EndsWith(".partial.mp4")).OrderByDescending(File.GetLastWriteTimeUtc).First(); var r = await Backend.Run(probe, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", f]); return Math.Round(double.Parse(r.Text.Trim(), CultureInfo.InvariantCulture), 2); }
        var target = new Window { Title = "FRAME length test source", Width = 320, Height = 200, Left = SystemParameters.WorkArea.Right - 340, Top = SystemParameters.WorkArea.Bottom - 220, ShowActivated = false, ShowInTaskbar = false, Content = new System.Windows.Controls.Border { Background = Brushes.DarkSlateGray } };
        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; int n = 0;
        try
        {
            if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase)) settings.OutputDirectory = Path.Combine(data, "Clips");
            Directory.CreateDirectory(settings.OutputDirectory);
            target.Show(); await Task.Delay(300); GetWindowRect(new WindowInteropHelper(target).Handle, out var r);
            testSource = [new CaptureTarget("length-test", "Length test window", new Box(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), 0, new WindowInteropHelper(target).Handle)];
            settings.ReplaySeconds = 20; settings.Fps = 30; settings.BitrateMbps = 6; settings.DesktopAudio = settings.Microphone = false; SyncDuration();
            tick.Tick += (_, _) => ((System.Windows.Controls.Border)target.Content).Background = (n++ % 2 == 0) ? Brushes.DarkSlateGray : Brushes.SlateBlue; tick.Start();
            await Act(ToggleShadow); await Task.Delay(30_000);
            Check("Slider usable while Shadow Recording runs", engine.Replay && HistorySlider.IsEnabled, new { HistorySlider.IsEnabled, engine.Replay });
            double first = await Save();
            Check("Save with 20 s history", Math.Abs(first - 20) < .6, first);
            CommitHistory(60);
            double grownNow = await Save();
            Check("Changed to 1 min during recording: saved at once, all retained history (20 s + 2 s margin) included", settings.ReplaySeconds == 60 && engine.Replay && grownNow is > 20.5 and < 26, grownNow);
            await Task.Delay(25_000);
            double grown = await Save();
            Check("The longer history fills up as recording continues", grown > grownNow + 20, new { before = grownNow, after = grown });
            CommitHistory(20); await Task.Delay(4_000); // buffer maintenance runs every second
            CommitHistory(60); await Task.Delay(1_000);
            double kept = await Save();
            Check("Turning the length down and up again keeps recorded history (no footage deleted)", kept >= grown + 4, new { before = grown, after = kept });
            Check("No fault, Shadow Recording kept running throughout", engine.Replay && fault == null && !SaveOverlay.History.Any(h => h.Contains("stopped")), SaveOverlay.History.ToArray());
            await Act(ToggleShadow);
        }
        catch (Exception e) { Check("Exception", false, e.ToString()); }
        finally { tick.Stop(); target.Close(); await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = results.Count(x => (bool)x.GetType().GetProperty("passed")!.GetValue(x)!), total = results.Count, results }, new JsonSerializerOptions { WriteIndented = true })); }
    }
}
