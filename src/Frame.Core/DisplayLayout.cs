namespace Frame.Core;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Overlaps(LayoutRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}
public sealed record DisplayArrangement(LayoutRect[] Cards, double Width, double Height);

// Capture-page monitor map: the Windows desktop arrangement (physical pixels, any origin, negative coordinates) as
// clickable cards in DIP. Pure geometry, so the no-overlap / inside-canvas / minimum-size rules are unit-tested.
public static class DisplayLayout
{
    // Cards keep the desktop proportions (one uniform scale that fits width and maxHeight); the canvas height follows the
    // arrangement within [minHeight, maxHeight]. Each axis is mapped monotonically edge by edge (see Axis), so monitors that
    // do not overlap on the desktop never overlap here, also after a too-small monitor is stretched to the minimum card
    // size. Every card is then inset by `inset` on each side: neighbours keep a visible 2 x inset gap.
    public static DisplayArrangement Arrange(IReadOnlyList<Box> monitors, double width, double minHeight, double maxHeight, double minCardWidth = 48, double minCardHeight = 34, double inset = 2)
    {
        if (monitors.Count == 0) return new([], Math.Max(0, width), minHeight);
        var union = Box.Union(monitors);
        double scale = Math.Min(width / union.Width, maxHeight / union.Height);
        var (left, right, totalX) = Axis(monitors.Select(m => m.X).ToArray(), monitors.Select(m => m.Right).ToArray(), scale, minCardWidth + 2 * inset, width);
        var (top, bottom, totalY) = Axis(monitors.Select(m => m.Y).ToArray(), monitors.Select(m => m.Bottom).ToArray(), scale, minCardHeight + 2 * inset, maxHeight);
        double height = Math.Clamp(totalY, minHeight, maxHeight), offsetX = Math.Round((width - totalX) / 2), offsetY = Math.Round((height - totalY) / 2);
        var cards = new LayoutRect[monitors.Count];
        for (int i = 0; i < cards.Length; i++) cards[i] = new(offsetX + left[i] + inset, offsetY + top[i] + inset, Math.Max(0, right[i] - left[i] - 2 * inset), Math.Max(0, bottom[i] - top[i] - 2 * inset));
        return new(cards, width, height);
    }
    // Readable card sizes for the Capture page (DIP, before the 2 DIP inset): Card fits the number (14) above the full
    // resolution (10, e.g. "3840 × 2160"); NarrowCard fits the number above the resolution split over two lines, for
    // arrangements with too many displays side by side for the wide card at the current width.
    public const double CardWidth = 76, CardHeight = 44, NarrowCardWidth = 46, NarrowCardHeight = 58;
    // Compact first (canvas ≤ 132 DIP); if a card would fall below the readable size, allow a taller canvas (≤ 176 DIP),
    // then the narrow card. Same monotone mapping as Arrange, so the Windows arrangement (e.g. a TV above) is kept.
    public static DisplayArrangement ArrangeReadable(IReadOnlyList<Box> monitors, double width)
    {
        static bool Fits(DisplayArrangement a, double w, double h) => a.Cards.All(c => c.Width >= w - 1 && c.Height >= h - 1);
        var compact = Arrange(monitors, width, 88, 132, CardWidth, CardHeight); if (Fits(compact, CardWidth, CardHeight)) return compact;
        var tall = Arrange(monitors, width, 88, 176, CardWidth, CardHeight); if (Fits(tall, CardWidth, CardHeight)) return tall;
        return Arrange(monitors, width, 88, 176, NarrowCardWidth, NarrowCardHeight);
    }
    // One axis: the sorted distinct monitor edges keep their order, so the mapping is monotone. Spans shorter than `min`
    // stretch their intervals; the axis is scaled back into `limit` and the two steps repeat until stable (an impossible
    // request, e.g. more monitors than fit side by side, ends best-effort but still inside the limit and overlap-free).
    // Edges are rounded to whole DIP (still monotone) so outlines stay crisp with layout rounding.
    static (double[] Start, double[] End, double Total) Axis(int[] starts, int[] ends, double scale, double min, double limit)
    {
        var edges = starts.Concat(ends).Distinct().Order().ToArray();
        var length = new double[edges.Length - 1];
        for (int i = 0; i < length.Length; i++) length[i] = (edges[i + 1] - (double)edges[i]) * scale;
        var spans = starts.Select((s, i) => (From: Array.BinarySearch(edges, s), To: Array.BinarySearch(edges, ends[i]))).ToArray();
        for (int pass = 0; pass < 200; pass++)
        {
            bool grew = false;
            foreach (var (from, to) in spans)
            {
                double size = 0; for (int i = from; i < to; i++) size += length[i];
                if (size > 0 && size < min - 1e-6) { grew = true; for (int i = from; i < to; i++) length[i] *= min / size; }
            }
            double sum = length.Sum(); if (sum > limit) for (int i = 0; i < length.Length; i++) length[i] *= limit / sum;
            if (!grew) break;
        }
        var position = new double[edges.Length];
        for (int i = 0; i < length.Length; i++) position[i + 1] = position[i] + length[i];
        for (int i = 0; i < position.Length; i++) position[i] = Math.Min(Math.Floor(limit), Math.Round(position[i]));
        return (spans.Select(s => position[s.From]).ToArray(), spans.Select(s => position[s.To]).ToArray(), position[^1]);
    }
    // Screen-reader name of a monitor card, e.g. "Display 2, 3840 × 2160, primary, selected".
    public static string Describe(int number, Box bounds, bool primary, bool selected) => $"Display {number}, {bounds.Width} × {bounds.Height}{(primary ? ", primary" : "")}, {(selected ? "selected" : "not selected")}";
    // Diagnostics (--render --fake-displays): "x,y,w,h[,P];…" in physical pixels, P = primary. Named like Native.Displays().
    public static CaptureTarget[] Fake(string spec) => spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select((part, i) => {
        var f = part.Split(',', StringSplitOptions.TrimEntries);
        if (f.Length is not (4 or 5) || (f.Length == 5 && !f[4].Equals("P", StringComparison.OrdinalIgnoreCase))) throw new FormatException("Display spec is x,y,width,height[,P]: " + part);
        var n = f.Take(4).Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (n[2] <= 0 || n[3] <= 0) throw new FormatException("Display size must be positive: " + part);
        return new CaptureTarget($@"\\.\FAKEDISPLAY{i + 1}", $"Display {i + 1}{(f.Length == 5 ? " · Primary" : "")}", new Box(n[0], n[1], n[2], n[3]));
    }).ToArray();
}
