namespace Frame.Core;

// Open hotkey (Ctrl+Alt+F): where FRAME's window goes on the primary display. Pure, so multi-monitor cases (negative
// coordinates, different scaling, a window larger than the work area) are unit-tested.
public static class WindowPlacement
{
    // Window rectangle in physical pixels moved onto a display's work area (physical pixels): the size is converted from the
    // source display's scale to the target's (WPF keeps the window's DIP size across a DPI change), clamped to the work
    // area and centred in it.
    public static Box CentreOn(Box window, Box work, double sourceScale, double targetScale)
    {
        double factor = sourceScale > 0 && targetScale > 0 ? targetScale / sourceScale : 1;
        int width = Math.Max(1, Math.Min(work.Width, (int)Math.Round(window.Width * factor))), height = Math.Max(1, Math.Min(work.Height, (int)Math.Round(window.Height * factor)));
        return new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
    }
}
