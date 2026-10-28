namespace Frame.Core;

public sealed class StateMachine
{
    public RecordingState State { get; private set; }
    public void Move(RecordingState next)
    {
        bool allowed = (State, next) switch {
            (RecordingState.Idle, RecordingState.Starting) => true,
            (RecordingState.Starting, RecordingState.Recording or RecordingState.Idle) => true,
            (RecordingState.Recording, RecordingState.Paused or RecordingState.Finalizing) => true,
            (RecordingState.Paused, RecordingState.Recording or RecordingState.Finalizing) => true,
            (RecordingState.Finalizing, RecordingState.Idle) => true,
            (RecordingState.Faulted, RecordingState.Idle) => true,
            (_, RecordingState.Faulted) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Cannot move from {State} to {next}.");
        State = next;
    }
}
public sealed record Segment(string Path, double Start, double End, long Bytes);
public sealed record TimeRange(double Start, double End);
public static class ReplayLogic
{
    public static IReadOnlyList<Segment> RequireCoverage(IEnumerable<Segment> segments, TimeRange range, double tolerance = .05)
    {
        if (!double.IsFinite(range.Start) || !double.IsFinite(range.End) || range.Start < 0 || range.End <= range.Start)
            throw new ArgumentException("The requested recording interval is invalid.");
        var selected = Select(segments, range.Start, range.End);
        if (selected.Count == 0 || selected[0].Start > range.Start + tolerance || selected[^1].End < range.End - tolerance)
            throw new IOException("Some requested footage is unavailable. Recovery segments have been retained.");
        for (int i = 1; i < selected.Count; i++)
            if (selected[i].Start > selected[i - 1].End + tolerance)
                throw new IOException("A gap was found in the recording. Recovery segments have been retained.");
        return selected;
    }
    public static IReadOnlyList<Segment> Select(IEnumerable<Segment> segments, double from, double until) =>
        segments.Where(s => s.End > from && s.Start < until).OrderBy(s => s.Start).ToArray();
    public static IReadOnlyList<Segment> Trim(IEnumerable<Segment> segments, double seconds, long budget, double? pinnedFrom = null)
    {
        var a = segments.OrderBy(s => s.Start).ToList();
        if (a.Count == 0) return a;
        var cutoff = a[^1].End - seconds; long bytes = a.Sum(s => s.Bytes);
        var remove = new List<Segment>();
        foreach (var s in a)
        {
            if (pinnedFrom is not null && s.End > pinnedFrom) break;
            if (s.End > cutoff && bytes <= budget) break;
            if (s == a[^1]) break;
            bytes -= s.Bytes; remove.Add(s);
        }
        return remove;
    }
}
public sealed class HotkeyGate
{
    readonly Dictionary<string, long> last = new();
    public bool Accept(string action, long milliseconds)
    {
        if (last.TryGetValue(action, out var prior) && milliseconds - prior < 350) return false;
        last[action] = milliseconds; return true;
    }
}
public sealed record Candidate(Box Bounds, string Source, double Confidence, bool Semantic);
public static class CandidateRanking
{
    public static Candidate[] Rank(IEnumerable<Candidate> candidates, int x, int y) => candidates
        .Where(c => c.Bounds.Width >= 24 && c.Bounds.Height >= 24 && c.Bounds.Contains(x, y))
        .GroupBy(c => c.Bounds).Select(g => g.OrderByDescending(c => c.Semantic).ThenByDescending(c => c.Confidence).First())
        .OrderByDescending(c => c.Semantic).ThenByDescending(c => c.Confidence).ThenBy(c => c.Bounds.Area).ToArray();
}
public static class CapturePlan
{
    public static CaptureTarget[][] Groups(IEnumerable<CaptureTarget> targets, bool combined)
    {
        var selected = targets.ToArray();
        if (selected.Length == 0) throw new ArgumentException("Select at least one display or window.");
        if (selected.GroupBy(t => t.Id).Any(g => g.Count() > 1)) throw new ArgumentException("A capture source was selected more than once.");
        foreach (var target in selected)
        {
            var b = target.Bounds;
            if (b.Width < 2 || b.Height < 2) throw new ArgumentException("A capture source has invalid dimensions.");
            if (target.Crop is Box c && (c.Width < 2 || c.Height < 2 || !b.Contains(c.X, c.Y) || !b.Contains(c.Right - 1, c.Bottom - 1)))
                throw new ArgumentException("The capture region must fit inside its display.");
        }
        return combined ? [selected] : selected.Select(t => new[] { t }).ToArray();
    }
}
