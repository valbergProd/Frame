using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Frame.App;

public static partial class MediaExport
{
    // Exports never compete with a game or the capture engine for CPU.
    const ProcessPriorityClass Background = ProcessPriorityClass.BelowNormal;

    // One concat entry in file time: [In, Out) of Path. Segments are 0..length (a session's first segment starts later,
    // see Pieces); encoded head/tail pieces skip their AAC priming packet the same way.
    public sealed record Piece(string Path, double In, double Out, bool Explicit = false); // Explicit: always in/out points (encoded or cut)

    // Concat list. Fast form (0.5.4): every entry has explicit inpoint/outpoint and a small probe (no analysis decode),
    // so opening a 1 s segment costs about half as much; explicit points make the timeline independent of the probe
    // (without them a short probe placed some segments 37 ms late and produced non-monotonic DTS warnings). Safe form
    // = the validated 0.5.4 list (duration, inpoint/outpoint only for a shifted start), used if the fast form fails.
    // Every entry carries its exact length from segments.csv: without it the demuxer probes each segment and overshoots by
    // ~30 ms, which put a 61–67 ms frame gap at every boundary and stretched the timeline ~3 % (0.5.4 measurement).
    static List<string> ConcatList(IEnumerable<Piece> pieces, bool fast, string dir)
    {
        var lines = new List<string>();
        foreach (var x in pieces)
        {
            string rel = Path.GetDirectoryName(x.Path) is { } d && string.Equals(Path.GetFullPath(d), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(x.Path) : "../" + Path.GetFileName(x.Path);
            lines.Add("file '" + rel + "'");
            if (fast) { lines.Add("option analyzeduration 0"); lines.Add("option probesize 65536"); lines.Add("inpoint " + Backend.F(x.In)); lines.Add("outpoint " + Backend.F(x.Out)); }
            else if (x.In > 0 || x.Explicit) { lines.Add("inpoint " + Backend.F(x.In)); lines.Add("outpoint " + Backend.F(x.Out)); }
            else lines.Add("duration " + Backend.F(x.Out - x.In));
        }
        return lines;
    }

    // Segments as pieces. Only a session's first segment (start 0) can be shifted: it begins with the AAC encoder's
    // priming packet (-21 ms), so Matroska moved the whole segment +21 ms while segments.csv says 0. Others need no probe.
    static async Task<List<Piece>> Pieces(string exe, IReadOnlyList<Segment> segments)
    {
        var pieces = segments.Select(s => new Piece(s.Path, 0, s.End - s.Start)).ToList();
        if (pieces.Count > 0 && segments[0].Start < .001 && await StartTime(exe, segments[0].Path) is > .0005 and < .5 and var shift) pieces[0] = pieces[0] with { In = shift };
        return pieces;
    }

    // Exact concat lines for the re-encoding paths (safe form). Returns the lines and the shift of the first segment.
    static async Task<(List<string> Lines, double Shift)> ExactLines(string exe, IReadOnlyList<Segment> segments, string dir)
    {
        var pieces = await Pieces(exe, segments);
        return (ConcatList(pieces, false, dir), pieces.Count > 0 ? pieces[0].In : 0);
    }

    // Shadow replay save. Fast path: the closed one-second segments (each starts with a keyframe) are stream-copied:
    // no decoding or encoding, the recorded H.264/AAC stays bit-identical, no GPU encoder use. Only a partial first
    // segment (exact start) and a still-screen tail are encoded, with the recording's own encoder settings. Anything
    // unusual or a failed check falls back to the validated copy form, then to the full re-encode.
    // The exact-start piece can be encoded as soon as the save is requested (the start is known then), while the save
    // still waits for the current segment to close; ExportReplay uses it when the final window starts at the same time.
    public sealed record PreparedHead(string Dir, double Start, Segment First, Task<(Piece?, string?)> Encode);
    public static PreparedHead? PrepareHead(string exe, Settings settings, CapturePipeline p, double start)
    {
        var first = p.Segments.FirstOrDefault(s => s.Start <= start && s.End > start);
        if (first == null || !NeedsHead(settings, first, start - first.Start)) return null;
        var dir = Path.Combine(p.Folder, "export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var next = p.Segments.Where(s => s.Start >= first.End - .05).OrderBy(s => s.Start).FirstOrDefault();
        return new(dir, start, first, Task.Run(async () => await EncodeHead(exe, settings, p, dir, first, start - first.Start, first.Start < .001 && await StartTime(exe, first.Path) is > .0005 and < .5 and var s ? s : 0, next?.Path)));
    }
    public static async Task Discard(PreparedHead? prepared)
    {
        if (prepared == null) return;
        try { await prepared.Encode; } catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException) { }
        try { if (Directory.Exists(prepared.Dir)) Directory.Delete(prepared.Dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    static bool NeedsHead(Settings settings, Segment first, double cut) => cut > .5 / settings.Fps && cut < first.End - first.Start - .5 / settings.Fps;

    public static async Task<string> ExportReplay(string exe, Settings settings, CapturePipeline p, LiveWindow window, string label, PreparedHead? prepared = null)
    {
        var watch = Stopwatch.StartNew(); double cpuBefore = Backend.RunCpuSeconds;
        string? reason;
        try
        {
            var (file, why, stages) = await ExportReplayCopy(exe, settings, p, window, label, prepared);
            if (file != null)
            {
                DiagnosticLog.Write("replay-save", $"{label}: stream copy{(window.NeedsTail ? " + encoded still tail " + Backend.F(Math.Round(window.Tail, 2)) + " s" : "")}, {Backend.F(Math.Round(window.Range.End - window.Range.Start, 2))} s requested, {watch.Elapsed.TotalSeconds:0.00} s, ffmpeg CPU {Backend.RunCpuSeconds - cpuBefore:0.00} s · {stages}");
                return file;
            }
            reason = why + " · " + stages;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { reason = e.Message; }
        finally { await Discard(prepared); } // a prepared piece that was not used (different window, or the fallback ran)
        DiagnosticLog.Warning("replay-save", $"{label}: full re-encode fallback ({reason})");
        return window.NeedsTail ? await ExportLive(exe, settings, p, window, label) : await Export(exe, settings, p, [window.Range], label);
    }

    // Returns (file, null, stage timings) on success or (null, reason, timings) when the full export has to do it.
    static async Task<(string? File, string? Reason, string Stages)> ExportReplayCopy(string exe, Settings settings, CapturePipeline p, LiveWindow window, string label, PreparedHead? prepared)
    {
        var stages = new List<string>(); var clock = Stopwatch.StartNew();
        void Stage(string name) { stages.Add($"{name} {clock.Elapsed.TotalSeconds:0.00}"); clock.Restart(); }
        var r = window.Range;
        // Segments overlapping the window by at least half a frame (a window edge exactly on a boundary adds nothing).
        var body = ReplayLogic.Select(p.Segments, r.Start, r.End).Where(s => s.End - r.Start > .5 / settings.Fps && r.End - s.Start > .5 / settings.Fps).ToList();
        if (body.Count == 0) return (null, "no closed segment yet", "");
        ReplayLogic.RequireCoverage(p.Segments, new TimeRange(body[0].Start, body[^1].End));
        double cut = r.Start - body[0].Start;
        bool wantHead = NeedsHead(settings, body[0], cut);
        bool usePrepared = wantHead && prepared != null && Math.Abs(prepared.Start - r.Start) < 1e-6 && prepared.First.Path == body[0].Path;
        var dir = usePrepared ? prepared!.Dir : Path.Combine(p.Folder, "export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        string? output = null, partial = null;
        try
        {
            var pieces = await Pieces(exe, body);
            double sourceStart = body[0].Start + pieces[0].In;
            // The last segment usually closed after the request: the copy ends at the request (any frame can end a copy).
            if (r.End < body[^1].End - .5 / settings.Fps) pieces[^1] = pieces[^1] with { Out = pieces[^1].In + (r.End - body[^1].Start), Explicit = true };
            // The window usually starts inside its first segment (keyframes are 1 s apart). Only that segment's part from
            // the requested start is encoded (at most 1 s); if that is not possible the copy starts at the keyframe.
            // Head and still tail are prepared at the same time (each a few frames of encoding at most).
            double tailLength = r.End - body[^1].End;
            var headTask = usePrepared ? prepared!.Encode : wantHead ? EncodeHead(exe, settings, p, dir, body[0], cut, pieces[0].In, body.Count > 1 ? body[1].Path : null) : Task.FromResult<(Piece?, string?)>((null, null));
            var tailTask = window.NeedsTail ? EncodeTail(exe, settings, p, dir, body[0].Path, tailLength) : Task.FromResult<(Piece?, string?)>((null, null));
            var (head, headWhy) = await headTask; var (tail, tailWhy) = await tailTask;
            if (head != null) sourceStart = body[0].End - (head.Out - head.In);
            // A window inside one segment: the encoded start piece also ends at the request.
            if (head != null) pieces[0] = body.Count == 1 && pieces[0].Explicit ? head with { Out = Math.Min(head.Out, head.In + (r.End - r.Start)) } : head;
            else if (wantHead) DiagnosticLog.Write("replay-save", "start snapped to the segment keyframe: " + headWhy);
            if (window.NeedsTail) { if (tail == null) return (null, tailWhy, string.Join(", ", stages)); pieces.Add(tail); }
            double expected = pieces.Sum(x => x.Out - x.In);
            // AAC frames depend on the preceding transform. Splicing a newly encoded head into copied AAC can
            // create a ~2.8 dB transient even with perfect packet timestamps. Keep the original AAC stream continuous;
            // MP4 edit-list pre-roll makes an exact start possible without replacing the first audio packets.
            var audio = p.HasAudio ? await ReplayAudio(exe, p, body, dir, sourceStart, window.NeedsTail) : null;
            Stage("prepare");
            // Written beside the final file (same folder: the commit is a rename, never a copy across drives).
            output = Files.Reserve(settings.OutputDirectory, label, "mp4"); partial = output + ".partial.mp4";
            foreach (bool fast in new[] { true, false })
            {
                var list = Path.Combine(dir, fast ? "copy-fast.txt" : "copy.txt"); await File.WriteAllLinesAsync(list, ConcatList(pieces, fast, dir));
                // Fast form: the index is written into reserved space at the start (no second pass over the file, which
                // +faststart needs); any FFmpeg warning counts as failure. Safe form: exactly the 0.5.4 command.
                long frames = (long)(expected * settings.Fps) + 1, packets = (long)(expected * 48000 / 1024) + 1;
                var args = new List<string> { "-v", fast ? "warning" : "error", "-y", "-f", "concat", "-safe", "0", "-i", list };
                if (audio != null) args.AddRange(["-ss", Backend.F(audio.Value.Seek), "-i", audio.Value.Path]);
                args.AddRange(["-map", "0:v:0", "-c:v", "copy"]);
                if (audio != null)
                {
                    args.AddRange(["-map", "1:a:0"]);
                    // A still tail needs padding, so encode its complete audio once from a joined original stream.
                    // Never decode the concat demuxer directly (AAC priming is otherwise repeated at every segment).
                    if (window.NeedsTail) args.AddRange(["-af", $"apad,atrim=duration={Backend.F(expected)}", "-c:a", "aac", "-b:a", "192k"]);
                    else args.AddRange(["-c:a", "copy"]);
                }
                args.AddRange(["-t", Backend.F(expected)]);
                args.AddRange(fast ? ["-moov_size", (65536 + 24 * (frames + packets)).ToString(CultureInfo.InvariantCulture)] : ["-movflags", "+faststart"]);
                args.AddRange(["-f", "mp4", partial]);
                var join = await Backend.Run(exe, args, 3600, Background);
                Stage(fast ? "copy" : "copy-safe");
                string? problem = join.Code != 0 ? "stream copy failed: " + join.Text.Trim()
                    : fast && join.Text.Trim().Length > 0 ? "FFmpeg warned: " + join.Text.Trim().Split('\n')[0]
                    : await FileProblem(exe, partial, expected, settings.Fps, p.HasAudio, fast ? 1.5 / settings.Fps + .005 : .25);
                Stage("check");
                if (problem == null) { File.Move(partial, output, true); return (output, null, string.Join(", ", stages) + (fast ? "" : " (safe copy)")); }
                DiagnosticLog.Warning("replay-save", $"{label}: {(fast ? "fast" : "safe")} copy rejected: {problem}");
                TryDelete(partial);
                if (!fast) return (null, problem, string.Join(", ", stages));
            }
            return (null, "copy failed", string.Join(", ", stages));
        }
        finally
        {
            if (partial != null) TryDelete(partial);
            if (output != null && File.Exists(output) && new FileInfo(output).Length == 0) TryDelete(output);
            try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    static async Task<(string Path, double Seek)?> ReplayAudio(string exe, CapturePipeline p, List<Segment> body, string dir, double sourceStart, bool tail)
    {
        var original = body.ToList();
        var previous = p.Segments.LastOrDefault(s => s.Start < body[0].Start && Math.Abs(s.End - body[0].Start) < .005);
        if (previous != null) original.Insert(0, previous); // AAC decoder pre-roll at a keyframe boundary too
        var (lines, shift) = await ExactLines(exe, original, dir);
        if (tail && File.Exists(Path.Combine(dir, "open.mkv"))) lines.Add("file 'open.mkv'");
        string list = Path.Combine(dir, "audio-original.txt"), joined = Path.Combine(dir, "audio-original.mka");
        await File.WriteAllLinesAsync(list, lines);
        var result = await Backend.Run(exe, ["-v", "error", "-y", "-copyts", "-f", "concat", "-safe", "0", "-i", list,
            "-map", "0:a:0", "-c:a", "copy", "-avoid_negative_ts", "disabled", joined], 3600, Background);
        if (result.Code != 0) throw new IOException("Original replay audio could not be joined: " + result.Text);
        double firstPacket = await AudioStart(exe, joined) ?? throw new IOException("Original replay audio has no readable start.");
        // Input -ss is relative to format.start_time; account for a segment whose first AAC packet follows video.
        return (joined, Math.Max(0, sourceStart - original[0].Start - shift - firstPacket));
    }

    // One ffprobe per file: codec parameters (SPS/PPS hash, size, profile; equal = joinable without re-encoding), size and
    // container start time. Each FFmpeg/ffprobe launch costs a few hundred ms on a busy PC, so they are combined.
    sealed record ProbeInfo(string? Hash, double Start, int Width, int Height, double VideoStart = 0);
    static async Task<ProbeInfo?> Probe(string exe, string file)
    {
        var r = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-select_streams", "v:0", "-show_entries", "format=start_time:stream=codec_name,profile,width,height,pix_fmt,start_time,extradata_hash", "-show_data_hash", "SHA256", "-of", "json", file], 30, Background);
        if (r.Code != 0) return null;
        using var doc = JsonDocument.Parse(r.Text);
        string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.ToString() : null;
        double start = doc.RootElement.TryGetProperty("format", out var f) && double.TryParse(S(f, "start_time"), CultureInfo.InvariantCulture, out var st) ? st : 0;
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0) return new(null, start, 0, 0);
        var v = streams[0]; string? hash = S(v, "extradata_hash") is { } h && h.Contains("SHA256:") ? string.Join('|', S(v, "codec_name"), S(v, "profile"), S(v, "width"), S(v, "height"), S(v, "pix_fmt"), h) : null;
        return new(hash, start, int.TryParse(S(v, "width"), out int w) ? w : 0, int.TryParse(S(v, "height"), out int hh) ? hh : 0, double.TryParse(S(v, "start_time"), CultureInfo.InvariantCulture, out var vs) ? vs : 0);
    }
    // A recording session's codec parameters and size (all its segments share them), probed once per pipeline.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CapturePipeline, Task<ProbeInfo?>> references = new();
    static Task<ProbeInfo?> Reference(string exe, CapturePipeline p, string segment) => references.GetValue(p, _ => Probe(exe, segment));
    // An encoded piece is joinable when its parameters equal the recording's; it starts with its own AAC priming packet,
    // like a session's first segment, which is skipped the same way (inpoint at its real start).
    static async Task<(Piece?, string?)> Joinable(string exe, CapturePipeline p, string reference, string file, double length, string what)
    {
        var a = await Reference(exe, p, reference); var b = await Probe(exe, file);
        if (a?.Hash == null || b?.Hash != a.Hash) return (null, what + " H.264 parameters differ from the recording");
        // Placed by its first video frame; the AAC priming packet (and any audio pre-roll) lies before it and is dropped.
        double lead = b.VideoStart is > 0 and < .5 ? b.VideoStart : 0;
        return (new Piece(file, lead, lead + length, true), null);
    }
    // The first segment from the requested start (accurate seek: decoded from its keyframe, encoded from the cut).
    internal static async Task<(Piece?, string?)> EncodeHead(string exe, Settings settings, CapturePipeline p, string dir, Segment first, double cut, double shift, string? next)
    {
        // The cut is moved to the nearest recorded frame (< half a frame), so the encoded frames continue the recording's
        // frame grid. A segment's frames lie at shift + k/fps in its file (shift > 0 only for a session's first segment,
        // see Pieces); ignoring that shift gave one frame too few and a 30 ms step at the join (measured).
        cut = shift + Math.Max(0, Math.Round((cut - shift) * settings.Fps)) / settings.Fps;
        double length = first.End - first.Start - cut;
        string head = Path.Combine(dir, "head.mkv");
        // Video: frames that start before the segment's end (trim keeps a frame whose end touches the limit; -t dropped it).
        var args = new List<string> { "-v", "error", "-y", "-ss", Backend.F(Math.Max(0, cut - .0005)), "-i", first.Path };
        string graph = $"[0:v]trim=duration={Backend.F(length)},format=nv12[v]";
        if (p.HasAudio)
        {
            // Audio: re-encoded AAC frames have their own grid. It is chosen so the piece's audio ends exactly where the next
            // segment's first (copied) audio packet starts: a whole number of 1024-sample frames, starting a little before
            // the cut (that pre-roll lies before the piece's in-point and is dropped by the concat demuxer).
            double frame = 1024 / 48000d, nextAudio = next != null && await AudioStart(exe, next) is >= 0 and < .2 and var a0 ? a0 : 0;
            int frames = (int)Math.Ceiling((length + nextAudio) / frame - 1e-6);
            double pre = Math.Min(cut, frames * frame - (length + nextAudio));
            args.AddRange(["-ss", Backend.F(cut - pre), "-i", first.Path]);
            graph += $";[1:a]atrim=duration={Backend.F(frames * frame)},asetpts=PTS-STARTPTS-{Backend.F(pre)}/TB[a]";
        }
        args.AddRange(["-filter_complex", graph, "-map", "[v]"]);
        if (p.HasAudio) args.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]);
        args.AddRange(["-fps_mode", "cfr", "-r", settings.Fps.ToString()]);
        args.AddRange(Backend.VideoArgs(p.Encoder, settings));
        args.AddRange(["-f", "matroska", head]);
        var result = await Backend.Run(exe, args, 120, Background);
        if (result.Code != 0) return (null, "head encode failed: " + result.Text.Trim());
        return await Joinable(exe, p, first.Path, head, length, "head");
    }

    // The part after the last closed segment (still screen: no new frames, so the open segment never closed): the open
    // segment's frames and audio, then the true final image (captured fresh), the last frame held to the request time.
    // Encoded like the recording; it is only joined by stream copy when its H.264 parameters equal the segments'.
    static async Task<(Piece?, string?)> EncodeTail(string exe, Settings settings, CapturePipeline p, string dir, string reference, double length)
    {
        if (p.OpenSegment() is not { } open) return (null, "no open segment for the tail");
        // The fresh final image (a separate capture start, ~1 s) and the recording's size are fetched while the open
        // segment is copied.
        var stillTask = p.CaptureStillAsync(Path.Combine(dir, "still.png")); var referenceTask = Reference(exe, p, reference);
        string copy = Path.Combine(dir, "open.mkv");
        try { await using var from = new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); await using var to = File.Create(copy); await from.CopyToAsync(to); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await stillTask; return (null, "open segment unreadable: " + e.Message); }
        string? still = await stillTask;
        if (new FileInfo(copy).Length == 0) return (null, "open segment empty");
        if (await referenceTask is not { Width: > 0, Height: > 0 } info) return (null, "segment size unknown");
        int w = info.Width, h = info.Height;
        string video = still != null
            ? $"[0:v]setsar=1,format=nv12[rec];[1:v]scale={w}:{h},setsar=1,format=nv12,fps={settings.Fps}[img];[rec][img]concat=n=2:v=1:a=0[joined];[joined]"
            : "[0:v]format=nv12,";
        string graph = video + $"tpad=stop_mode=clone:stop_duration={Backend.F(length + 2)},trim=duration={Backend.F(length)},setpts=PTS-STARTPTS[v]";
        if (p.HasAudio) graph += $";[0:a]apad=whole_dur={Backend.F(length + 1)},atrim=end={Backend.F(length)}[a]"; // keeps its real start: the previous segment's last packet ends there
        var args = new List<string> { "-v", "error", "-y", "-i", copy };
        if (still != null) args.AddRange(["-i", still]);
        args.AddRange(["-filter_complex", graph, "-map", "[v]"]);
        if (p.HasAudio) args.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]);
        args.AddRange(["-fps_mode", "cfr", "-r", settings.Fps.ToString()]);
        args.AddRange(Backend.VideoArgs(p.Encoder, settings));
        string tail = Path.Combine(dir, "tail.mkv");
        args.AddRange(["-t", Backend.F(length), "-f", "matroska", tail]);
        var result = await Backend.Run(exe, args, 600, Background);
        if (result.Code != 0) return (null, "tail encode failed: " + result.Text.Trim());
        return await Joinable(exe, p, reference, tail, length, "tail");
    }

    static async Task<double?> AudioStart(string exe, string file)
    {
        var r = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=start_time", "-of", "csv=p=0", file], 30, Background);
        return r.Code == 0 && double.TryParse(r.Text.Trim().TrimEnd(','), CultureInfo.InvariantCulture, out var s) ? s : null;
    }
    static async Task<double?> StartTime(string exe, string file)
    {
        var r = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "format=start_time", "-of", "csv=p=0", file]);
        return r.Code == 0 && double.TryParse(r.Text.Trim(), CultureInfo.InvariantCulture, out var s) ? s : null;
    }
    static async Task<(int W, int H)?> VideoSize(string exe, string file)
    {
        var probe = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", file]);
        var wh = probe.Text.Trim().Split(',');
        return probe.Code == 0 && wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h) ? (w, h) : null;
    }
    // Checks of the finished file in one ffprobe: from its index, duration as planned, audio as long as video and the
    // frame count matching the constant frame rate (rules out gaps or a stretched timeline); and the first video frame
    // actually decodes (only the first 0.2 s are read).
    static async Task<string?> FileProblem(string exe, string file, double expected, int fps, bool audio, double tolerance)
    {
        var r = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-read_intervals", "%+0.2", "-show_entries", "stream=codec_type,duration,nb_frames:frame=media_type", "-of", "json", file], 60, Background);
        if (r.Code != 0) return "probe failed: " + r.Text.Trim();
        int open = r.Text.IndexOf('{'), close = r.Text.LastIndexOf('}'); if (open < 0 || close < open) return "probe gave no result";
        using var doc = JsonDocument.Parse(r.Text[open..(close + 1)]);
        if (!doc.RootElement.TryGetProperty("frames", out var decoded) || !decoded.EnumerateArray().Any(x => x.TryGetProperty("media_type", out var m) && m.GetString() == "video"))
        {
            // Copied AAC pre-roll can precede time zero by more than the probe's 0.2 s packet interval. It is hidden
            // by the MP4 edit list, but ffprobe counts it toward that interval before reaching the first video frame.
            var first = await Backend.Run(exe, ["-v", "error", "-i", file, "-map", "0:v:0", "-frames:v", "1", "-f", "framemd5", "-"], 60, Background);
            if (first.Code != 0 || !first.Text.Split('\n').Any(line => line.StartsWith("0,"))) return "first video frame not decodable";
        }
        double video = 0, sound = 0; long frames = 0;
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            double d = double.TryParse(s.TryGetProperty("duration", out var dv) ? dv.GetString() : null, CultureInfo.InvariantCulture, out var x) ? x : 0;
            if (s.GetProperty("codec_type").GetString() == "video") { video = d; frames = long.TryParse(s.TryGetProperty("nb_frames", out var n) ? n.GetString() : null, out var f) ? f : 0; }
            else if (s.GetProperty("codec_type").GetString() == "audio") sound = d;
        }
        if (Math.Abs(video - expected) > tolerance) return $"video duration {video:0.000} s, expected {expected:0.000} s";
        if (frames > 0 && Math.Abs(frames - video * fps) > Math.Max(3, video * fps * .01)) return $"{frames} frames for {video:0.000} s at {fps} fps (gaps)";
        if (audio && Math.Abs(sound - video) > .15) return $"audio {sound:0.000} s vs video {video:0.000} s";
        return null;
    }

    // Re-encoding paths decode one stream-copied file, never the concat demuxer directly: the AAC decoder loses its first
    // frame at every file switch (measured: one 21 ms hole per one-second segment in the previous full export).
    static async Task<string?> Join(string exe, string dir, IEnumerable<string> lines, string name)
    {
        var list = Path.Combine(dir, name + ".txt"); await File.WriteAllLinesAsync(list, lines);
        var joined = Path.Combine(dir, name + ".mkv");
        var r = await Backend.Run(exe, ["-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", list, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-f", "matroska", joined], 3600, Background);
        return r.Code == 0 && File.Exists(joined) && new FileInfo(joined).Length > 0 ? joined : null;
    }

    // Full re-encode (manual recordings with pauses, and the replay fallback): exact-length concat list, then trim.
    public static async Task<string> Export(string exe, Settings settings, CapturePipeline p, IReadOnlyList<TimeRange> selectedRanges, string label)
    {
        if (selectedRanges.Count == 0) throw new IOException("No recording intervals were selected.");
        foreach (var range in selectedRanges) ReplayLogic.RequireCoverage(p.Segments, range);
        var dir = Path.Combine(p.Folder, "export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var chunks = new List<string>();
        foreach (var r in selectedRanges)
        {
            var selection = ReplayLogic.RequireCoverage(p.Segments, r);
            var (lines, shift) = await ExactLines(exe, selection, dir);
            var joined = await Join(exe, dir, lines, "joined-" + chunks.Count) ?? throw new IOException("Export failed: the recorded segments could not be joined. Original segments retained.");
            var chunk = Path.Combine(dir, "trim-" + chunks.Count + ".mp4");
            var args = new List<string> { "-v", "error", "-y", "-i", joined, "-ss", Backend.F(Math.Max(0, r.Start - selection[0].Start - shift)), "-t", Backend.F(Math.Min(r.End, selection[^1].End) - r.Start), "-map", "0:v:0", "-map", "0:a?" };
            AddEncoding(args, settings, p); args.AddRange(["-fps_mode:v", "passthrough", "-enc_time_base:v", "filter", "-avoid_negative_ts", "make_zero", chunk]);
            var result = await Backend.Run(exe, args, 3600, Background);
            if (result.Code != 0) throw new IOException("Export failed. Original segments retained. " + result.Text); chunks.Add(chunk);
        }
        return await Finalize(exe, settings, dir, chunks, label);
    }

    static void AddEncoding(List<string> args, Settings settings, CapturePipeline p)
    {
        args.AddRange(["-c:v", p.Encoder.Id]);
        if (p.Encoder.Hardware) args.AddRange(["-b:v", settings.BitrateMbps + "M"]); else args.AddRange(["-preset", "veryfast", "-crf", "18"]);
        args.AddRange(["-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"]);
    }

    // Shadow save that ends at the save request even when the screen was still: closed segments, plus the segment ffmpeg is
    // still writing (copied; readable because clusters are flushed every 100 ms), with the last frame held (tpad clone) and
    // audio padded (apad) up to the requested end, then cut exactly with trim/atrim. Fallback for the stream-copy path.
    public static async Task<string> ExportLive(string exe, Settings settings, CapturePipeline p, LiveWindow window, string label)
    {
        var all = p.Segments.OrderBy(s => s.Start).ToList();
        var r = window.Range;
        var selection = all.Where(s => s.End > r.Start + .001).ToList();
        if (selection.Count == 0 && all.Count > 0) selection = [all[^1]]; // whole window lies in the still tail: only the newest frame is needed
        // No closed segment yet (save within the first second): everything comes from the open first segment, starting at 0.
        double baseStart = selection.Count > 0 ? selection[0].Start : 0;
        var dir = Path.Combine(p.Folder, "export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var (inputs, shift) = await ExactLines(exe, selection, dir); int closedLines = inputs.Count;
        if (p.OpenSegment() is { } open)
        {
            string copy = Path.Combine(dir, "open.mkv");
            try { await using var from = new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); await using var to = File.Create(copy); await from.CopyToAsync(to); if (to.Length > 0) inputs.Add("file 'open.mkv'"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* closed in the meantime: the held last frame covers it */ }
        }
        if (inputs.Count == 0) throw new IOException("Shadow Recording is just starting and nothing is on disk yet. Try again in a moment.");
        string firstFile = selection.Count > 0 ? selection[0].Path : Path.Combine(dir, "open.mkv");
        double offset = Math.Max(0, r.Start - baseStart - shift), length = r.End - r.Start;
        // The true final image: captured fresh from the unchanged source, appended after the recorded frames.
        string? still = await p.CaptureStillAsync(Path.Combine(dir, "still.png"));
        (int W, int H)? size = null;
        if (still != null) { size = await VideoSize(exe, firstFile); if (size == null) still = null; }
        string video = still != null && size is { } s
            ? $"[0:v]setsar=1,format=yuv420p[rec];[1:v]scale={s.W}:{s.H},setsar=1,format=yuv420p,fps={settings.Fps}[img];[rec][img]concat=n=2:v=1:a=0[joined];[joined]"
            : "[0:v]";
        string Graph() => video + $"tpad=stop_mode=clone:stop_duration={Backend.F(offset + length + 2)},trim=start={Backend.F(offset)}:duration={Backend.F(length)},setpts=PTS-STARTPTS[v]"
            + (p.HasAudio ? $";[0:a]apad=whole_dur={Backend.F(offset + length + 1)},atrim=start={Backend.F(offset)}:duration={Backend.F(length)},asetpts=PTS-STARTPTS[a]" : "");
        async Task<(int Code, string Text)> Encode(List<string> lines)
        {
            if (await Join(exe, dir, lines, "live-in") is not { } joined) return (1, "the recorded segments could not be joined");
            var args = new List<string> { "-v", "error", "-y", "-i", joined };
            if (still != null) args.AddRange(["-i", still]);
            args.AddRange(["-filter_complex", Graph(), "-map", "[v]"]);
            if (p.HasAudio) args.AddRange(["-map", "[a]"]);
            AddEncoding(args, settings, p); args.AddRange(["-t", Backend.F(length), Path.Combine(dir, "live.mp4")]);
            return await Backend.Run(exe, args, 3600, Background);
        }
        var result = await Encode(inputs);
        // A torn open segment must never cost the save: retry from closed segments only (the last closed frame is held).
        if (result.Code != 0 && selection.Count > 0 && inputs.Count > closedLines) result = await Encode(inputs.Take(closedLines).ToList());
        if (result.Code != 0) throw new IOException("Export failed. Original segments retained. " + result.Text);
        return await Finalize(exe, settings, dir, [Path.Combine(dir, "live.mp4")], label);
    }

    static async Task<string> Finalize(string exe, Settings settings, string dir, List<string> chunks, string label)
    {
        if (chunks.Count == 0) throw new IOException("No finalized segments were available.");
        var finalList = Path.Combine(dir, "final.txt"); await File.WriteAllLinesAsync(finalList, chunks.Select(c => "file '" + Path.GetFileName(c) + "'"));
        var output = Files.Reserve(settings.OutputDirectory, label, "mp4"); var partial = output + ".partial.mp4";
        try
        {
            var join = await Backend.Run(exe, ["-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", finalList, "-c", "copy", "-movflags", "+faststart", partial], 3600, Background);
            if (join.Code != 0) throw new IOException("Finalization failed: " + join.Text);
            await Backend.Verify(exe, partial); File.Move(partial, output, true);
            try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine("Export succeeded; temporary files retained: " + dir); }
            return output;
        }
        catch { try { if (File.Exists(output) && new FileInfo(output).Length == 0) File.Delete(output); } catch (IOException) { } throw; }
    }
}
