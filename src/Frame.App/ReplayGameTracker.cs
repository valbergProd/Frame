using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Frame.App;

// No window titles, screenshots, audio, network or process list. One query per captured target at the existing 1 Hz maintenance tick.
sealed class ReplayGameTracker : IDisposable
{
    delegate void EventCallback(nint hook, uint ev, nint hwnd, int obj, int child, uint thread, uint time);
    readonly EventCallback callback;
    nint hook;
    long epoch;
    // Foreground: raised on the UI thread (out-of-context hook on the creating thread) for each EVENT_SYSTEM_FOREGROUND.
    public event Action<nint>? Foreground;
    public ReplayGameTracker() { callback = (_, _, hwnd, _, _, _, _) => { Interlocked.Increment(ref epoch); Foreground?.Invoke(hwnd); }; hook = SetWinEventHook(3, 3, 0, callback, 0, 0, 0); }
    // Owning process id and executable name of a window (limited query rights; no handle kept open).
    public static (uint Pid, string? Exe) Owner(nint h)
    {
        if (h == 0) return (0, null);
        GetWindowThreadProcessId(h, out uint pid);
        nint process = OpenProcess(0x1000, false, pid);
        if (process == 0) return (pid, null);
        try { var path = new StringBuilder(1024); uint size = 1024; return (pid, QueryFullProcessImageName(process, 0, path, ref size) ? Path.GetFileName(path.ToString()) : null); }
        finally { CloseHandle(process); }
    }
    // Windows' own fullscreen classification (3 = Direct3D exclusive fullscreen); -1 if unavailable.
    public static int NotificationState() => SHQueryUserNotificationState(out int state) == 0 ? state : -1;
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    public GameObservation Observe(CaptureTarget[] sources)
    {
        long before = Interlocked.Read(ref epoch);
        if (hook == 0) return new(null, before); // Missed foreground transitions cannot be attributed reliably.
        string? common = null;
        foreach (var source in sources)
        {
            nint h = source.WindowHandle != 0 ? (nint)source.WindowHandle : GetForegroundWindow();
            if (h == 0 || IsIconic(h) || !Native.GetWindowRect(h, out var rect)) return new(null, before);
            GetWindowThreadProcessId(h, out uint pid);
            nint process = OpenProcess(0x1000, false, pid);
            if (process == 0) return new(null, before);
            string? folder;
            try { var path = new StringBuilder(1024); uint size = 1024; if (!QueryFullProcessImageName(process, 0, path, ref size)) return new(null, before); folder = GameCatalog.ForSource(source, h, rect.Box, Path.GetFileName(path.ToString())); }
            finally { CloseHandle(process); }
            if (folder == null || (common != null && common != folder)) return new(null, before);
            common = folder;
        }
        return new(before == Interlocked.Read(ref epoch) ? common : null, before);
    }
    public void Dispose() { if (hook != 0) { UnhookWinEvent(hook); hook = 0; } }
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint min, uint max, nint module, EventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(nint hook);
    [DllImport("kernel32.dll")] static extern nint OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
}
