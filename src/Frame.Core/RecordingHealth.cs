namespace Frame.Core;

// What a save should contain when the buffer's newest closed segment ends before the save request
// (a still screen: Windows Graphics Capture delivers no frames, so the open segment never closes).
public sealed record LiveWindow(TimeRange Range, double Tail)
{
    // Tail = seconds after the last closed segment that must come from the open segment and the held last frame.
    public bool NeedsTail => Tail > .05;
}

public static class ReplayWindow
{
    // Shortest save offered: a few frames must exist (the first frame arrives within ~0.5 s of the start).
    public const double MinimumSeconds = .2;
    // The requested history ending at the save request ("live edge"), never earlier content than was recorded.
    // Closed segments inside the window must be continuous; the part after the last closed segment is the still tail.
    public static LiveWindow? Live(IEnumerable<Segment> segments, int requested, double liveEdge)
    {
        var a = segments.OrderBy(s => s.Start).ToArray();
        if (requested <= 0 || !double.IsFinite(liveEdge)) return null;
        // 0.5.4: the requested length is a maximum. Before the first one-second segment closes, the whole recording so far
        // is the tail (read from the open first segment), so a save right after starting gives ~what was recorded.
        if (a.Length == 0) return liveEdge >= MinimumSeconds ? new(new TimeRange(Math.Max(0, liveEdge - requested), liveEdge), liveEdge) : null;
        // 0.5.4: the window ends at the request (the live edge), not at the end of the segment that closed after it
        // (up to ~1 s later); the stream-copy save cuts that segment there. Start = request - requested history.
        double end = Math.Max(liveEdge, a[0].Start + MinimumSeconds), start = Math.Max(a[0].Start, end - requested);
        if (start < a[^1].End - .05)
            try { ReplayLogic.RequireCoverage(a, new TimeRange(start, a[^1].End)); }
            catch (Exception e) when (e is IOException or ArgumentException) { return null; }
        return new(new TimeRange(start, end), Math.Max(0, end - a[^1].End));
    }
}

public enum FaultKind { Disk, Encoder, Source, Audio, Start, Engine }

public sealed record RecordingFault(DateTimeOffset At, FaultKind Kind, string Title, string Explanation, string Suggestion, string Details, bool Shadow)
{
    public bool AutoRetryTried { get; init; }
    public bool Recovered { get; init; }
    public DateTimeOffset? RecoveredAt { get; init; }
    // Disk problems and repeated start failures are not retried automatically.
    public bool Retryable => Kind is FaultKind.Encoder or FaultKind.Engine or FaultKind.Source or FaultKind.Audio;
}

public sealed record FaultSignals(bool DiskLow, bool AudioFailed, bool StartFailure, int? ExitCode, string ErrorText);

public static class RecordingHealth
{
    // Plain-language classification from what the capture engine actually reported. The raw text stays in Details.
    public static RecordingFault Classify(FaultSignals s, bool shadow, DateTimeOffset at)
    {
        string text = (s.ErrorText ?? "").ToLowerInvariant();
        string what = shadow ? "Shadow Recording" : "Recording";
        string details = $"Exit code: {(s.ExitCode?.ToString() ?? "n/a")}{Environment.NewLine}{(s.ErrorText ?? "").Trim()}".Trim();
        RecordingFault Make(FaultKind kind, string explanation, string suggestion) => new(at, kind, what + " stopped unexpectedly", explanation, suggestion, details, shadow);
        if (s.DiskLow || text.Contains("disk space") || text.Contains("no space"))
            return Make(FaultKind.Disk, "Not enough storage space to continue recording.", "Free up disk space or choose another capture folder, then start Shadow Recording again.");
        // 0.5.4: lost/changed audio devices no longer stop recording (they degrade to silence and recover); what remains is
        // the capture engine no longer accepting audio, which a restart fixes.
        if (text.Contains("audio input"))
            return Make(FaultKind.Audio, "The capture engine stopped accepting audio.", "FRAME restarts Shadow Recording automatically. If this repeats, turn off Desktop audio / Microphone and send the technical details.");
        if (s.AudioFailed || text.Contains("audio device") || text.Contains("wasapi"))
            return Make(FaultKind.Audio, "An audio device stopped or became unavailable.", "Reconnect the device or turn off Desktop audio / Microphone, then restart recording.");
        if (s.StartFailure)
            return Make(FaultKind.Start, "Capture could not start on the selected source.", "Check Display settings on the Capture page and try again.");
        // gfxcapture ends the stream normally when its window or display goes away; FRAME never asked it to stop.
        if (s.ExitCode == 0)
            return Make(FaultKind.Source, "The captured window or display was closed or is no longer available.", "Reopen the window or reconnect the display, or choose another capture source.");
        if (new[] { "nvenc", "openencodesession", "amf", "qsv", "error while opening encoder", "encoder", "cuda", "mfx" }.Any(text.Contains))
            return Make(FaultKind.Encoder, "Video encoding stopped unexpectedly.", "Restart Shadow Recording. If it happens again, choose another encoder in Settings › Advanced.");
        if (new[] { "monitor", "hwnd", "window", "graphicscaptureitem", "capture item", "display", "closed", "device removed", "dxgi" }.Any(text.Contains))
            return Make(FaultKind.Source, "The selected display or window is no longer available.", "Reconnect the display or choose another capture source.");
        return Make(FaultKind.Engine, s.ExitCode is int code ? $"The capture engine exited unexpectedly (code {code})." : "The capture engine stopped unexpectedly.", "Try restarting Shadow Recording. Technical details are available below.");
    }
}
