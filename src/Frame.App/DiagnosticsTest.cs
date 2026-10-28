using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Frame.App;

// --render <png> --diagnostics-test <report.json> (isolated --data, real FFmpeg): beta diagnostics checks. Controlled
// failures only, in this data folder: log format, a hotkey that another window already holds, an encoder FFmpeg rejects
// (start failure with FFmpeg's message), a replay save into a capture folder that cannot be created, the crash policy, and
// Export diagnostics (contents, redaction, nothing private). A previous --crash-test run on the same folder is picked up
// (unexpected-exit note, crash details in the export). Records a small test window for a few seconds.
public partial class MainWindow
{
    async Task ValidateDiagnostics(string report)
    {
        var results = new List<object>();
        void Check(string name, bool ok, object? detail = null) => results.Add(new { name, passed = ok, detail });
        string[] Logged(string component) => DiagnosticLog.ReadLines().Where(l => l.Contains($"\"Component\":\"{component}\"")).ToArray();
        var target = new Window { Title = "FRAME diagnostics test source", Width = 320, Height = 200, Left = SystemParameters.WorkArea.Right - 340, Top = SystemParameters.WorkArea.Bottom - 220, ShowActivated = false, ShowInTaskbar = false, Content = new System.Windows.Controls.Border { Background = Brushes.DarkSlateGray } };
        try
        {
            bool crashedBefore = DiagnosticLog.CrashFile is { } cf && File.Exists(cf);
            if (crashedBefore) Check("After --crash-test: the unexpected-exit note is shown and the crash was logged as Critical", UnexpectedExitNote.Visibility == Visibility.Visible && DiagnosticLog.ReadLines().Any(l => l.Contains("\"Level\":\"Critical\"") && l.Contains("Simulated fatal error")), new { note = UnexpectedExitNote.Visibility.ToString() });

            // 1. Format: one JSON line, UTC time, level, version, component, message, exception with stack.
            try { throw new IOException("Simulated I/O failure (diagnostics test)"); } catch (IOException e) { DiagnosticLog.Error("diag-test", "Controlled error for the diagnostics test", e); }
            var line = Logged("diag-test").LastOrDefault() ?? "";
            bool format = false;
            try { using var d = JsonDocument.Parse(line); var r = d.RootElement; format = r.GetProperty("At").GetDateTimeOffset().Offset == TimeSpan.Zero && r.GetProperty("Level").GetString() == "Error" && r.GetProperty("Version").GetString() == DiagnosticLog.Version && r.GetProperty("Exception").GetString()!.Contains(" at ") && r.GetProperty("Error").GetString() == "System.IO.IOException"; } catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
            Check("Log line: JSON with UTC time, level, version, component, message, exception type and stack", format && DiagnosticLog.LogsFolder is { } lf && Directory.GetFiles(lf, "FRAME-*.log").Length > 0, line.Length > 300 ? line[..300] : line);
            Check("Start-up line names version, Windows build and .NET", Logged("startup").LastOrDefault() is string s && s.Contains("FRAME " + DiagnosticLog.Version) && s.Contains("build") && s.Contains(".NET"));

            // 2. Hotkey already held by another window → registration failure is reported and logged.
            var holder = new HwndSource(new HwndSourceParameters("FRAME diagnostics hotkey holder") { Width = 1, Height = 1, PositionX = -20000, PositionY = -20000 });
            var replay = settings.Hotkeys.First(h => h.Action == "Replay");
            bool held = Native.RegisterHotKey(holder.Handle, 9001, replay.Modifiers | 0x4000, replay.Key);
            RegisterKeys(); bool reported = hotkeyIssue.Contains("Replay");
            Native.UnregisterHotKey(holder.Handle, 9001); holder.Dispose();
            Check("Hotkey held by another window: reported and logged as a warning", held && reported && Logged("hotkeys").Any(l => l.Contains("\"Level\":\"Warning\"") && l.Contains("Replay")), new { held, hotkeyIssue });
            foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, id); hotkeys.Clear();

            // Capture source for 3 and 4: a small window that changes colour.
            target.Show(); await Task.Delay(300); Native.GetWindowRect(new WindowInteropHelper(target).Handle, out var r0);
            testSource = [new CaptureTarget("diagnostics-test", "Diagnostics test window", r0.Box, 0, new WindowInteropHelper(target).Handle)];
            settings.ReplaySeconds = 10; settings.Fps = 30; settings.BitrateMbps = 6; settings.DesktopAudio = settings.Microphone = false;
            int colour = 0; var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; tick.Tick += (_, _) => ((System.Windows.Controls.Border)target.Content).Background = (colour++ % 2 == 0) ? Brushes.DarkSlateGray : Brushes.SlateBlue; tick.Start();

            // 3. Encoder initialisation failure: an encoder FFmpeg does not know → start fails with FFmpeg's message, logged.
            forcedEncoder = new("h264_frame_test_missing", "Missing encoder (diagnostics test)", true, false, false);
            await Act(ToggleShadow);
            forcedEncoder = null;
            var startFailure = Logged("action").LastOrDefault() ?? "";
            Check("Encoder that FFmpeg rejects: Shadow Recording does not start; error logged with FFmpeg's message", !engine.Replay && startFailure.Contains("\"Level\":\"Error\"") && startFailure.Contains("h264_frame_test_missing"), startFailure.Length > 400 ? startFailure[..400] : startFailure);

            // 4. Capture folder that becomes unusable while recording (removed, a file of the same name in its place: e.g. a
            // disconnected drive) → the replay save fails and is logged; Shadow Recording keeps running. (A folder that
            // cannot be created at start makes the start fail instead; that is logged too.)
            string original = settings.OutputDirectory, vanishing = Path.Combine(data, "vanishing-clips"); settings.OutputDirectory = vanishing;
            await Act(ToggleShadow); bool started = engine.Replay; await Task.Delay(4000);
            if (Directory.Exists(vanishing)) Directory.Delete(vanishing, true); File.WriteAllText(vanishing, "a file where the capture folder was");
            await RequestSaveReplay();
            var saveFailure = Logged("action").LastOrDefault() ?? "";
            Check("Replay save into an unusable capture folder: fails, error logged, Shadow Recording keeps running", started && engine.Replay && saveFailure.Contains("\"Level\":\"Error\"") && saveFailure.Contains("SaveShadow"), saveFailure.Length > 400 ? saveFailure[..400] : saveFailure);
            await Act(ToggleShadow); settings.OutputDirectory = original; tick.Stop(); target.Close();

            // 5. Crash policy: ordinary failures keep FRAME running; corrupted-state ones and failure loops end it.
            bool ordinary = !CrashPolicy.IsFatal(new IOException("x"), true, 0), corrupted = CrashPolicy.IsFatal(new OutOfMemoryException(), true, 0), early = CrashPolicy.IsFatal(new IOException("x"), false, 0);
            bool loop = Enumerable.Range(0, 5).Select(i => CrashPolicy.IsFatal(new InvalidOperationException("x"), true, 100_000 + i)).ToArray().Last(); // ToArray: Range+Select+Last would evaluate only the last call
            Check("Crash policy: I/O failure continues; OutOfMemory, pre-window and a burst of 5 in a minute are fatal", ordinary && corrupted && early && loop, new { ordinary, corrupted, early, loop });

            // 6. Export diagnostics: contents, redaction, nothing private.
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            DiagnosticLog.Write("diag-test", $"Redaction probe: token=abc123secret password: hunter2 mail tester.person@example.com path {Path.Combine(profile, "Videos", "Private clip.mp4")} user {Environment.UserName} on {Environment.MachineName}");
            Directory.CreateDirectory(Path.Combine(data, "Recovery", "diag-session")); File.WriteAllText(Path.Combine(data, "Recovery", "diag-session", "part-00000001.mkv"), "not a real video");
            File.WriteAllText(Path.Combine(data, "Recovery", "diag-session", "engine.log"), "[h264_nvenc @ 0x1] simulated FFmpeg error line");
            Directory.CreateDirectory(Path.Combine(data, "Thumbnails")); File.WriteAllText(Path.Combine(data, "Thumbnails", "thumb.png"), "png");
            var withShare = CloneSettings(); withShare.ShareCustom = new CustomDestination("Private place", "https://example.com/upload", "custom"); SettingsStore.Save(configPath, withShare);
            string zip = Path.Combine(data, "export", $"FRAME-Diagnostics-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");
            var result = await ExportDiagnosticsTo(zip);
            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(e => e.FullName).ToArray();
            string all = string.Join("\n", archive.Entries.Select(e => { using var rd = new StreamReader(e.Open()); return e.FullName + "\n" + rd.ReadToEnd(); }));
            string summary = new StreamReader(archive.GetEntry("summary.txt")!.Open()).ReadToEnd();
            Check("Export: ZIP with summary, README, logs, settings and FFmpeg's error log of a failed session", names.Contains("summary.txt") && names.Contains("README.txt") && names.Contains("settings.json") && names.Any(n => n.StartsWith("logs/FRAME-")) && names.Any(n => n.StartsWith("engine/")), names);
            Check("Export: no videos, segments, thumbnails or images", !names.Any(n => n.EndsWith(".mp4") || n.EndsWith(".mkv") || n.EndsWith(".png") || n.EndsWith(".jpg") || n.EndsWith(".wav") || n.Contains("Thumbnails")), names);
            bool userHidden = !System.Text.RegularExpressions.Regex.IsMatch(all, @"(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(Environment.UserName) + @"(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Check("Export: profile path, user name, computer name, e-mail and secrets are redacted", !all.Contains(profile, StringComparison.OrdinalIgnoreCase) && userHidden && !all.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && !all.Contains("tester.person@example.com") && !all.Contains("hunter2") && !all.Contains("abc123secret") && all.Contains("%USERPROFILE%") && all.Contains("<email>") && all.Contains("<redacted>"), new { userHidden });
            Check("Export: settings without the custom share destination", !all.Contains("Private place") && !all.Contains("example.com/upload"));
            Check("Export summary: FRAME version, Windows, CPU, GPU, displays, FFmpeg version and encoders, recent errors", summary.Contains("FRAME " + DiagnosticLog.Version) && summary.Contains("CPU: ") && summary.Contains("GPU: ") && summary.Contains("Displays") && summary.Contains("ffmpeg version") && summary.Contains("Encoders that passed the probe") && summary.Contains("Controlled error for the diagnostics test"), summary.Length > 1500 ? summary[..1500] : summary);
            if (crashedBefore) Check("Export after a crash: crash details and the unexpected-exit record are included", names.Any(n => n.StartsWith("crash/crash")) && names.Contains("crash/unexpected-exit.json"), names);
            Check("Export is logged", Logged("diagnostics").Any(l => l.Contains("Diagnostics exported")));
            SettingsStore.Save(configPath, CloneSettings()); // the live settings (ShareCustom was only for the export check)
        }
        catch (Exception e) { Check("Exception", false, e.ToString()); }
        finally
        {
            if (target.IsVisible) target.Close();
            DiagnosticLog.Flush();
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = results.Count(x => (bool)x.GetType().GetProperty("passed")!.GetValue(x)!), total = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
