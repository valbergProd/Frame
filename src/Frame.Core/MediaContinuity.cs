namespace Frame.Core;

// Continuity and sync rules for saved clips (used by FRAME's media tests). Audio is AAC at 48 kHz: packets follow each
// other every 1024 samples (21.333 ms); Matroska's 1 ms timestamps make single steps vary by about ±1 ms.
public sealed record AudioContinuity(int Packets, int Holes, int Overlaps, int Duplicates, double LongestStepMs, double ShortestStepMs, IReadOnlyList<(double At, double StepMs)> Irregular);

public static class MediaContinuity
{
    public const double AacFrame = 1024 / 48000d;
    const double Jitter = .002;    // timestamp rounding tolerance
    const double Duplicate = .0005; // a step this short means the same audio was written twice

    public static AudioContinuity Audio(IReadOnlyList<double> packetTimes)
    {
        var t = packetTimes.Order().ToArray(); var irregular = new List<(double, double)>();
        int holes = 0, overlaps = 0, duplicates = 0; double longest = 0, shortest = double.PositiveInfinity;
        for (int i = 1; i < t.Length; i++)
        {
            double step = t[i] - t[i - 1]; longest = Math.Max(longest, step); shortest = Math.Min(shortest, step);
            if (step > AacFrame + Jitter) holes++;
            else if (step <= Duplicate) duplicates++;
            else if (step < AacFrame - Jitter) overlaps++;
            else continue;
            irregular.Add((t[i - 1], Math.Round(step * 1000, 2)));
        }
        return new(t.Length, holes, overlaps, duplicates, Math.Round(longest * 1000, 2), t.Length > 1 ? Math.Round(shortest * 1000, 2) : 0, irregular);
    }

    // The only tolerated irregularity: one partial overlap (shorter step, not a duplicate) where the encoded exact-start
    // piece meets the first copied segment, i.e. within the first second of the clip. Anything else is a problem:
    // a hole (missing audio), a duplicate, more than one overlap, or an overlap later in the clip.
    public static string? AudioProblem(AudioContinuity a, double startJoinWithin = 1.05, int allowedStartOverlaps = 1)
    {
        if (a.Holes > 0) return $"{a.Holes} audio hole(s), longest step {a.LongestStepMs} ms";
        if (a.Duplicates > 0) return $"{a.Duplicates} duplicated audio packet(s)";
        var late = a.Irregular.Where(x => x.StepMs < AacFrame * 1000 && x.At > startJoinWithin).ToArray();
        if (late.Length > 0) return $"audio overlap at {late[0].At:0.000} s (only the start join may overlap)";
        if (a.Overlaps > allowedStartOverlaps) return $"{a.Overlaps} audio overlaps at the start (at most {allowedStartOverlaps})";
        return null;
    }

    // A/V drift as the least-squares trend of the measured offsets over the clip (single events jitter by about one
    // captured frame, so last-minus-first is not a drift measure). Returns the change across the whole series, seconds.
    public static double Trend(IReadOnlyList<double> offsets)
    {
        if (offsets.Count < 3) return 0;
        double mx = (offsets.Count - 1) / 2d, my = offsets.Average(), sxy = 0, sxx = 0;
        for (int i = 0; i < offsets.Count; i++) { sxy += (i - mx) * (offsets[i] - my); sxx += (i - mx) * (i - mx); }
        return sxy / sxx * (offsets.Count - 1);
    }
}
