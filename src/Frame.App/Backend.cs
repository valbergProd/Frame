using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Frame.App;

public static class Backend
{
    static readonly ConcurrentDictionary<int, Process> children = new();
    static readonly object processLock = new();
    static bool aborting;
    // Shutdown deadline fallback: stop only this FRAME instance's media processes and refuse late continuations.
    internal static void AbortAll()
    {
        lock (processLock)
        {
            aborting = true;
            foreach (var p in children.Values)
                try { if (!p.HasExited) p.Kill(true); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { DiagnosticLog.Warning("shutdown", "Could not end a media process: " + e.Message); }
        }
    }
    // Diagnostic runs only (--ffmpeg-only-settings with --data): look only at the Settings path, to test "FFmpeg missing"
    // on a PC that has FFmpeg installed.
    internal static bool OnlyConfiguredPath { get; set; }
    // Last successful probe (shown in Settings and written to exported diagnostics).
    public static string? EngineVersion { get; private set; }
    public static string? EnginePath { get; private set; }
    public static string Resolve(string configured = "")
    {
        // Order: explicit Settings path, tools\ beside the app, the user's WinGet links, then PATH.
        var candidates = new List<string> { configured };
        if (!OnlyConfiguredPath)
        {
            candidates.AddRange([Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe")]);
            candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(Directory.Exists).Select(p => Path.Combine(p, "ffmpeg.exe")));
        }
        foreach (var p in candidates.Where(File.Exists))
        {
            try { var file = new FileInfo(p); return file.ResolveLinkTarget(true)?.FullName ?? file.FullName; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        throw new FileNotFoundException("FFmpeg 8.1 or newer is required. Install it (winget install Gyan.FFmpeg) or place ffmpeg.exe and ffprobe.exe in the tools folder, then restart FRAME. You can also choose ffmpeg.exe in Settings › Advanced.");
    }
    public static Process Start(string exe, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        lock (processLock)
        {
            if (aborting) throw new OperationCanceledException("FRAME is closing; media work was cancelled. Recovery files are retained.");
            var p = Process.Start(start) ?? throw new IOException("The media engine could not start.");
            ProcessJob.Add(p); // ends with FRAME even if FRAME crashes or is killed (0.6.2)
            int id = p.Id; children[id] = p;
            p.Exited += (_, _) => children.TryRemove(id, out _); p.EnableRaisingEvents = true;
            return p;
        }
    }
    // CPU seconds used by finished Run() processes (diagnostics and export measurements only).
    static long runCpuTicks;
    public static double RunCpuSeconds => TimeSpan.FromTicks(Interlocked.Read(ref runCpuTicks)).TotalSeconds;
    public static async Task<(int Code, string Text)> Run(string exe, IEnumerable<string> args, int seconds = 30, ProcessPriorityClass? priority = null)
    {
        using var p = Start(exe, args);
        // Exports run below normal priority so a game and the capture engine keep precedence.
        if (priority is { } level) try { p.PriorityClass = level; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var stderr = p.StandardError.ReadToEndAsync(); var stdout = p.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!p.HasExited) p.Kill(true); throw new TimeoutException("The media engine did not respond in time."); }
        try { Interlocked.Add(ref runCpuTicks, p.TotalProcessorTime.Ticks); } catch (InvalidOperationException) { }
        return (p.ExitCode, await stdout + await stderr);
    }
    // Video encoding exactly as the capture engine records it (also used to encode a replay's still tail, so the tail
    // can be joined to stream-copied segments).
    public static List<string> VideoArgs(Encoder encoder, Settings settings)
    {
        var args = new List<string> { "-c:v", encoder.Id, "-b:v", settings.BitrateMbps + "M", "-g", settings.Fps.ToString(), "-bf", "0", "-force_key_frames", "expr:gte(t,n_forced*1)" };
        if (encoder.Id == "libx264") args.AddRange(["-preset", "veryfast", "-pix_fmt", "yuv420p"]);
        if (encoder.LowLatency || encoder.Id == "libx264") args.AddRange(LowLatencyArgs(encoder.Id));
        return args;
    }
    public static async Task<List<Encoder>> Probe(string exe)
    {
        // FFmpeg must start, report its version, have ffprobe.exe beside it (used to verify every saved file) and include
        // Windows Graphics Capture (gfxcapture, FFmpeg 8.1 or newer).
        var version = await Run(exe, ["-hide_banner", "-version"]);
        string first = version.Text.Split('\n', 2)[0].Trim();
        if (version.Code != 0 || !first.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("The selected file did not respond like FFmpeg. Choose ffmpeg.exe from an FFmpeg 8.1 or newer build.");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "ffprobe.exe"))) throw new FileNotFoundException("ffprobe.exe was not found next to ffmpeg.exe. FRAME needs both files from the same FFmpeg build (8.1 or newer).");
        var filter = await Run(exe, ["-hide_banner", "-h", "filter=gfxcapture"]);
        if (!filter.Text.Contains("hmonitor")) throw new NotSupportedException($"This FFmpeg build ({first}) lacks Windows Graphics Capture. FRAME needs FFmpeg 8.1 or newer.");
        EngineVersion = first; EnginePath = exe;
        var list = new List<Encoder>();
        foreach (var vendor in new[] { ("nvenc", "NVIDIA NVENC", true), ("amf", "AMD AMF", true), ("qsv", "Intel Quick Sync", false) })
        {
            foreach (var codec in new[] { "h264", "hevc", "av1" })
            {
                string id = codec + "_" + vendor.Item1;
                try {
                    // Prefer the no-delay configuration; an encoder that rejects it is still offered without it.
                    var fast = await Run(exe, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=size=256x256:rate=30", "-frames:v", "2", "-c:v", id, .. LowLatencyArgs(id), "-f", "null", "-"], 8);
                    var result = fast.Code == 0 ? fast : await Run(exe, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=size=256x256:rate=30", "-frames:v", "2", "-c:v", id, "-f", "null", "-"], 8);
                    if (result.Code == 0) list.Add(new(id, vendor.Item2 + " · " + codec.ToUpperInvariant(), true, vendor.Item3, fast.Code == 0));
                } catch (TimeoutException) { /* An encoder must finish a real probe to be offered. */ }
            }
        }
        list.Add(new("libx264", "CPU · H.264 (higher overhead)", false, false, true));
        return list;
    }
    // Encoders normally hold several frames before output. On a still screen no further frames arrive, so held frames
    // (the first frame at start, the final still image) would never be written. These options emit each frame at once.
    public static string[] LowLatencyArgs(string encoderId) =>
        encoderId.EndsWith("_nvenc") ? ["-delay", "0", "-zerolatency", "1"]
        : encoderId.EndsWith("_qsv") ? ["-async_depth", "1"]
        : encoderId.EndsWith("_amf") ? ["-usage", "lowlatency"]
        : encoderId == "libx264" ? ["-tune", "zerolatency"] : [];
    public static string F(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);
    public static async Task Verify(string ffmpeg, string file)
    {
        if (!File.Exists(file) || new FileInfo(file).Length < 256) throw new IOException("The exported file is empty.");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var r = await Run(probe, ["-v", "error", "-show_entries", "stream=codec_type,width,height:format=duration", "-of", "json", file]);
        if (r.Code != 0) throw new IOException("File verification failed: " + r.Text);
        using var doc = JsonDocument.Parse(r.Text);
        if (!doc.RootElement.GetProperty("streams").EnumerateArray().Any(s => s.GetProperty("codec_type").GetString() == "video")) throw new IOException("No video stream was saved.");
        if (!double.TryParse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString(), CultureInfo.InvariantCulture, out var duration) || duration <= 0) throw new IOException("The saved video has no duration.");
        var decode = await Run(ffmpeg, ["-v", "error", "-i", file, "-frames:v", "1", "-f", "null", "-"]);
        if (decode.Code != 0) throw new IOException("The first video frame could not be decoded.");
    }
}

public sealed class CapturePipeline : IAsyncDisposable
{
    public string Folder { get; }
    public string Label { get; }
    public Encoder Encoder => encoder;
    public List<Segment> Segments { get; } = [];
    public double Position => Volatile.Read(ref position);
    public long Frames => Interlocked.Read(ref frames);
    public double IdleProgressSeconds => (Environment.TickCount64 - Interlocked.Read(ref lastFrameAt)) / 1000d;
    // Media time "now": gfxcapture timestamps follow the wall clock (measured), but frames arrive only when the screen
    // changes, so ffmpeg's progress stalls on a still screen. Extend from the last delivered frame with elapsed time.
    public double LiveEdge => Frames == 0 ? 0 : Volatile.Read(ref lastFramePosition) + IdleProgressSeconds;
    public bool HasAudio => HasAudioOverride ?? audio != null;
    internal bool? HasAudioOverride { get; set; } // media tests: synthetic segments carry audio without a live audio pipe
    public bool StorageFailed => storageFailed;
    internal AudioPipe? Audio => audio; // diagnostics
    // Audio source problems no longer stop recording (AudioPipe degrades to silence and recovers); only a broken audio
    // pipeline is a fault, and that is an engine-side problem, not a lost device.
    public bool AudioDeviceFailed => false;
    public AudioSourceState[] AudioSources => audio?.Sources ?? [];
    public int AudioSourcesEnabled => audio?.EnabledSources ?? 0;
    public event Action? AudioChanged;
    // FFmpeg must keep consuming audio once the audio is behind the video; see AudioHealth.InputBroken.
    public bool AudioInputBroken => audio != null && AudioHealth.InputBroken(audio.WriteBlockedSeconds, audio.AcceptedSeconds, Frames / (double)Math.Max(1, settings.Fps));
    public int? ExitCode { get { try { return process is { HasExited: true } ? process.ExitCode : null; } catch (InvalidOperationException) { return null; } } }
    // Newest part file not yet listed as closed: the segment ffmpeg is still writing (readable while open; see StartAsync).
    public string? OpenSegment()
    {
        var closed = Segments.Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(Folder, "part-*.mkv").Order(StringComparer.OrdinalIgnoreCase).LastOrDefault(f => !closed.Contains(f) && new FileInfo(f).Length > 0);
    }
    public int? ProcessId => process?.Id;
    public bool Running => process is { HasExited: false };
    public string Error { get { lock (errors) return (audio?.Failure?.Message ?? "") + string.Join(Environment.NewLine, errors); } }
    public bool AudioFailed
    {
        get
        {
            if (storageFailed || audio?.Failure != null) return true;
            if (!AudioInputBroken) return false;
            lock (errors) if (!errors.Contains(BrokenAudioInput)) errors.Enqueue(BrokenAudioInput);
            return true;
        }
    }
    const string BrokenAudioInput = "The capture engine stopped reading the audio input while video continued (audio input stalled).";
    readonly string exe;
    readonly Settings settings;
    readonly Encoder encoder;
    readonly CaptureTarget[] targets;
    readonly Queue<string> errors = new();
    long csvOffset;
    string csvPending = "";
    Process? process;
    AudioPipe? audio;
    Task? drain;
    double position;
    long frames;
    long lastFrameAt = Environment.TickCount64;
    double lastFramePosition;
    long framesAtBlock;
    bool storageFailed;
    long lastDiskCheck;
    public CapturePipeline(string exe, Settings settings, Encoder encoder, CaptureTarget[] targets, string root, string label)
    {
        this.exe = exe; this.settings = settings; this.encoder = encoder; this.targets = targets;
        Label = label; Folder = Path.Combine(root, label); Directory.CreateDirectory(Folder);
    }
    // A/V timeline origin (0.5.4 sync fix). Before: audio time 0 = the moment FFmpeg opened the audio pipe, video time 0 =
    // the first captured frame (setpts=PTS-STARTPTS; gfxcapture's own timestamps also start at 0 on its first frame).
    // Windows Graphics Capture delivers that first frame ~0.3–0.6 s after FFmpeg starts, so every sound was recorded that
    // much late, for the whole session (measured 0.61 s). Now both count from one instant taken just before FFmpeg
    // starts: the audio pipe produces timed silence from that instant (AudioPipe origin), and video frames are placed at
    // (wall clock of the first frame − origin) + gfxcapture's own frame deltas. Both clocks are read from the system,
    // nothing is a fixed offset. -fps_mode cfr fills the start of the timeline by holding the first frame.
    public sealed record TimelineOrigin(long Timestamp, long UnixMicroseconds)
    {
        public static TimelineOrigin Now() { long qpc = Stopwatch.GetTimestamp(); return new(qpc, (DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10); }
    }
    internal TimelineOrigin? Origin { get; private set; }
    public double? AudioConnectedAfter => audio?.ConnectedAfterSeconds; // diagnostics: origin -> FFmpeg opened the audio pipe
    public double? VideoStartedAfter { get; private set; }              // diagnostics: origin -> first video frame (held frames / fps)
    string Graph(bool download = false, TimelineOrigin? origin = null)
    {
        var parts = new List<string>();
        for (int i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            var g = $"gfxcapture={(t.WindowHandle != 0 ? "hwnd=" + t.WindowHandle : "hmonitor=" + t.MonitorHandle)}:max_framerate={settings.Fps}:capture_cursor={(settings.Cursor ? 1 : 0)}:width=-2:height=-2:resize_mode=scale_aspect";
            if (t.Crop is Box c) g += $":crop_left={c.X - t.Bounds.X}:crop_top={c.Y - t.Bounds.Y}:crop_right={t.Bounds.Right - c.Right}:crop_bottom={t.Bounds.Bottom - c.Bottom}";
            if (targets.Length == 1 && settings.OutputHeight > 0)
            {
                var b = t.Crop ?? t.Bounds; int h = Math.Min(settings.OutputHeight, b.Height) / 2 * 2; int w = (int)((double)b.Width / b.Height * h) / 2 * 2;
                g = g.Replace("width=-2:height=-2", $"width={w}:height={h}");
            }
            // Single source: constant rate is made at the output (-fps_mode cfr) so each delivered frame is encoded at once.
            // The fps filter holds the newest frame until the next arrives, which on a still screen meant no first frame
            // (start failed) and a missing final still image. Combined layouts still need a common rate before xstack.
            // RTCTIME (FFmpeg's wall clock, µs) is read once at the first frame and kept in register 0 (st/ld), so the
            // capture's own precise frame spacing is preserved; only the start is anchored to the origin.
            string start = origin == null ? "setpts=PTS-STARTPTS"
                : $"setpts='PTS-STARTPTS+if(eq(N,0),st(0,max(0,(RTCTIME-{origin.UnixMicroseconds})/1000000/TB)),ld(0))'";
            // Combined layouts: the fps filter must also start at 0 (holding the first frame), like -fps_mode cfr does.
            g += targets.Length > 1 && origin != null ? $",{start},fps={settings.Fps}:start_time=0" : "," + start;
            if (!encoder.DirectGpu || targets.Length > 1 || download) g += ",hwdownload,format=bgra,format=nv12";
            g += $"[v{i}]"; parts.Add(g);
        }
        if (targets.Length > 1)
        {
            var union = Box.Union(targets.Select(t => t.Bounds));
            string layout = string.Join('|', targets.Select(t => $"{t.Bounds.X - union.X}_{t.Bounds.Y - union.Y}"));
            string graph = string.Concat(Enumerable.Range(0, targets.Length).Select(i => $"[v{i}]")) + $"xstack=inputs={targets.Length}:layout={layout}:fill=black";
            if (settings.OutputHeight > 0) graph += $",scale=-2:{settings.OutputHeight}";
            graph += ",format=nv12[v]"; parts.Add(graph);
        }
        else parts.Add("[v0]null[v]");
        return string.Join(';', parts);
    }
    // One fresh frame of the (unchanged) source with the same size/crop as the recording, for a still-screen save.
    // The muxer keeps its newest cluster in memory, so the final still image is not yet on disk.
    public async Task<string?> CaptureStillAsync(string png)
    {
        try
        {
            var r = await Backend.Run(exe, ["-hide_banner", "-loglevel", "error", "-y", "-filter_complex", Graph(download: true), "-map", "[v]", "-frames:v", "1", "-update", "1", png], 10);
            return r.Code == 0 && File.Exists(png) && new FileInfo(png).Length > 0 ? png : null;
        }
        catch (Exception e) when (e is TimeoutException or IOException or System.ComponentModel.Win32Exception) { return null; }
    }
    public async Task StartAsync()
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-stats_period", "0.25", "-progress", "pipe:1" };
        var origin = Origin = TimelineOrigin.Now();
        if (settings.DesktopAudio || settings.Microphone)
        {
            audio = new AudioPipe(settings, origin.Timestamp); audio.StatusChanged += () => AudioChanged?.Invoke();
            // -ch_layout stereo (not -ac 2): same stream, without FFmpeg's "Guessed Channel Layout" warning in every report.
            args.AddRange(["-thread_queue_size", "128", "-analyzeduration", "0", "-probesize", "32", "-f", "f32le", "-ar", "48000", "-ch_layout", "stereo", "-i", audio.PipePath]);
        }
        args.AddRange(["-filter_complex", Graph(origin: origin), "-map", "[v]"]);
        if (targets.Length == 1) args.AddRange(["-fps_mode", "cfr", "-r", settings.Fps.ToString()]);
        if (audio != null) args.AddRange(["-map", "0:a", "-c:a", "aac", "-b:a", "192k", "-af", "aresample=async=1:first_pts=0"]);
        args.AddRange(Backend.VideoArgs(encoder, settings));
        // With audio: after a still screen FFmpeg fills the gap with duplicated video frames in one burst while the audio
        // for that time follows from the AudioPipe spool. The muxer must wait for that audio (default: only 10 s), or the
        // late audio lands in later segments with wrong times and replays across the still period break (0.5.4 test B).
        if (audio != null) args.AddRange(["-max_interleave_delta", "0"]);
        args.AddRange(["-f", "segment", "-segment_time", "1", "-segment_format", "matroska", "-segment_format_options", "cluster_time_limit=100:flush_packets=1", "-reset_timestamps", "1", "-segment_list_type", "csv", "-segment_list", Path.Combine(Folder, "segments.csv"), Path.Combine(Folder, "part-%08d.mkv")]);
        await File.WriteAllTextAsync(Path.Combine(Folder, "session.json"), JsonSerializer.Serialize(new { Label, targets, encoder, args }));
        try
        {
            process = Backend.Start(exe, args);
            drain = Task.WhenAll(ReadProgress(process.StandardOutput), ReadErrors(process.StandardError));
            if (audio != null) _ = audio.ConnectAsync();
            var watch = Stopwatch.StartNew();
            while (Frames == 0 && Running && watch.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(100);
            if (Frames == 0 || !Running) throw new IOException("Capture did not produce frames. " + Error);
        }
        catch { await DisposeAsync(); throw; }
    }
    async Task ReadProgress(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is string line)
        {
            var parts = line.Split('=', 2); if (parts.Length != 2) continue;
            if (parts[0] == "out_time_us" && long.TryParse(parts[1], out var us)) Volatile.Write(ref position, Math.Max(0, us / 1e6));
            if (parts[0] == "frame" && long.TryParse(parts[1], out var f) && f > Frames) Interlocked.Exchange(ref frames, f);
            // The first progress block with frames: frames held at the start (dup) = when the first frame arrived.
            if (parts[0] == "dup_frames" && VideoStartedAfter == null && Frames > 0 && long.TryParse(parts[1], out var dup)) { VideoStartedAfter = dup / (double)Math.Max(1, settings.Fps); DiagnosticLog.Write("av-start", $"{Label}: audio pipe opened +{AudioConnectedAfter?.ToString("0.000", CultureInfo.InvariantCulture) ?? "-"} s, first video frame +{VideoStartedAfter.Value.ToString("0.000", CultureInfo.InvariantCulture)} s after the shared origin"); }
            // A progress block ends with "progress="; only blocks with new frames move the last-frame clock.
            if (parts[0] == "progress" && Frames > framesAtBlock) { framesAtBlock = Frames; Volatile.Write(ref lastFramePosition, Position); Interlocked.Exchange(ref lastFrameAt, Environment.TickCount64); }
            // Protect disk even while a long export holds the coordinator's command gate.
            if (Environment.TickCount64 - lastDiskCheck > 1500)
            {
                lastDiskCheck = Environment.TickCount64;
                if (new[] { Folder, settings.OutputDirectory }.Any(path => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace < 512L * 1024 * 1024))
                {
                    storageFailed = true; lock (errors) errors.Enqueue("Low disk space: capture stopped. Recovery files retained.");
                    if (process is { HasExited: false }) { await process.StandardInput.WriteLineAsync("q"); await process.StandardInput.FlushAsync(); }
                }
            }
        }
    }
    async Task ReadErrors(StreamReader reader)
    {
        using var log = new StreamWriter(Path.Combine(Folder, "engine.log"), true) { AutoFlush = true };
        long written = 0;
        while (await reader.ReadLineAsync() is string line) { if (line.Length > 4096) line = line[..4096]; lock (errors) { errors.Enqueue(line); if (errors.Count > 20) errors.Dequeue(); } if (written < 8 * 1024 * 1024) { await log.WriteLineAsync(line); written += line.Length * 2; } }
    }
    public void Refresh()
    {
        var path = Path.Combine(Folder, "segments.csv"); if (!File.Exists(path)) return;
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        f.Position = csvOffset;
        var bytes = new byte[8192]; int read;
        while ((read = f.Read(bytes, 0, bytes.Length)) > 0)
        {
            csvOffset += read; csvPending += System.Text.Encoding.UTF8.GetString(bytes, 0, read);
            int endLine;
            while ((endLine = csvPending.IndexOf('\n')) >= 0)
            {
                string line = csvPending[..endLine].TrimEnd('\r'); csvPending = csvPending[(endLine + 1)..];
                var cols = line.Split(','); if (cols.Length < 3) continue;
                var file = Path.Combine(Folder, Path.GetFileName(cols[0].Trim('"')));
                if (!double.TryParse(cols[1], CultureInfo.InvariantCulture, out var start) || !double.TryParse(cols[2], CultureInfo.InvariantCulture, out var end) || !File.Exists(file)) continue;
                Segments.Add(new(file, start, end, new FileInfo(file).Length));
            }
            if (csvPending.Length > 4096) throw new InvalidDataException("Invalid segment journal.");
        }
    }
    // Bound the complete stop, including stdin and audio cleanup. gfxcapture can receive q yet hang in cleanup
    // until another frame arrives. Manual Stop snapshots its tail before disposing the capture with a 3 s grace.
    public async Task StopAsync(int graceSeconds = 12)
    {
        if (process is not { } stopping) return;
        audio?.Stopping(); // FFmpeg closes the audio input while it finishes; that is not an audio failure
        async Task Finish()
        {
            if (!stopping.HasExited)
            {
                await stopping.StandardInput.WriteLineAsync("q"); await stopping.StandardInput.FlushAsync();
                await stopping.WaitForExitAsync();
            }
            if (audio != null) await audio.DisposeAsync();
            if (drain != null) await drain;
        }
        try { await Finish().WaitAsync(TimeSpan.FromSeconds(graceSeconds)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(true);
            throw new IOException("Capture did not finalize. Recovery segments were retained.");
        }
        Refresh();
        if (process.ExitCode != 0 || storageFailed || audio?.Failure != null) throw new IOException("Capture engine failed. Recovery segments retained. " + Error);
    }
    public async ValueTask DisposeAsync()
    {
        if (process is { HasExited: false }) { try { await StopAsync(3); } catch { if (!process.HasExited) process.Kill(true); } }
        if (audio != null)
            try { await audio.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { DiagnosticLog.Warning("capture-stop", "Audio cleanup timed out after capture stopped."); }
        if (process != null)
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { DiagnosticLog.Warning("capture-stop", "Capture process did not acknowledge termination."); }
        process?.Dispose(); process = null;
    }
}
