using System.Windows;

namespace Frame.App;

public partial class App : Application
{
    Mutex? mutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--update-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await UpdateDiagnostics.Run(e.Args[1], e.Args[2]); Shutdown(); }
            catch (Exception error) { Directory.CreateDirectory(e.Args[1]); await File.WriteAllTextAsync(Path.Combine(e.Args[1], "error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--update-handoff-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await UpdateInstaller.Apply(e.Args[1], _ => File.WriteAllText(e.Args[2], "{\"passed\":true,\"installerLaunched\":false}")); Shutdown(); }
            catch (Exception error) { await File.WriteAllTextAsync(e.Args[2], System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.Message })); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--apply-update")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await UpdateInstaller.Apply(e.Args[1]); Shutdown(); }
            catch (Exception error) { QuietDialog.Inform(null, "Update not installed", error.Message); Shutdown(1); }
            return;
        }
        TooltipPolish.Install();
        int perf = Array.IndexOf(e.Args, "--export-perf");
        if (perf >= 0) { int ff = Array.IndexOf(e.Args, "--ffmpeg"); int code; try { code = await MediaValidation.ExportPerformance(e.Args[perf + 1], ff >= 0 ? e.Args[ff + 1] : "", e.Args); } catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args[perf + 1], "fatal.txt"), ex.ToString()); code = 1; } Shutdown(code); return; }
        int test = Array.IndexOf(e.Args, "--media-test");
        if (test >= 0) { int code; try { int ff = Array.IndexOf(e.Args, "--ffmpeg"); code = await MediaValidation.Run(e.Args[test + 1], ff >= 0 ? e.Args[ff + 1] : ""); } catch (Exception ex) { Directory.CreateDirectory(e.Args[test + 1]); File.WriteAllText(Path.Combine(e.Args[test + 1], "fatal.txt"), ex.ToString()); code = 1; } Shutdown(code); return; }
        // Isolated, non-capturing UI diagnostics may coexist with the user's running app.
        int dataIndex = Array.IndexOf(e.Args, "--data");
        bool isolatedDiagnostic = dataIndex >= 0 && dataIndex + 1 < e.Args.Length && e.Args.Any(a => a is "--render" or "--hotkey-ui-test" or "--background-test" or "--capture-test" or "--audio-test" or "--game-test" or "--open-hotkey-test" or "--live-length-test" or "--anim-benchmark" or "--crash-test" or "--capture-ui-test" or "--library-test" or "--resource-test" or "--quality-change-test" or "--close-test" or "--audio-quality-test" or "--still-stop-test" or "--mode-switch-test");
        string identity = isolatedDiagnostic ? "Local\\FRAME-Diagnostic-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(e.Args[dataIndex + 1]))))[..16] : "Local\\FRAME-Desktop-0.1";
        mutex = new Mutex(true, identity, out bool first);
        if (!first) { QuietDialog.Inform(null, "FRAME is already running", "FRAME is running in the system tray. Double-click its tray icon or press the Show FRAME window hotkey (Ctrl+Alt+F by default)."); Shutdown(); return; } // silent: no message-box sound
        // Diagnostics first, so even a failure while the window is built is logged (same data folder as MainWindow).
        string data = dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? e.Args[dataIndex + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FRAME");
        try { Directory.CreateDirectory(data); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        DiagnosticLog.Folder = data; DiagnosticLog.BeginSession();
        // Capture FFmpeg left running by an earlier FRAME that crashed or was killed (0.6.2: new FFmpeg processes are tied to FRAME's job object).
        _ = Task.Run(() => { try { ProcessJob.KillOrphans(Path.Combine(data, "Recovery")); } catch (Exception ex) { DiagnosticLog.Warning("orphan-ffmpeg", "Orphan check failed", ex); } });
        // Fatal paths: logged synchronously (no UI), then the process ends as .NET decides; nothing is swallowed here.
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { if (a.ExceptionObject is Exception ex) DiagnosticLog.Crash(a.IsTerminating ? "background thread" : "AppDomain", ex); };
        // Faulted tasks nobody awaited: logged (they do not end the process in .NET 8).
        // AudioPipe's writer ends with "Pipe is broken" when FFmpeg closes the audio input at a stop (pre-existing, harmless:
        // the audio test passes); logged as Info so it does not look like a problem in exported summaries.
        TaskScheduler.UnobservedTaskException += (_, a) => {
            bool pipeClosed = a.Exception.InnerExceptions.All(x => x is IOException io && io.Message.Contains("pipe", StringComparison.OrdinalIgnoreCase) && io.StackTrace?.Contains("AudioPipe") == true);
            if (pipeClosed) DiagnosticLog.Write("audio-pipe", "Audio writer ended: FFmpeg closed the audio pipe (normal when a recording stops).");
            else DiagnosticLog.Warning("unobserved-task", "A background task failed without being observed.", a.Exception);
            a.SetObserved();
        };
        DispatcherUnhandledException += (_, a) => {
            int diagnostic = new[] { "--render", "--idle-benchmark", "--hotkey-ui-test" }.Select(a => Array.IndexOf(e.Args, a)).Max();
            if (diagnostic >= 0) { File.WriteAllText(e.Args[diagnostic + 1] + ".error.txt", a.Exception.ToString()); DiagnosticLog.Error("unhandled", "Unhandled exception in a diagnostic run", a.Exception); DiagnosticLog.Flush(); a.Handled = true; Shutdown(1); return; }
            // UI-thread exceptions: an ordinary failure (I/O, access, invalid state, external process, bad input) is logged
            // and reported and FRAME keeps running, as before. Corrupted-state failures, failures before the main window is
            // up, or a burst of failures are logged as Critical and NOT handled, so FRAME closes instead of running on in an
            // unknown state (the session marker then offers Export diagnostics at the next start).
            if (CrashPolicy.IsFatal(a.Exception, MainWindow is MainWindow { IsLoaded: true }, Environment.TickCount64))
            {
                DiagnosticLog.Crash("UI thread", a.Exception); return;
            }
            DiagnosticLog.Error("unhandled", "Unhandled exception on the UI thread (FRAME continues): " + a.Exception.Message, a.Exception);
            if (MainWindow?.IsVisible == true) QuietDialog.Inform(MainWindow, "Action failed", a.Exception.Message); else SaveOverlay.Present("FRAME", SaveFeedback.Failed(a.Exception.Message), false);
            a.Handled = true;
        };
        // --crash-test (isolated --data only): an exception on a background thread, i.e. a real fatal crash of the process.
        if (isolatedDiagnostic && e.Args.Contains("--crash-test")) { new Thread(() => throw new InvalidOperationException("Simulated fatal error (--crash-test)")) { IsBackground = true }.Start(); await Task.Delay(10000); }
        var window = new MainWindow(e.Args); MainWindow = window;
        // Start-with-Windows launch: open minimized without a taskbar button; OnLoaded moves it to the tray before it paints.
        if (e.Args.Contains("--background")) { window.ShowInTaskbar = false; window.WindowState = WindowState.Minimized; }
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { if (mutex != null && DiagnosticLog.Folder != null) DiagnosticLog.EndSession(); mutex?.Dispose(); base.OnExit(e); }
}

// Which UI-thread exceptions end FRAME (see DispatcherUnhandledException): corrupted-state and runtime failures, anything
// before the main window has loaded, or the fifth unhandled exception within a minute (a failure loop).
static class CrashPolicy
{
    static readonly Queue<long> Recent = new();
    public static bool IsFatal(Exception error, bool windowLoaded, long nowMs)
    {
        var inner = error is System.Reflection.TargetInvocationException { InnerException: { } i } ? i : error;
        if (inner is OutOfMemoryException or InsufficientExecutionStackException or AccessViolationException or System.Runtime.InteropServices.SEHException or BadImageFormatException or InvalidProgramException or TypeInitializationException or TypeLoadException or MissingMemberException or FileLoadException) return true;
        if (!windowLoaded) return true;
        while (Recent.Count > 0 && nowMs - Recent.Peek() > 60_000) Recent.Dequeue();
        Recent.Enqueue(nowMs);
        return Recent.Count >= 5;
    }
}
