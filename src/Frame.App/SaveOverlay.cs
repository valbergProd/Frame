using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shapes = System.Windows.Shapes;

namespace Frame.App;

// Tiny "Clip saved" toast near the top-right of the display the user is looking at (foreground window's monitor).
// Created on demand, one at a time (a new save replaces the text and restarts the fade), click-through, never activated,
// excluded from screen capture where Windows supports it, and closed after its single ~2.5 s fade. No render loop or hooks.
// Exclusive-fullscreen games may draw over it; borderless/windowed games show it.
sealed class SaveOverlay : Window
{
    public static SaveOverlay? Current { get; private set; }
    // Last headings shown (bounded), for diagnostics and tests; never persisted.
    public static List<string> History { get; } = [];
    public const double EdgeMargin = 24;
    readonly TextBlock title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(245, 238, 230)) };
    readonly TextBlock detail = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(199, 184, 170)), Margin = new Thickness(0, 2, 0, 0) };
    readonly Shapes.Path mark = new() { Width = 16, Height = 16, Stretch = Stretch.Uniform, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center };
    readonly Border card;
    Storyboard? fade;
    nint target; // foreground window at show time, used for monitor choice
    public bool Success { get; private set; }
    public bool Pending { get; private set; }
    public int ExtendedStyle { get; private set; }
    public uint DisplayAffinity { get; private set; }

    SaveOverlay()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false; Focusable = false; IsHitTestVisible = false; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; Opacity = 0; Title = "FRAME notification";
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(title); text.Children.Add(detail);
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(mark); row.Children.Add(text);
        card = new Border { Child = row, Padding = new Thickness(16, 11, 20, 12), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(214, 23, 19, 16)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 229, 172, 82)), BorderThickness = new Thickness(1) };
        Content = card;
        SourceInitialized += (_, _) => Prepare();
        Closed += (_, _) => { fade?.Stop(this); fade = null; if (Current == this) Current = null; };
    }

    // pending: an accepted request still in progress ("Saving replay…"); gold open ring, never a check, so it cannot read as saved.
    public static SaveOverlay Present(string heading, string text, bool success, bool pending = false)
    {
        var overlay = Current ?? new SaveOverlay();
        Current = overlay;
        overlay.target = GetForegroundWindow();
        overlay.Success = success && !pending; overlay.Pending = pending; overlay.title.Text = heading; overlay.detail.Text = text;
        History.Add(heading); if (History.Count > 20) History.RemoveAt(0);
        // Gold check for success; muted ember ring with a bar for failure (distinct shape, not only color).
        overlay.mark.Data = Geometry.Parse(pending ? "M 8,2.5 A 5.5,5.5 0 1 1 2.5,8" : success ? "M 2,8.5 L 6.5,13 L 14,3.5" : "M 8,3 L 8,9.5 M 8,13 L 8,13.2");
        overlay.mark.Stroke = new SolidColorBrush(success ? Color.FromRgb(229, 172, 82) : Color.FromRgb(214, 110, 70));
        overlay.card.BorderBrush = new SolidColorBrush(success ? Color.FromArgb(70, 229, 172, 82) : Color.FromArgb(90, 214, 110, 70));
        if (!overlay.IsVisible) overlay.Show(); else overlay.Place();
        overlay.Play();
        return overlay;
    }

    void Prepare()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Click-through, never activated, no taskbar/Alt+Tab entry.
        int style = GetWindowLong(hwnd, -20) | 0x20 | 0x80000 | 0x8000000 | 0x80; // TRANSPARENT | LAYERED | NOACTIVATE | TOOLWINDOW
        SetWindowLong(hwnd, -20, style); ExtendedStyle = GetWindowLong(hwnd, -20);
        // Keep the toast out of FRAME's own and other screen captures (Windows 10 2004+); harmless if unsupported.
        if (SetWindowDisplayAffinity(hwnd, 0x11) && GetWindowDisplayAffinity(hwnd, out uint affinity)) DisplayAffinity = affinity;
        ContentRendered += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place);
    }

    // Physical-pixel placement on the foreground window's monitor work area (falls back to the primary display).
    public void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == 0) return;
        var monitor = MonitorFromWindow(target != 0 ? target : hwnd, 1);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var r)) return;
        GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        var (x, y) = ToastPlacement.TopRight(new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top), r.Right - r.Left, r.Bottom - r.Top, EdgeMargin * Math.Max(96, dpi) / 96d);
        SetWindowPos(hwnd, -1, x, y, 0, 0, 0x0001 | 0x0010 | 0x0400); // NOSIZE | NOACTIVATE | NOSENDCHANGING; HWND_TOPMOST
        PlacedAt = new Box(x, y, r.Right - r.Left, r.Bottom - r.Top); MonitorWork = new Box(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }
    public Box? PlacedAt { get; private set; }
    public Box? MonitorWork { get; private set; }

    // One storyboard: 0.18 s fade in, hold, 0.35 s fade out, then close (≈2.5 s). Restarted, not stacked, on repeat saves.
    void Play()
    {
        fade?.Stop(this);
        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.18)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.15))));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));
        var board = new Storyboard(); board.Children.Add(opacity);
        board.Completed += (_, _) => { if (fade == board) Close(); };
        fade = board; board.Begin(this, true);
    }

    [StructLayout(LayoutKind.Sequential)] struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out Rect32 rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
}
