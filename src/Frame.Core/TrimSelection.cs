using System.Globalization;

namespace Frame.Core;

// Trim editor selection state. Pure and UI-free: the editor timeline, keyboard and time fields all go through it,
// so the start can never pass the end and both stay inside the source.
public sealed class TrimSelection
{
    public double Duration { get; }
    public double FrameSeconds { get; }
    public double MinLength => FrameSeconds;
    public double Start { get; private set; }
    public double End { get; private set; }
    public double Length => End - Start;
    public event Action? Changed;

    public TrimSelection(double duration, double fps)
    {
        if (!(duration > 0) || !double.IsFinite(duration)) throw new ArgumentException("The recording has no usable duration.");
        Duration = duration;
        FrameSeconds = fps is > 0 and < 1000 ? 1 / fps : 1 / 60d;
        if (FrameSeconds > duration) FrameSeconds = duration;
        End = duration;
    }

    // Nominal frame grid (source FPS). Exports re-encode on exact timestamps, so this is a UI convenience, not a guarantee for VFR media.
    public double Snap(double t) => Math.Clamp(Math.Round(t / FrameSeconds) * FrameSeconds, 0, Duration);

    public void SetStart(double t)
    {
        if (!double.IsFinite(t)) return;
        double next = Math.Clamp(Snap(t), 0, Math.Max(0, End - MinLength));
        if (next == Start) return; Start = next; Changed?.Invoke();
    }
    public void SetEnd(double t)
    {
        if (!double.IsFinite(t)) return;
        double next = t >= Duration - FrameSeconds / 2 ? Duration : Snap(t);
        next = Math.Clamp(next, Math.Min(Duration, Start + MinLength), Duration);
        if (next == End) return; End = next; Changed?.Invoke();
    }
    public void Set(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end - start < MinLength - 1e-9 || start < 0 || end > Duration + 1e-9) throw new ArgumentException("Choose an end after the start, inside the recording.");
        Start = Snap(start); End = end >= Duration - FrameSeconds / 2 ? Duration : Snap(end);
        if (End - Start < MinLength - 1e-9) End = Math.Min(Duration, Start + MinLength);
        Changed?.Invoke();
    }
    public bool IsWhole => Start <= 1e-9 && End >= Duration - 1e-9;
    public TimeRange Range => new(Start, End);
}

public static class TimeText
{
    // 1:05.250 or 1:02:03.000; always three decimals so frame-level values stay visible.
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}" : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
    public static string Short(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds))); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}"; }
    // Accepts "75", "75.5", "1:15.5", "0:01:15.5" (invariant decimal point or comma).
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3) return false;
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v) || v < 0) return false;
            if (!last && v != Math.Floor(v)) return false;
            if (i > 0 && v >= 60) return false;
            total = total * 60 + v;
        }
        seconds = total; return double.IsFinite(total);
    }
}
