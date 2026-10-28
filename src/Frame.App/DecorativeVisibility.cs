using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Frame.App;

// Whether decorative motion can be seen or is wanted, from events only (never polled; capture services untouched):
// - Cloaked: DWM hides windows on other virtual desktops without changing WPF IsVisible (this process's cloak WinEvents).
// - Obscured: the session is locked or disconnected (WM_WTSSESSION_CHANGE), the display is off (console display
//   state power notification), or another desktop has the input (0.6.0: a secure screen saver, the lock or UAC screen;
//   EVENT_SYSTEM_DESKTOPSWITCH), so nothing reaches a screen.
// - ReducedMotion: Windows "Show animations" is off (SPI_GETCLIENTAREAANIMATION), re-read on WM_SETTINGCHANGE.
sealed class DecorativeVisibility : IDisposable
{
    readonly Window window;
    readonly WinEvent callback, desktopCallback;
    nint hook, desktopHook, handle, power;
    HwndSource? source;
    bool disposed, session, locked, displayOff, otherDesktop;
    public bool Cloaked { get; private set; }
    public bool Obscured => locked || displayOff || otherDesktop;
    public bool ReducedMotion { get; private set; } = !SystemParameters.ClientAreaAnimation;
    public event Action? Changed;
    public DecorativeVisibility(Window window)
    {
        this.window = window; callback = Event; desktopCallback = DesktopEvent;
        window.SourceInitialized += Initialize; window.Closed += Closed;
    }
    void Initialize(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(window).Handle;
        Refresh(); ReadMotion();
        hook = SetWinEventHook(0x8017, 0x8018, 0, callback, (uint)Environment.ProcessId, 0, 0);
        source = HwndSource.FromHwnd(handle); source?.AddHook(Message);
        session = WTSRegisterSessionNotification(handle, 0);
        var display = DisplayState; power = RegisterPowerSettingNotification(handle, ref display, 0);
        ReadDesktop(); desktopHook = SetWinEventHook(0x0020, 0x0020, 0, desktopCallback, 0, 0, 0); // all processes, out of context
    }
    void DesktopEvent(nint h, uint kind, nint hwnd, int objectId, int child, uint thread, uint time)
    {
        if (!disposed) window.Dispatcher.InvokeAsync(() => { if (!disposed) ReadDesktop(); });
    }
    // The input desktop is the user's "Default" desktop; a secure desktop (screen saver, lock, UAC) cannot be opened or has
    // another name. Read once at start and on every desktop switch, never polled.
    void ReadDesktop()
    {
        bool other = true; nint desktop = OpenInputDesktop(0, false, 0x0001 /* DESKTOP_READOBJECTS */);
        if (desktop != 0)
        {
            try { var name = new char[64]; other = !GetUserObjectInformation(desktop, 2 /* UOI_NAME */, name, name.Length * 2, out _) || new string(name).TrimEnd((char)0) != "Default"; }
            finally { CloseDesktop(desktop); }
        }
        if (other != otherDesktop) { otherDesktop = other; Changed?.Invoke(); }
    }
    void Event(nint h, uint kind, nint hwnd, int objectId, int child, uint thread, uint time)
    {
        if (hwnd == handle && objectId == 0 && !disposed)
            window.Dispatcher.InvokeAsync(() => { if (!disposed) Refresh(); });
    }
    void Refresh()
    {
        bool next = DwmGetWindowAttribute(handle, 14, out int value, sizeof(int)) == 0 && value != 0;
        if (Cloaked == next) return;
        Cloaked = next; Changed?.Invoke();
    }
    void ReadMotion()
    {
        bool reduced = SystemParametersInfo(0x1042, 0, out int animate, 0) ? animate == 0 : !SystemParameters.ClientAreaAnimation;
        if (reduced == ReducedMotion) return;
        ReducedMotion = reduced; Changed?.Invoke();
    }
    nint Message(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (disposed) return 0;
        if (msg == 0x001A) ReadMotion(); // WM_SETTINGCHANGE (SPI_SETCLIENTAREAANIMATION and others; one cheap re-read)
        else if (msg == 0x02B1) // WM_WTSSESSION_CHANGE: 2/4 disconnect, 1/3 connect, 7 lock, 8 unlock
        {
            int code = (int)wParam; bool next = code is 2 or 4 or 7 ? true : code is 1 or 3 or 8 ? false : locked;
            if (next != locked) { locked = next; Changed?.Invoke(); }
        }
        else if (msg == 0x0218 && (int)wParam == 0x8013 && lParam != 0 && Marshal.PtrToStructure<Guid>(lParam) == DisplayState) // PBT_POWERSETTINGCHANGE
        {
            bool next = Marshal.ReadInt32(lParam, 20) == 0; // 0 off, 1 on, 2 dimmed
            if (next != displayOff) { displayOff = next; Changed?.Invoke(); }
        }
        return 0;
    }
    static readonly Guid DisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47"); // GUID_CONSOLE_DISPLAY_STATE
    void Closed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (hook != 0) { UnhookWinEvent(hook); hook = 0; }
        if (desktopHook != 0) { UnhookWinEvent(desktopHook); desktopHook = 0; }
        source?.RemoveHook(Message); source = null;
        if (session) { WTSUnRegisterSessionNotification(handle); session = false; }
        if (power != 0) { UnregisterPowerSettingNotification(power); power = 0; }
        window.SourceInitialized -= Initialize; window.Closed -= Closed; Changed = null;
    }
    delegate void WinEvent(nint hook, uint kind, nint hwnd, int objectId, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(nint hook);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
    [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(nint hwnd, int flags);
    [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("user32.dll")] static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, int flags);
    [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("user32.dll", SetLastError = true)] static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(nint handle, int index, [Out] char[] info, int length, out int needed);
    [DllImport("user32.dll")] static extern bool CloseDesktop(nint desktop);
}
