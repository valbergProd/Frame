using System.Windows;
using System.Windows.Threading;

namespace Frame.App;

// "Minimize FRAME when a game starts" (0.5.4). Event-driven: reuses ReplayGameTracker's foreground WinEvent hook, so
// nothing runs until the foreground window changes; then one process-name query (and, for unknown programs, a single
// delayed fullscreen check). No polling, process scans, injection or hooks into games. Hiding goes to the tray exactly
// like the close button, so capture, hotkeys and the tray keep running and decorative animation stops.
public partial class MainWindow
{
    readonly GameMinimizePolicy gamePolicy = new();
    DispatcherTimer? fullscreenCheck;
    nint fullscreenCandidate;
    long lastInteraction;
    internal int GameMinimizeCount { get; private set; }

    void SetupGameMinimize()
    {
        gameTracker.Foreground += OnForegroundChanged;
        PreviewMouseDown += (_, _) => lastInteraction = Environment.TickCount64;
        PreviewKeyDown += (_, _) => lastInteraction = Environment.TickCount64;
    }

    void OnForegroundChanged(nint hwnd)
    {
        fullscreenCheck?.Stop();
        if (quitting || !settings.MinimizeOnGameStart || hwnd == 0) return;
        var (pid, exe) = ReplayGameTracker.Owner(hwnd);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return;
        if (GameDetection.KnownGame(exe)) { ConsiderGame(pid, exe); return; }
        // Unknown program: exclusive fullscreen is usually entered a moment after the window appears, so look once more.
        if (!IsVisible) return;
        fullscreenCandidate = hwnd;
        fullscreenCheck ??= CreateFullscreenCheck();
        fullscreenCheck.Start();
    }
    DispatcherTimer CreateFullscreenCheck()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (quitting || fullscreenCandidate == 0 || ForegroundWindow() != fullscreenCandidate) return;
            var (pid, exe) = ReplayGameTracker.Owner(fullscreenCandidate);
            if (GameDetection.IsGame(exe, ReplayGameTracker.NotificationState())) ConsiderGame(pid, exe);
        };
        return t;
    }

    // Busy = the user is working in FRAME: an editor/dialog is open, a hotkey is being recorded, or input in the last 2 s.
    bool FrameInUse() => hotkeyCaptureActive || Environment.TickCount64 - lastInteraction < 2000
        || Application.Current.Windows.OfType<Window>().Any(w => w != this && w is not SaveOverlay && w.IsVisible);

    internal bool ConsiderGame(uint pid, string? exe)
    {
        bool visible = IsVisible && WindowState != WindowState.Minimized;
        if (!gamePolicy.ShouldHide(pid, true, settings.MinimizeOnGameStart, visible, FrameInUse()) || tray == null) return false;
        Hide(); GameMinimizeCount++;
        DiagnosticLog.Write("game-minimize", (exe ?? "fullscreen app") + ": FRAME hidden to the tray");
        return true;
    }
}
