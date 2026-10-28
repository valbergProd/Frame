using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Frame.App;

public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public readonly Box Box => new(Left, Top, Right - Left, Bottom - Top); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);
    // Brings a window of this process to the front. Windows may refuse SetForegroundWindow while another app (e.g. a
    // full-screen game) holds the foreground; joining that thread's input state for the call is the documented way
    // around the foreground lock (no injection into the other process; detached immediately).
    public static bool ForceForeground(nint hwnd)
    {
        if (GetForegroundWindow() == hwnd) return true;
        SetForegroundWindow(hwnd); if (GetForegroundWindow() == hwnd) return true;
        uint foreign = GetWindowThreadProcessId(GetForegroundWindow(), out _), own = GetCurrentThreadId();
        bool attached = foreign != 0 && foreign != own && AttachThreadInput(own, foreign, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }
        finally { if (attached) AttachThreadInput(own, foreign, false); }
        return GetForegroundWindow() == hwnd;
    }
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(System.Drawing.Point point, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    public const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    // Windows' primary display (the one containing 0,0) and the display a window is on (nearest), as monitor handles.
    public static nint PrimaryMonitor() => MonitorFromPoint(new System.Drawing.Point(0, 0), 1 /* MONITOR_DEFAULTTOPRIMARY */);
    public static nint MonitorOf(nint hwnd) => MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
    // Work area (excludes the taskbar) in physical pixels, and the display's effective scale (1.0 = 96 DPI).
    public static Box? WorkArea(nint monitor) { var i = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" }; return monitor != 0 && GetMonitorInfo(monitor, ref i) ? i.Work.Box : null; }
    public static double Scale(nint monitor) => monitor != 0 && GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1;
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    public static CaptureTarget[] Displays()
    {
        var result = new List<CaptureTarget>();
        EnumDisplayMonitors(0, 0, (nint handle, nint dc, ref Rect r, nint data) => {
            var i = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" }; GetMonitorInfo(handle, ref i);
            result.Add(new(i.Device, $"Display {result.Count + 1}{((i.Flags & 1) != 0 ? " · Primary" : "")}", i.Monitor.Box, handle)); return true;
        }, 0);
        return result.ToArray();
    }
    public static void Decorate(Window w, bool exclude = false)
    {
        var h = new WindowInteropHelper(w).Handle; int dark = 1; DwmSetWindowAttribute(h, 20, ref dark, 4);
        if (exclude) SetWindowDisplayAffinity(h, 0x11);
    }
}
