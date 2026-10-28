namespace Frame.Core;

public static class HistoryDial
{
    public static int[] Values(int persisted) => new[] { 60, 120, 180, 300, 600, 900, 1800, 3600 }
        .Append(persisted is >= 5 and <= 3600 ? persisted : throw new ArgumentOutOfRangeException(nameof(persisted))).Distinct().Order().ToArray();
    // Discrete steps widen for longer histories; five minutes remains a short ember arc. Last 60 degrees is an end stop.
    public static double Angle(int index, int count) => 300d * Math.Pow(index / (double)(count - 1), 2);
    public static double Unwrap(double previous, double angle) => previous + ((angle - previous + 540) % 360 + 360) % 360 - 180;
    public static int Index(double angle, int count) => (int)Math.Round(Math.Sqrt(Math.Clamp(angle, 0, 300) / 300) * (count - 1), MidpointRounding.AwayFromZero);
    public static string Label(int seconds) => seconds < 60 ? $"{seconds} sec" : $"{seconds / 60d:0.##} min";
    public static TimeRange? Available(IEnumerable<Segment> segments, int requested)
    {
        var a = segments.OrderBy(s => s.Start).ToArray();
        if (a.Length == 0) return null;
        var range = new TimeRange(Math.Max(a[0].Start, a[^1].End - requested), a[^1].End);
        try { ReplayLogic.RequireCoverage(a, range); return range; }
        catch (Exception e) when (e is IOException or ArgumentException) { return null; }
    }
}

public sealed record GameObservation(string? Folder, long Epoch);
public sealed class ReplayGameHistory
{
    readonly List<(double At, GameObservation Game)> samples = [];
    public int Count => samples.Count;
    public void Add(double position, GameObservation game)
    {
        if (!double.IsFinite(position) || position < 0) return;
        if (samples.Count > 0 && position <= samples[^1].At) return;
        samples.Add((position, game));
        // One hour plus bracketing samples; hard cap also bounds stalled/high-rate callers.
        while (samples.Count > 4000 || (samples.Count > 2 && samples[1].At < position - 3605)) samples.RemoveAt(0);
    }
    public string? Classify(TimeRange range)
    {
        if (range.End <= range.Start || samples.Count < 2) return null;
        int first = samples.FindLastIndex(s => s.At <= range.Start);
        int last = samples.FindIndex(s => s.At >= range.End);
        if (first < 0 || last <= first) return null;
        var expected = samples[first].Game;
        if (!GameCatalog.IsFolder(expected.Folder)) return null;
        for (int i = first + 1; i <= last; i++)
            if (samples[i].Game != expected || samples[i].At - samples[i - 1].At > 2.5) return null;
        return expected.Folder;
    }
}

public static class GameCatalog
{
    // Exact executable basenames only. Extend here with confirmed game binaries and canonical folders.
    static readonly Dictionary<string, string> Games = new(StringComparer.OrdinalIgnoreCase) {
        ["FortniteClient-Win64-Shipping.exe"] = "Fortnite", ["cs2.exe"] = "CS",
        ["dota2.exe"] = "Dota 2", ["RocketLeague.exe"] = "Rocket League", ["r5apex.exe"] = "Apex Legends"
    };
    public static IEnumerable<string> Folders => Games.Values.Distinct(StringComparer.OrdinalIgnoreCase);
    public static string? Recognize(string process) => Games.GetValueOrDefault(process);
    public static bool IsFolder(string? folder) => folder != null && Folders.Contains(folder, StringComparer.Ordinal);
    public static bool SafeFolder(string name) => name.Length is > 0 and <= 80 && name == name.Trim() && !name.EndsWith('.')
        && !Path.IsPathRooted(name) && !name.Contains("..") && name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) < 0
        && !name.Any(char.IsControl) && !new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$", "Advanced" }.Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { "COM" + i, "LPT" + i }))
            .Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase);
    public static string? ForSource(CaptureTarget source, long window, Box bounds, string executable)
    {
        string? game = Recognize(executable);
        if (game == null) return null;
        if (source.WindowHandle != 0) return source.WindowHandle == window ? game : null;
        var b = source.Crop ?? source.Bounds;
        long overlap = (long)Math.Max(0, Math.Min(b.Right, bounds.Right) - Math.Max(b.X, bounds.X)) * Math.Max(0, Math.Min(b.Bottom, bounds.Bottom) - Math.Max(b.Y, bounds.Y));
        return overlap >= b.Area * .95 ? game : null;
    }
}

