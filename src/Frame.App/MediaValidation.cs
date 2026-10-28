using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Frame.App;

public static class MediaValidation
{
    // --export-perf <folder>: FRAME's replay export paths on recorded-style segments (folder\Segments\segments.csv +
    // part-*.mkv, e.g. 1440p60 NVENC 20 Mbps): stream copy (ExportReplay) vs the full re-encode (Export), same window.
    public static async Task<int> ExportPerformance(string root, string ffmpeg, string[]? args = null)
    {
        string exe = Backend.Resolve(ffmpeg), probe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        var settings = new Settings { OutputDirectory = Path.Combine(root, "exports"), FfmpegPath = exe, Fps = 60, BitrateMbps = 20 };
        var p = new CapturePipeline(exe, settings, new("h264_nvenc", "NVIDIA NVENC · H264", true, true, true), [], root, "Segments") { HasAudioOverride = true };
        p.Refresh();
        // --limit N: only the first N segments (quick correctness runs); --copy-only: no full re-encode (no GPU encoding).
        args ??= []; int li = Array.IndexOf(args, "--limit"); bool copyOnly = args.Contains("--copy-only");
        if (li >= 0 && int.TryParse(args[li + 1], out int limit) && limit < p.Segments.Count) p.Segments.RemoveRange(limit, p.Segments.Count - limit);
        var window = new LiveWindow(new TimeRange(p.Segments[0].Start, p.Segments[^1].End), 0);
        async Task<object> Measure(string name, Func<Task<string>> export)
        {
            double cpu = Backend.RunCpuSeconds; var timer = Stopwatch.StartNew(); string file = await export(); double wall = timer.Elapsed.TotalSeconds; cpu = Backend.RunCpuSeconds - cpu;
            async Task<double[]> Times(string s) { var r = await Backend.Run(probe, ["-v", "error", "-select_streams", s, "-show_entries", "packet=pts_time", "-of", "csv=p=0", file], 120); return r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => double.TryParse(x.Trim().TrimEnd(','), CultureInfo.InvariantCulture, out var v) ? v : double.NaN).Where(double.IsFinite).Order().ToArray(); }
            var v = await Times("v:0"); var a = await Times("a:0");
            var vs = v.Zip(v.Skip(1), (x, y) => y - x).ToArray(); var steps = a.Zip(a.Skip(1), (x, y) => y - x).ToArray();
            var info = await Backend.Run(probe, ["-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "csv=p=0", file]);
            return new { name, wallSeconds = Math.Round(wall, 2), ffmpegCpuSeconds = Math.Round(cpu, 2), streams = info.Text.Trim().Replace("\r", "").Split('\n'), videoFrames = v.Length, videoGapsOver25ms = vs.Count(x => x > .025), longestVideoStepMs = Math.Round(vs.Max() * 1000, 1), audioPackets = a.Length, longestAudioStepMs = Math.Round(steps.Max() * 1000, 1), mb = Math.Round(new FileInfo(file).Length / 1048576d, 1) };
        }
        DiagnosticLog.Folder = root;
        var results = new List<object>
        {
            await Measure("stream copy (ExportReplay)", () => MediaExport.ExportReplay(exe, settings, p, window, "Perf copy")),
            await Measure("stream copy, start and end mid-segment (encoded head, cut end)", () => MediaExport.ExportReplay(exe, settings, p, new LiveWindow(new TimeRange(window.Range.End - 11.13, window.Range.End - .37), 0), "Perf copy head")),
            await Measure("stream copy, start in a later segment (encoded head)", () => MediaExport.ExportReplay(exe, settings, p, new LiveWindow(new TimeRange(window.Range.Start + 5.41, window.Range.End), 0), "Perf copy head later")),
        };
        if (!copyOnly) results.Add(await Measure("full re-encode (Export)", () => MediaExport.Export(exe, settings, p, [window.Range], "Perf re-encode")));
        await File.WriteAllTextAsync(Path.Combine(root, "export-perf.json"), JsonSerializer.Serialize(new { segments = p.Segments.Count, seconds = window.Range.End - window.Range.Start, results }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    public static async Task<int> Run(string root, string ffmpeg)
    {
        Directory.CreateDirectory(root); DiagnosticLog.Folder = root;
        var results = new List<object>(); int failures = 0;
        async Task Test(string name, Func<Task<object?>> test)
        {
            try { var detail = await test(); results.Add(new { name, passed = true, detail }); }
            catch (Exception e) { failures++; results.Add(new { name, passed = false, detail = (object)e.ToString() }); }
            await File.WriteAllTextAsync(Path.Combine(root, "media-validation.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        string exe = Backend.Resolve(ffmpeg); var settings = new Settings { OutputDirectory = Path.Combine(root, "exports"), FfmpegPath = exe };
        var p = new CapturePipeline(exe, settings, new("libx264", "CPU", false, false), [], root, "Synthetic");
        await Test("Generate keyframe-aligned video and audio segments", async () => {
            var r = await Backend.Run(exe, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000", "-t", "5", "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-bf", "0", "-force_key_frames", "expr:gte(t,n_forced*1)", "-c:a", "aac", "-f", "segment", "-segment_time", "1", "-reset_timestamps", "1", "-segment_list_type", "csv", "-segment_list", Path.Combine(p.Folder, "segments.csv"), Path.Combine(p.Folder, "part-%08d.mkv")]);
            if (r.Code != 0) throw new Exception(r.Text); p.Refresh(); if (p.Segments.Count < 4) throw new Exception("Expected multiple closed segments"); return new { segments = p.Segments.Count, duration = p.Segments[^1].End };
        });
        async Task<object?> ExportCheck(string label, TimeRange[] ranges, double expected)
        {
            var timer = Stopwatch.StartNew(); string output = await MediaExport.Export(exe, settings, p, ranges, label);
            var info = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "json", output]);
            using var doc = JsonDocument.Parse(info.Text); double duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (Math.Abs(duration - expected) > .25) throw new Exception($"Duration {duration} differs from expected {expected}");
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => s.GetProperty("codec_type").GetString()).ToArray(); if (!streams.Contains("audio") || !streams.Contains("video")) throw new Exception("Missing audio or video");
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]); if (decode.Code != 0) throw new Exception(decode.Text);
            return new { file = Path.GetFileName(output), duration, elapsedSeconds = timer.Elapsed.TotalSeconds, bytes = new FileInfo(output).Length, allFramesDecoded = true };
        }
        await Test("Replay extraction with fractional keyframe boundary", () => ExportCheck("Replay-test", [new(1.35, 3.35)], 2));
        await Test("Pause and resume removes intervening material", () => ExportCheck("Paused-test", [new(.35, 1.25), new(2.1, 3.1)], 1.9));
        await Test("Verified game replay survives routing and full decode, and Library discovers it", async () => {
            string root = await MediaExport.Export(exe, settings, p, [new(.5, 2.5)], "Replay Capture-1");
            string game = ReplayFolders.RouteVerified(settings.OutputDirectory, root, "Fortnite");
            if (game == root || File.Exists(root) || !ReplayFolders.LibraryFiles(settings.OutputDirectory).Contains(game)) throw new IOException("Replay was not routed or indexed.");
            await Backend.Verify(exe, game);
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", game, "-f", "null", "-"]); if (decode.Code != 0) throw new IOException(decode.Text);
            return new { game, allFramesDecoded = true };
        });
        await Test("Game destination failure retains the fully verified root replay", async () => {
            string root = await MediaExport.Export(exe, settings, p, [new(1, 2)], "Replay fallback");
            string block = Path.Combine(settings.OutputDirectory, "CS"); await File.WriteAllTextAsync(block, "synthetic folder blocker");
            string kept = ReplayFolders.RouteVerified(settings.OutputDirectory, root, "CS");
            if (kept != root || !File.Exists(root)) throw new IOException("Root fallback was lost.");
            await Backend.Verify(exe, kept); return new { kept, verified = true };
        });
        await Test("Reject empty video instead of marking saved", async () => { string f = Path.Combine(root, "empty.mp4"); await File.WriteAllTextAsync(f, ""); try { await Backend.Verify(exe, f); } catch (IOException) { return "Empty output rejected"; } throw new Exception("Empty output accepted"); });
        await Test("Missing replay footage rejects export and preserves originals", async () => {
            int count = Directory.GetFiles(settings.OutputDirectory, "*.mp4").Length;
            var removed = p.Segments[2]; p.Segments.RemoveAt(2);
            try {
                try { await MediaExport.Export(exe, settings, p, [new(.5, 4)], "Must-not-save"); }
                catch (IOException) {
                    if (!File.Exists(removed.Path) || Directory.GetFiles(settings.OutputDirectory, "*.mp4").Length != count) throw new Exception("Originals or output directory changed");
                    return "Incomplete interval rejected before encoding; source segment preserved";
                }
                throw new Exception("Missing footage was silently omitted");
            } finally { p.Segments.Insert(2, removed); }
        });
        await Test("Save failure preserves source segments", async () => {
            string blocker = Path.Combine(root, "not-a-directory"); await File.WriteAllTextAsync(blocker, "fixture");
            string valid = settings.OutputDirectory; settings.OutputDirectory = blocker;
            try {
                try { await MediaExport.Export(exe, settings, p, [new(.5, 1.2)], "Must-not-save"); }
                catch (IOException) { if (p.Segments.Any(s => !File.Exists(s.Path))) throw new Exception("A source segment was lost"); return "Unwritable output rejected; all source segments preserved"; }
                throw new Exception("Invalid output folder was accepted");
            } finally { settings.OutputDirectory = valid; }
        });
        // 0.5.3 still-screen saves: closed segments end at 5.02 s, the save request is later (screen unchanged since).
        async Task<object?> LiveCheck(double start, double end, bool openSegment, bool audio)
        {
            p.Refresh(); p.HasAudioOverride = audio;
            string? torn = null;
            if (openSegment) { torn = Path.Combine(p.Folder, "part-99999999.mkv"); var bytes = await File.ReadAllBytesAsync(p.Segments[^1].Path); await File.WriteAllBytesAsync(torn, bytes[..(bytes.Length * 2 / 3)]); }
            try
            {
                var window = ReplayWindow.Live(p.Segments, (int)Math.Ceiling(end - start), end)!;
                if (Math.Abs(window.Range.Start - start) > .02 || !window.NeedsTail) throw new Exception("Unexpected window " + window);
                string output = await MediaExport.ExportLive(exe, settings, p, window, "Live-test");
                var probe = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_type,duration,nb_frames:format=duration", "-of", "json", output]);
                using var doc = JsonDocument.Parse(probe.Text);
                double duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                var types = doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => s.GetProperty("codec_type").GetString()).ToArray();
                var frames = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", output]);
                int decoded = int.Parse(frames.Text.Trim());
                var freeze = await Backend.Run(exe, ["-hide_banner", "-i", output, "-vf", "freezedetect=n=0.001:d=0.5", "-map", "0:v", "-f", "null", "-"], 120);
                var starts = System.Text.RegularExpressions.Regex.Matches(freeze.Text, @"freeze_start: ([0-9.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                double length = end - start, contentEnd = p.Segments[^1].End;
                double openSeconds = 0; if (torn != null) { var op = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "quiet", "-select_streams", "v:0", "-show_entries", "packet=pts_time", "-of", "csv=p=0", torn]); openSeconds = op.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length / 30d; }
                // Frames from the open segment continue the motion; the held frame starts after them.
                double expectedFreeze = Math.Max(0, end - Math.Max(start, contentEnd + openSeconds));
                if (Math.Abs(duration - length) > .1) throw new Exception($"Duration {duration} for {length}");
                if (Math.Abs(decoded - length * 30) > 2) throw new Exception($"Decoded {decoded} frames for {length * 30}");
                if (audio != types.Contains("audio")) throw new Exception("Audio presence mismatch");
                // The held tail must be a real, playable still: one freeze reaching the end, starting where content stops.
                if (expectedFreeze > .6 && (starts.Length == 0 || Math.Abs(starts[^1] - (length - expectedFreeze)) > .6 || freeze.Text.Contains("freeze_end: " + (length - .01).ToString("0", CultureInfo.InvariantCulture) + "x"))) throw new Exception($"Freeze starts {string.Join(",", starts)} expected ~{length - expectedFreeze}");
                var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]); if (decode.Code != 0) throw new Exception(decode.Text);
                return new { file = Path.GetFileName(output), start, end, duration, decoded, freezeStarts = starts, expectedFreezeFrom = length - expectedFreeze, openSegmentSeconds = openSeconds, audio, openSegment };
            }
            finally { p.HasAudioOverride = null; if (torn != null) File.Delete(torn); }
        }
        await Test("Still tail: 3 s of history + 4 s unchanged screen plays through to the request", () => LiveCheck(2, 9, false, false));
        await Test("Still tail with audio padded and a torn open segment", () => LiveCheck(2, 9, true, true));
        await Test("Still tail: window entirely after the last change shows the held frame", () => LiveCheck(12, 18, false, false));
        await Test("Still tail: buffer shorter than requested saves only what exists", () => { p.Refresh(); var w = ReplayWindow.Live(p.Segments, 300, 8)!; if (w.Range.Start != p.Segments[0].Start || Math.Abs(w.Range.End - 8) > 1e-9) throw new Exception(w.ToString()); return Task.FromResult<object?>(w); });
        // Share / Export: real ffmpeg on a synthetic 1280x720 h264/aac recording; originals must stay byte-identical.
        string shareDir = Path.Combine(root, "share"); Directory.CreateDirectory(shareDir); string clip = Path.Combine(shareDir, "FRAME source.mp4");
        string Hash(string f) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));
        await Test("Share: generate source recording", async () => { var r = await Backend.Run(exe, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=60", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000", "-t", "6", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", clip]); if (r.Code != 0) throw new Exception(r.Text); return Hash(clip); });
        string original = File.Exists(clip) ? Hash(clip) : "";
        var shareEncoder = (await Backend.Probe(exe)).FirstOrDefault(e => e.Id.StartsWith("h264_") && e.Hardware) ?? new("libx264", "CPU", false, false);
        var noProgress = new Progress<(ExportStage, double)>(_ => { });
        async Task<object?> ShareCheck(SharePreset preset, Framing framing, int aspectW, int aspectH, int side, int width, int height, bool copy, TimeRange? range = null, Encoder? encoder = null)
        {
            var info = await ShareExportJob.Probe(exe, clip);
            var request = new ExportRequest(clip, info, aspectW, aspectH, side, framing, .3, .5, ExportQuality.Balanced, preset.MaxFps, preset.CopyWhenPossible, Path.Combine(shareDir, "out"), range);
            if (ExportPlanner.Plan(request).StreamCopy != copy) throw new Exception("Unexpected copy decision");
            var timer = Stopwatch.StartNew(); string output = await ShareExportJob.Run(exe, request, encoder ?? shareEncoder, "test " + preset.Id, noProgress, CancellationToken.None);
            var made = await ShareExportJob.Probe(exe, output);
            if (made.Width != width || made.Height != height || !made.HasAudio || made.VideoCodec != "h264" || made.AudioCodec != "aac") throw new Exception($"Got {made}");
            var streams = await Backend.Run(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_type,start_time,duration", "-of", "json", output]);
            using var doc = JsonDocument.Parse(streams.Text); var d = doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => (type: s.GetProperty("codec_type").GetString(), start: double.Parse(s.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture), duration: double.Parse(s.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture))).ToArray();
            double drift = Math.Abs(d.First(x => x.type == "video").start - d.First(x => x.type == "audio").start) + Math.Abs(d.First(x => x.type == "video").duration - d.First(x => x.type == "audio").duration);
            if (drift > .1) throw new Exception("Audio/video start or length differs by " + drift);
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]); if (decode.Code != 0) throw new Exception(decode.Text);
            if (Hash(clip) != original) throw new Exception("Source recording changed");
            return new { file = Path.GetFileName(output), made.Width, made.Height, made.Fps, made.Duration, avDrift = drift, encoder = copy ? "copy" : (encoder ?? shareEncoder).Id, seconds = timer.Elapsed.TotalSeconds };
        }
        await Test("Share: YouTube keeps original by stream copy", () => ShareCheck(SharePresets.YouTube, Framing.Original, 0, 0, 0, 1280, 720, true));
        await Test("Share: Discord keeps original by stream copy", () => ShareCheck(SharePresets.Discord, Framing.Original, 0, 0, 0, 1280, 720, true));
        await Test("Share: Shorts blurred background 1080x1920", () => ShareCheck(SharePresets.Shorts, Framing.Blur, 9, 16, 1080, 1080, 1920, false));
        await Test("Share: TikTok crop 1080x1920", () => ShareCheck(SharePresets.TikTok, Framing.Crop, 9, 16, 1080, 1080, 1920, false));
        await Test("Share: Custom square fit 720 on CPU encoder", () => ShareCheck(SharePresets.Custom, Framing.Fit, 1, 1, 720, 720, 720, false, null, new("libx264", "CPU", false, false)));
        await Test("Share: time range (Phase 3 path) is re-encoded to exact length", async () => { var r = await ShareCheck(SharePresets.YouTube, Framing.Original, 0, 0, 0, 1280, 720, false, new(1.5, 4)); return r; });
        await Test("Share: cancellation removes partial and reserved files", async () => {
            var info = await ShareExportJob.Probe(exe, clip); string outDir = Path.Combine(shareDir, "cancel"); Directory.CreateDirectory(outDir);
            var request = new ExportRequest(clip, info, 9, 16, 2160, Framing.Blur, .5, .5, ExportQuality.High, 0, false, outDir);
            using var cts = new CancellationTokenSource(); bool encoding = false;
            var progress = new Progress<(ExportStage Stage, double)>(p => { if (p.Stage == ExportStage.Encoding && !encoding) { encoding = true; cts.CancelAfter(150); } });
            try { await ShareExportJob.Run(exe, request, new("libx264", "CPU", false, false), "cancel", progress, cts.Token); throw new Exception("Export was not cancelled"); }
            catch (OperationCanceledException) { }
            await Task.Delay(300); var left = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
            if (left.Length != 0 || ShareExportJob.Busy || Hash(clip) != original) throw new Exception("Leftovers: " + string.Join(", ", left)); return "No files left; gate released";
        });
        await Test("Share: missing source and unwritable folder fail clearly", async () => {
            try { await ShareExportJob.Probe(exe, Path.Combine(shareDir, "missing.mp4")); throw new Exception("Missing source accepted"); } catch (FileNotFoundException) { }
            var info = await ShareExportJob.Probe(exe, clip); string blocker = Path.Combine(shareDir, "blocker"); await File.WriteAllTextAsync(blocker, "x");
            try { await ShareExportJob.Run(exe, new ExportRequest(clip, info, 0, 0, 0, Framing.Original, .5, .5, ExportQuality.High, 0, true, Path.Combine(blocker, "sub")), shareEncoder, "x", noProgress, CancellationToken.None); throw new Exception("Unwritable folder accepted"); } catch (IOException) { }
            if (ShareExportJob.Busy || Hash(clip) != original) throw new Exception("State changed"); return "Rejected with IOException; source intact";
        });
        await Test("Share: never overwrites an existing export name", async () => { var a = await ShareCheck(SharePresets.Discord, Framing.Original, 0, 0, 0, 1280, 720, true); var files = Directory.GetFiles(Path.Combine(shareDir, "out"), "*Discord*.mp4"); if (files.Length < 2) throw new Exception("Second export reused a name"); return files.Length; });
        // Phase 3 trimming: real sources at several frame rates (B-frames, 2 s GOP), VFR and a long file. Each cut is checked by
        // decoded frame timestamps against the source's own frames, first-frame PSNR, A/V lengths and an unchanged source hash.
        string trimDir = Path.Combine(root, "trim"); Directory.CreateDirectory(trimDir); string probeExe = Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe");
        async Task<string> Source(string name, string size, string rate, double seconds, string? vf = null)
        {
            string file = Path.Combine(trimDir, name + ".mp4"); double gop = rate.Contains('/') ? 48 : double.Parse(rate, CultureInfo.InvariantCulture) * 2;
            var args = new List<string> { "-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate={rate}", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000", "-t", Backend.F(seconds) };
            if (vf != null) args.AddRange(["-vf", vf, "-fps_mode", "vfr"]);
            args.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-g", Backend.F(gop), "-bf", "2", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", file]);
            var r = await Backend.Run(exe, args, 600); if (r.Code != 0) throw new Exception(r.Text); return file;
        }
        async Task<double[]> FrameTimes(string file, bool decode)
        {
            var r = await Backend.Run(probeExe, decode ? ["-v", "error", "-select_streams", "v:0", "-show_entries", "frame=pts_time", "-of", "csv=p=0", file] : ["-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time", "-of", "csv=p=0", file], 300);
            if (r.Code != 0) throw new Exception(r.Text);
            return r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(l => l.Split(',')[0]).Where(v => v.Length > 0 && v != "N/A").Select(v => double.Parse(v, CultureInfo.InvariantCulture)).Order().ToArray();
        }
        async Task<double> Psnr(string clip, string src, double at)
        {
            var r = await Backend.Run(exe, ["-v", "info", "-hide_banner", "-i", clip, "-ss", Backend.F(at), "-i", src, "-filter_complex", "[0:v]trim=end_frame=1,setpts=PTS-STARTPTS[a];[1:v]trim=end_frame=1,setpts=PTS-STARTPTS[b];[a][b]psnr", "-f", "null", "-"], 120);
            var m = System.Text.RegularExpressions.Regex.Match(r.Text, @"average:(\S+)"); if (!m.Success) throw new Exception("PSNR unavailable: " + r.Text);
            return m.Groups[1].Value == "inf" ? 99 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        async Task<object?> TrimCheck(string src, double start, double end, SharePreset? preset = null)
        {
            string hash = Hash(src); var info = await ShareExportJob.Probe(exe, src); 
            var p = preset ?? SharePresets.YouTube; bool original = preset == null;
            var request = new ExportRequest(src, info, original ? 0 : p.AspectW, original ? 0 : p.AspectH, original ? 0 : p.ShortSide, original ? Framing.Original : p.Framing, .5, .5, ExportQuality.High, original ? 0 : p.MaxFps, original || p.CopyWhenPossible, Path.Combine(trimDir, "out"), new TimeRange(start, end));
            var plan = ExportPlanner.Plan(request); var timer = Stopwatch.StartNew();
            string clip = await ShareExportJob.Run(exe, request, shareEncoder, "trim", noProgress, CancellationToken.None);
            var source = await FrameTimes(src, false); double first = source[0];
            int expected = source.Count(t => t - first >= start - 1e-4 && t - first < end - 1e-4);
            var frames = await FrameTimes(clip, true);
            if (Math.Abs(frames.Length - expected) > 1) throw new Exception($"Expected {expected} frames from the source interval, decoded {frames.Length}");
            var made = await ShareExportJob.Probe(exe, clip);
            var streams = await Backend.Run(probeExe, ["-v", "error", "-show_entries", "stream=codec_type,start_time,duration", "-of", "json", clip]);
            using var doc = JsonDocument.Parse(streams.Text); var d = doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => (type: s.GetProperty("codec_type").GetString(), start: double.Parse(s.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture), duration: double.Parse(s.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture))).ToArray();
            var v = d.First(x => x.type == "video"); var a = d.First(x => x.type == "audio");
            double frame = 1 / info.Fps, drift = Math.Abs(v.start - a.start) + Math.Abs(v.duration - a.duration);
            if (drift > Math.Max(.06, 2 * frame)) throw new Exception("Audio/video differ by " + drift);
            if (Math.Abs(v.duration - (end - start)) > 2 * frame + .01) throw new Exception($"Video length {v.duration} for selection {end - start}");
            double match = original ? await Psnr(clip, src, start) : 99, other = original ? await Psnr(clip, src, Math.Min(info.Duration - frame, start + .5)) : 0;
            if (original && (match < 30 || match <= other + 3)) throw new Exception($"First frame PSNR {match} vs 0.5 s later {other}");
            var decode = await Backend.Run(exe, ["-v", "error", "-xerror", "-i", clip, "-f", "null", "-"], 300); if (decode.Code != 0) throw new Exception(decode.Text);
            if (Hash(src) != hash) throw new Exception("Source recording changed");
            return new { clip = Path.GetFileName(clip), start, end, copy = plan.StreamCopy, expected, decoded = frames.Length, videoLength = v.duration, avDrift = drift, firstFramePsnr = match, laterPsnr = other, made.Width, made.Height, seconds = timer.Elapsed.TotalSeconds };
        }
        string s60 = "", s30 = "", s24 = "", vfr = "", longSource = "";
        await Test("Trim: generate 60/30/23.976 FPS, VFR and 10-minute sources", async () => { s60 = await Source("src60", "1280x720", "60", 12); s30 = await Source("src30", "1280x720", "30", 10); s24 = await Source("src24", "1280x720", "24000/1001", 8); vfr = await Source("srcvfr", "960x540", "60", 8, "select='lt(mod(n\\,5)\\,3)'"); longSource = await Source("src-long", "320x180", "30", 600); return new { vfrFrames = (await FrameTimes(vfr, false)).Length }; });
        await Test("Trim: near the beginning (0 → 1.5 s, exact re-encode)", () => TrimCheck(s60, 0, 1.5));
        await Test("Trim: near the end (10.37 s → end)", () => TrimCheck(s60, 10.37, 12));
        await Test("Trim: across non-keyframe boundaries (3.37 → 7.81 s)", () => TrimCheck(s60, 3.37, 7.81));
        await Test("Trim: very short interval (6 frames)", () => TrimCheck(s60, 5.5, 5.6));
        await Test("Trim: keyframe-aligned start (4 s → end) stays exact", () => TrimCheck(s60, 4, 12));
        await Test("Trim: 30 FPS source (2.2 → 4.9 s)", () => TrimCheck(s30, 2.2, 4.9));
        await Test("Trim: 23.976 FPS source (1.1 → 3.3 s)", () => TrimCheck(s24, 1.1, 3.3));
        await Test("Trim: variable frame rate source (1 → 3 s)", () => TrimCheck(vfr, 1, 3));
        await Test("Trim: 20 s from a 10-minute recording (312.4 → 332.4 s)", () => TrimCheck(longSource, 312.4, 332.4));
        await Test("Trim: selected interval through Shorts preset (3.37 → 5 s)", () => TrimCheck(s60, 3.37, 5, SharePresets.Shorts));
        await Test("Trim: cancellation removes partial clip", async () => {
            var info = await ShareExportJob.Probe(exe, longSource); string outDir = Path.Combine(trimDir, "cancel"); Directory.CreateDirectory(outDir);
            var request = new ExportRequest(longSource, info, 9, 16, 2160, Framing.Blur, .5, .5, ExportQuality.High, 0, false, outDir, new TimeRange(10, 300));
            using var cts = new CancellationTokenSource(); var progress = new Progress<(ExportStage Stage, double)>(p => { if (p.Stage == ExportStage.Encoding) cts.CancelAfter(300); });
            try { await ShareExportJob.Run(exe, request, new("libx264", "CPU", false, false), "cancel", progress, cts.Token); throw new Exception("Not cancelled"); } catch (OperationCanceledException) { }
            await Task.Delay(300); var left = Directory.GetFiles(outDir); if (left.Length != 0 || ShareExportJob.Busy) throw new Exception("Leftovers"); return "clean";
        });
        await Test("Trim: timeline thumbnails are lazy, cached, cancellable and invalidated by changes", async () => {
            string cacheDir = Path.Combine(trimDir, "thumbs"); var seen = new List<int>(); var timer = Stopwatch.StartNew();
            await ShareExportJob.Thumbnails(exe, longSource, 600, 14, 72, cacheDir, (i, _) => seen.Add(i), CancellationToken.None); double cold = timer.Elapsed.TotalSeconds;
            if (seen.Count != 14 || Directory.GetFiles(cacheDir, "*-tl14-*.jpg").Length != 14) throw new Exception("Expected 14 stills");
            timer.Restart(); seen.Clear(); await ShareExportJob.Thumbnails(exe, longSource, 600, 14, 72, cacheDir, (i, _) => seen.Add(i), CancellationToken.None); double warm = timer.Elapsed.TotalSeconds;
            using var cts = new CancellationTokenSource(); int got = 0; string fresh = Path.Combine(cacheDir, "fresh"); Directory.CreateDirectory(fresh);
            try { await ShareExportJob.Thumbnails(exe, s60, 12, 14, 72, fresh, (_, _) => { if (++got == 2) cts.Cancel(); }, cts.Token); throw new Exception("Not cancelled"); } catch (OperationCanceledException) { }
            if (ShareExportJob.ActiveThumbnailJobs != 0 || got != 2 || Directory.GetFiles(fresh).Length > 5) throw new Exception($"Cancellation left work: active {ShareExportJob.ActiveThumbnailJobs}, got {got}, files {Directory.GetFiles(fresh).Length}");
            string copy = Path.Combine(trimDir, "changed.mp4"); File.Copy(s30, copy); await ShareExportJob.Thumbnails(exe, copy, 10, 4, 72, cacheDir, (_, _) => { }, CancellationToken.None);
            int before = Directory.GetFiles(cacheDir, "*-tl4-*.jpg").Length; File.Copy(s24, copy, true); File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(1));
            await ShareExportJob.Thumbnails(exe, copy, 8, 4, 72, cacheDir, (_, _) => { }, CancellationToken.None);
            if (Directory.GetFiles(cacheDir, "*-tl4-*.jpg").Length != before + 4) throw new Exception("Changed source reused stale stills");
            return new { coldSeconds = cold, warmSeconds = warm, cancelledAfter = got };
        });
        await Test("Hardware encoder initialization probes", async () => (await Backend.Probe(exe)).Select(e => e.Name).ToArray());
        await p.DisposeAsync(); return failures == 0 ? 0 : 1;
    }
}
