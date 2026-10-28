using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Frame.App;

// --library-test <json> (isolated --data, 0.5.5): Library previews are made on demand only. Synthetic clips in the test's own
// capture folder (plus a broken clip, an empty and a corrupt cached preview); Shadow Recording runs on a test window during
// the whole check (newer clips had no preview before because nothing was generated while recording). Checks: no preview work
// while Library is closed (also after a refresh), previews made when Library opens, at most two at a time, rows in view first
// after a scroll, broken cache entries replaced, a broken clip shows "No preview", work stops when Library is left or FRAME is
// hidden and resumes when shown, a clip saved while Library is open gets its preview.
public partial class MainWindow
{
    internal readonly List<string> previewStartedNames = []; // diagnostics: file names in the order their preview started
    async Task LibraryPreviewTest(string report)
    {
        var results = new List<object>(); int passedCount = 0; string exe = Backend.Resolve(settings.FfmpegPath);
        void Check(string name, bool ok, object? detail = null) { results.Add(new { name, passed = ok, detail }); if (ok) passedCount++; }
        async Task<bool> WaitFor(Func<bool> condition, double seconds) { var w = Stopwatch.StartNew(); while (w.Elapsed.TotalSeconds < seconds) { if (condition()) return true; await Task.Delay(25); } return condition(); }
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(20, 30, 50)) }; var bar = new Rectangle { Width = 40, Height = 120, Fill = Brushes.Orange }; canvas.Children.Add(bar);
        var area = SystemParameters.WorkArea;
        var target = new Window { Title = "FRAME library test", Width = 360, Height = 220, Left = area.Right - 380, Top = area.Bottom - 240, WindowStyle = WindowStyle.ToolWindow, ShowActivated = false, ShowInTaskbar = false, Content = canvas, ResizeMode = ResizeMode.NoResize };
        int tick = 0; var motion = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) }; motion.Tick += (_, _) => { tick++; Canvas.SetLeft(bar, tick * 7 % 320); };
        int maxWorkers = 0; var sampler = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) }; sampler.Tick += (_, _) => maxWorkers = Math.Max(maxWorkers, previewWorkers);
        var cache = Path.Combine(data, "Thumbnails");
        IEnumerable<(string Name, Image Image, TextBlock Placeholder, Border Card)> Rows() => LibraryList.Children.OfType<Border>().Select(card => { var row = (Grid)card.Child; var face = (Grid)((Border)row.Children[0]).Child; var info = (StackPanel)row.Children[1]; return (((TextBlock)info.Children[0]).Text, (Image)face.Children[1], (TextBlock)face.Children[0], card); });
        try
        {
            if (!Path.GetFullPath(settings.OutputDirectory).StartsWith(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The capture folder must be inside the test data folder.");
            Directory.CreateDirectory(settings.OutputDirectory); Directory.CreateDirectory(cache);
            // 30 short clips (distinct dates, newest last), one broken clip.
            var start = DateTime.Now.AddHours(-2);
            for (int i = 0; i < 30; i++)
            {
                string clip = Path.Combine(settings.OutputDirectory, $"FRAME test clip {i:00}.mp4");
                var r = await Backend.Run(exe, ["-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size=320x180:rate=30:duration=1", "-vf", $"drawtext=text='{i:00}':fontsize=60:x=10:y=10", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", clip], 60);
                if (r.Code != 0) { r = await Backend.Run(exe, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=1", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", clip], 60); }
                File.SetLastWriteTime(clip, start.AddMinutes(i));
            }
            string broken = Path.Combine(settings.OutputDirectory, "FRAME broken clip.mp4"); File.WriteAllBytes(broken, new byte[4096]); File.SetLastWriteTime(broken, start.AddMinutes(-5));
            CaptureFile Info(string path) { var f = new FileInfo(path); return new CaptureFile(f.FullName, f.Name, f.Length, f.LastWriteTime, false); }
            // Broken cache entries for clips 29 (empty file) and 28 (not a PNG): must be replaced by real previews.
            string empty = Path.Combine(cache, PreviewKey(Info(Path.Combine(settings.OutputDirectory, "FRAME test clip 29.mp4"))) + ".png"), corrupt = Path.Combine(cache, PreviewKey(Info(Path.Combine(settings.OutputDirectory, "FRAME test clip 28.mp4"))) + ".png");
            File.WriteAllBytes(empty, []); File.WriteAllText(corrupt, "not a png");
            Check("Test clips created", Directory.EnumerateFiles(settings.OutputDirectory, "FRAME test clip *.mp4").Count() == 30);

            // Shadow Recording on a test window for the whole test.
            target.Show(); await Task.Delay(500); var hwnd = new WindowInteropHelper(target).Handle; GetWindowRect(hwnd, out var wr);
            testSource = [new CaptureTarget("library-test", "Library test window", new Box(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top), 0, hwnd)];
            settings.ReplaySeconds = 20; settings.Fps = 30; settings.BitrateMbps = 6; settings.OutputHeight = 0; settings.DesktopAudio = settings.Microphone = false; settings.ShowSaveNotifications = false;
            await Act(ToggleShadow); motion.Start();
            Check("Shadow Recording runs during the test", engine.Replay && engine.Busy);

            // A. Library closed: a refresh (as after every save) makes no preview work.
            ShowPage(HomePage, "Capture", ""); previewStartedNames.Clear(); int startedBefore = PreviewsStarted; int filesBefore = Directory.EnumerateFiles(cache).Count();
            await RefreshLibrary(); await Task.Delay(2500);
            Check("Library closed: no preview work, nothing written to the cache", PreviewsStarted == startedBefore && PreviewsPending == 0 && Directory.EnumerateFiles(cache).Count() == filesBefore, new { started = PreviewsStarted - startedBefore, filesBefore, filesAfter = Directory.EnumerateFiles(cache).Count() });

            // B. Open Library (the user's path), scroll to the bottom at once: rows in view next, at most two at a time.
            sampler.Start(); previewStartedNames.Clear();
            LibraryClick(this, new RoutedEventArgs()); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            int beforeScroll = previewStartedNames.Count; PageScroll.ScrollToEnd(); UpdateLayout(); await Task.Delay(50);
            var visibleAfterScroll = Rows().Where(r => InView(r.Card)).Select(r => r.Name).ToHashSet();
            bool done = await WaitFor(() => PreviewsPending == 0, 120); sampler.Stop();
            var afterScroll = previewStartedNames.Skip(beforeScroll).Take(Math.Min(4, visibleAfterScroll.Count)).Select(n => Path.GetFileNameWithoutExtension(n)).ToArray();
            Check("Opening Library makes previews (also while recording); at most two at a time", done && Rows().Where(r => r.Name != "FRAME broken clip").All(r => r.Image.Source != null && r.Placeholder.Text == "") && maxWorkers is >= 1 and <= 2, new { done, maxWorkers, started = previewStartedNames.Count, missing = Rows().Where(r => r.Image.Source == null).Select(r => r.Name).ToArray() });
            Check("Rows in view after a scroll are made first", afterScroll.Length > 0 && afterScroll.All(visibleAfterScroll.Contains), new { afterScroll, visible = visibleAfterScroll.ToArray(), order = previewStartedNames.Take(10).ToArray() });
            Check("Empty and corrupt cached previews are replaced with valid ones", new FileInfo(empty).Length > 100 && File.ReadAllBytes(corrupt).Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
            Check("A clip FFmpeg cannot read shows 'No preview' (and the file stays)", Rows().First(r => r.Name == "FRAME broken clip").Placeholder.Text == "No preview" && File.Exists(broken));
            Check("No temporary preview files are left", !Directory.EnumerateFiles(cache, "*.tmp.png").Any());

            // C. Cached previews load again without FFmpeg work (a fresh listing re-reads the cache).
            previewStartedNames.Clear(); await RefreshLibrary(); bool reloaded = await WaitFor(() => PreviewsPending == 0, 60);
            Check("Reopening uses the cache: every preview shown again", reloaded && Rows().Where(r => r.Name != "FRAME broken clip").All(r => r.Image.Source != null));

            // D. Leaving Library stops work; hiding pauses it and showing resumes it. Remove the cache so work is needed.
            foreach (var f in Directory.EnumerateFiles(cache, "*.png")) File.Delete(f);
            await RefreshLibrary(); await Task.Delay(150); ShowPage(HomePage, "Capture", ""); await Task.Delay(1500);
            int afterLeave = PreviewsStarted; await Task.Delay(2500);
            Check("Leaving Library stops preview work (queue cleared, nothing new starts)", PreviewsStarted == afterLeave && PreviewsPending == 0, new { pending = PreviewsPending });
            LibraryClick(this, new RoutedEventArgs()); await Task.Delay(200); Hide(); await Task.Delay(1500); int whileHidden = PreviewsStarted; await Task.Delay(2500);
            bool pausedHidden = PreviewsStarted == whileHidden && PreviewsPending > 0;
            Show(); bool resumed = await WaitFor(() => PreviewsPending == 0, 120);
            Check("Hidden FRAME pauses preview work; showing it resumes and finishes", pausedHidden && resumed && Rows().Where(r => r.Name != "FRAME broken clip").All(r => r.Image.Source != null), new { pausedHidden, resumed });

            // E. A replay saved while Library is open appears with its preview.
            await Task.Delay(3000); await RequestSaveReplay();
            bool newRow = await WaitFor(() => Rows().Any(r => r.Name.Contains("Replay") && r.Image.Source != null), 60);
            Check("A clip saved while Library is open gets its preview", newRow, Rows().Select(r => r.Name).Take(3).ToArray());
            SaveRender(Path.ChangeExtension(report, ".png"));
            await RequestShadowToggle();
        }
        catch (Exception e) { Check("Unexpected failure", false, e.ToString()); }
        finally
        {
            motion.Stop(); sampler.Stop(); if (target.IsVisible) target.Close();
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = passedCount, total = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
