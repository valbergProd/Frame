namespace Frame.App;

public static partial class MediaExport
{
    // Freeze all sources before stopping capture or encoding any one source. A stalled gfxcapture shutdown cannot
    // destroy the only usable frame, and another display's pending PCM cannot age out during a long export.
    public static async Task<Func<Task<string>>> PrepareRecording(string exe, Settings settings, CapturePipeline p, IReadOnlyList<TimeRange> ranges, string label)
    {
        if (ranges.Count == 0) throw new IOException("No recording intervals were selected.");
        double end = ranges[^1].End;
        if (p.Segments.LastOrDefault()?.End >= end)
            return () => Export(exe, settings, p, ranges, label);

        var segments = p.Segments.Where(s => s.End > ranges[0].Start && s.Start < end).OrderBy(s => s.Start).ToList();
        if (segments.Count == 0 && p.Segments.Count > 0) segments.Add(p.Segments[^1]); // a recording wholly in the still tail
        double baseStart = segments.Count > 0 ? segments[0].Start : 0;
        if (segments.Count > 0 && ranges[0].Start < segments[^1].End)
            ReplayLogic.RequireCoverage(segments, new TimeRange(Math.Max(baseStart, ranges[0].Start), Math.Min(end, segments[^1].End)));
        var dir = Path.Combine(p.Folder, "manual-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var stillTask = p.CaptureStillAsync(Path.Combine(dir, "still.png"));
        var audioTask = p.Audio?.SnapshotAsync(Path.Combine(dir, "pending-audio.f32")) ?? Task.FromResult<AudioPipe.Snapshot?>(null);
        var (lines, shift) = await ExactLines(exe, segments, dir); int closedLines = lines.Count;
        if (p.OpenSegment() is { } open)
        {
            DiagnosticLog.Write("manual-snapshot", $"{p.Label}: {segments.Count} closed segments, start {baseStart:0.000}, end {end:0.000}, open {Path.GetFileName(open)}");
            string copy = Path.Combine(dir, "open.mkv");
            await using var from = new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var to = File.Create(copy); await from.CopyToAsync(to);
            if (to.Length > 0) lines.Add("file 'open.mkv'");
        }
        string? still = await stillTask; var pcm = await audioTask;
        return Finish;

        async Task<string> Finish()
        {
            string? joined = lines.Count > 0 ? await Join(exe, dir, lines, "recorded") : null;
            if (joined == null && closedLines > 0) joined = await Join(exe, dir, lines.Take(closedLines), "closed");
            if (joined != null)
            {
                var packets = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"),
                    ["-v", "error", "-select_streams", "v:0", "-read_intervals", "%+#1", "-show_entries", "packet=pts_time", "-of", "csv=p=0", joined]);
                if (packets.Code != 0 || string.IsNullOrWhiteSpace(packets.Text)) joined = null;
            }
            if (joined == null && (still == null || segments.Count > 0)) throw new IOException("Recorded frames could not be read. Recovery files were retained.");
            if (joined == null) { baseStart = 0; shift = 0; }

            double timelineStart = baseStart + shift, duration = Math.Max(.001, end - timelineStart);
            var args = new List<string> { "-v", "error", "-y" };
            var graph = new List<string>(); int input = 0, recordedIndex = -1;
            if (joined != null) { recordedIndex = input++; args.AddRange(["-i", joined]); }
            if (still != null)
            {
                int imageIndex = input++; args.AddRange(["-i", still]);
                var size = joined == null ? null : await VideoSize(exe, joined);
                string scale = size is { } s ? $"scale={s.W}:{s.H}," : "";
                graph.Add($"[{imageIndex}:v]{scale}setsar=1,format=yuv420p,fps={settings.Fps}[still]");
                if (joined != null)
                {
                    graph.Add($"[{recordedIndex}:v]setsar=1,format=yuv420p[recorded]");
                    graph.Add("[recorded][still]concat=n=2:v=1:a=0[base]");
                }
                else graph.Add("[still]null[base]");
            }
            else graph.Add($"[{recordedIndex}:v]null[base]");
            graph.Add($"[base]tpad=stop_mode=clone:stop_duration={Backend.F(duration + 2)},fps={settings.Fps}[video]");

            if (p.HasAudio)
            {
                bool recordedAudio = false;
                if (joined != null)
                {
                    var probe = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"),
                        ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_type", "-of", "csv=p=0", joined]);
                    recordedAudio = probe.Code == 0 && probe.Text.Trim() == "audio";
                }
                string oldAudio = recordedAudio ? $"[{recordedIndex}:a]" : "anullsrc=r=48000:cl=stereo,";
                if (pcm != null && pcm.Start < end && pcm.End > timelineStart)
                {
                    int pcmIndex = input++; args.AddRange(["-f", "f32le", "-ar", "48000", "-ch_layout", "stereo", "-i", pcm.Path]);
                    double prefix = Math.Max(0, pcm.Start - timelineStart), skip = Math.Max(0, timelineStart - pcm.Start);
                    graph.Add($"[{pcmIndex}:a]atrim=start={Backend.F(skip)},asetpts=PTS-STARTPTS[recent]");
                    if (prefix > .0001)
                    {
                        graph.Add(oldAudio + $"apad=whole_dur={Backend.F(prefix)},atrim=duration={Backend.F(prefix)},asetpts=PTS-STARTPTS[prefix]");
                        graph.Add("[prefix][recent]concat=n=2:v=0:a=1[audioBase]");
                    }
                    else graph.Add("[recent]anull[audioBase]");
                }
                else graph.Add(oldAudio + "anull[audioBase]");
                graph.Add($"[audioBase]apad=whole_dur={Backend.F(duration + 1)}[audio]");
            }

            graph.Add($"[video]split={ranges.Count}" + string.Concat(Enumerable.Range(0, ranges.Count).Select(i => $"[vr{i}]")));
            if (p.HasAudio) graph.Add($"[audio]asplit={ranges.Count}" + string.Concat(Enumerable.Range(0, ranges.Count).Select(i => $"[ar{i}]")));
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i]; double offset = Math.Max(0, r.Start - timelineStart), length = r.End - r.Start;
                graph.Add($"[vr{i}]trim=start={Backend.F(offset)}:duration={Backend.F(length)},setpts=PTS-STARTPTS[v{i}]");
                if (p.HasAudio) graph.Add($"[ar{i}]atrim=start={Backend.F(offset)}:duration={Backend.F(length)},asetpts=PTS-STARTPTS[a{i}]");
            }
            graph.Add(string.Concat(Enumerable.Range(0, ranges.Count).Select(i => $"[v{i}]" + (p.HasAudio ? $"[a{i}]" : "")))
                + $"concat=n={ranges.Count}:v=1:a={(p.HasAudio ? 1 : 0)}[v]" + (p.HasAudio ? "[a]" : ""));
            args.AddRange(["-filter_complex", string.Join(';', graph), "-map", "[v]"]);
            if (p.HasAudio) args.AddRange(["-map", "[a]"]);
            double expected = ranges.Sum(r => r.End - r.Start); string chunk = Path.Combine(dir, "recording.mp4");
            AddEncoding(args, settings, p); args.AddRange(["-r", settings.Fps.ToString(), "-t", Backend.F(expected), chunk]);
            var result = await Backend.Run(exe, args, 3600, Background);
            if (result.Code != 0) throw new IOException("Recording export failed. Recovery files retained. " + result.Text);
            string? problem = await FileProblem(exe, chunk, expected, settings.Fps, p.HasAudio, .15);
            if (problem != null) throw new IOException("Recording verification failed: " + problem + ". Recovery files retained.");
            string output = await Finalize(exe, settings, dir, [chunk], label);
            DiagnosticLog.Write("manual-save", $"Saved {expected:0.00} s including a still tail; {ranges.Count} recording intervals; pending audio preserved when available.");
            return output;
        }
    }
}