public static class ReplayFolders
{
    const string Marker = ".frame-replays";
    const string Signature = "FRAME replay folder v1";
    static bool Link(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    static string Child(string root, string folder)
    {
        if (!GameCatalog.IsFolder(folder) || !GameCatalog.SafeFolder(folder)) throw new ArgumentException("Unrecognized replay folder.");
        var full = Path.GetFullPath(root); var child = Path.GetFullPath(Path.Combine(full, folder));
        if (!string.Equals(Path.GetDirectoryName(child), Path.TrimEndingDirectorySeparator(full), StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid replay folder.");
        return child;
    }
    // The input is already verified and committed by MediaExport. Failure leaves that root clip intact.
    public static string RouteVerified(string root, string verified, string? folder)
    {
        if (folder == null) return verified;
        string? reserved = null, partial = null;
        try
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(verified)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase)) return verified;
            string dir = Child(root, folder);
            // Never adopt somebody else's nonempty folder. Existing FRAME-marked folders are reused.
            if (Directory.Exists(dir) && !File.Exists(Path.Combine(dir, Marker)) && Directory.EnumerateFileSystemEntries(dir).Any()) return verified;
            Directory.CreateDirectory(dir); if (Link(dir)) return verified;
            string marker = Path.Combine(dir, Marker);
            if (File.Exists(marker)) { if (Link(marker) || new FileInfo(marker).Length > 100 || File.ReadAllText(marker) != Signature) return verified; }
            else { using var m = new FileStream(marker, FileMode.CreateNew, FileAccess.Write); using var writer = new StreamWriter(m); writer.Write(Signature); }
            reserved = ReserveCopy(dir, Path.GetFileName(verified)); partial = reserved + ".partial." + Guid.NewGuid().ToString("N");
            using (var input = new FileStream(verified, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); if (input.Length != output.Length) throw new IOException("Incomplete replay copy."); }
            File.Move(partial, reserved, true); partial = null;
            // If root deletion fails, return the preserved root and remove the extra copy if possible.
            File.Delete(verified); return reserved;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        finally { if (File.Exists(verified)) { TryDelete(partial); TryDelete(reserved); } }
        return verified;
    }
    static string ReserveCopy(string dir, string name)
    {
        for (int i = 0; i < 1000; i++)
        {
            string target = Path.Combine(dir, i == 0 ? name : Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name));
            try { using var f = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); return target; }
            catch (IOException) when (File.Exists(target)) { }
        }
        throw new IOException("Could not reserve a replay filename.");
    }
    static void TryDelete(string? path) { if (path == null) return; try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    // Editor "Save clip" destination: a fixed direct subfolder of the capture folder, created on first save.
    public const string TrimmedFolder = "Trimmed Clips";
    public static string TrimmedClips(string root) => Path.Combine(Path.GetFullPath(root), TrimmedFolder);
    public static IEnumerable<string> LibraryFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root)) yield return path;
        // Trimmed Clips: FRAME's own direct subfolder (not a game folder); regular .mp4 files only, no links.
        string trimmed = TrimmedClips(root); string[] clips = [];
        try { if (Directory.Exists(trimmed) && !Link(trimmed)) clips = Directory.GetFiles(trimmed, "*.mp4"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (string path in clips) { bool regular; try { regular = !Link(path); } catch (IOException) { continue; } if (regular) yield return path; }
        foreach (string folder in GameCatalog.Folders)
        {
            string dir = Child(root, folder); string marker = Path.Combine(dir, Marker); bool owned;
            try { owned = Directory.Exists(dir) && !Link(dir) && File.Exists(marker) && !Link(marker) && new FileInfo(marker).Length <= 100 && File.ReadAllText(marker) == Signature; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (!owned) continue;
            string[] paths;
            try { paths = Directory.GetFiles(dir, "*.mp4"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string path in paths) { bool regular; try { regular = !Link(path); } catch (IOException) { continue; } if (regular) yield return path; }
        }
    }
}
