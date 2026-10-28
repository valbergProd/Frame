namespace Frame.Core;

// Pure helpers for the save confirmation toast (wording and placement), kept testable outside WPF.
public static class SaveFeedback
{
    // Natural wording of the actual saved length: "Last 42 seconds saved", "Last 5 minutes saved", "Last 2 min 30 s saved".
    public static string Saved(double seconds, int clips = 1)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return clips > 1 ? $"{clips} clips saved" : "Clip saved";
        int s = (int)Math.Round(seconds);
        string span = s < 60 ? $"{s} second{(s == 1 ? "" : "s")}"
            : s % 60 is <= 2 or >= 58 ? $"{(int)Math.Round(s / 60d)} minute{(Math.Round(s / 60d) == 1 ? "" : "s")}"
            : $"{s / 60} min {s % 60} s";
        return (clips > 1 ? $"{clips} clips · " : "") + $"Last {span} saved";
    }
    public static string Failed(string reason)
    {
        var text = (reason ?? "").Trim().Split('\n')[0];
        return text.Length == 0 ? "The clip could not be saved" : text.Length > 90 ? text[..87] + "…" : text;
    }
}

public static class ToastPlacement
{
    // Top-right of a monitor work area with a margin; clamped so the toast never leaves the work area.
    public static (int X, int Y) TopRight(Box work, int width, int height, double margin)
    {
        int m = (int)Math.Round(margin);
        int x = Math.Max(work.X, work.Right - width - m), y = Math.Min(work.Y + m, Math.Max(work.Y, work.Bottom - height));
        return (x, y);
    }
}
