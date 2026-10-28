using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Frame.App;

public enum ExportStage { Preparing, Encoding, Finalizing, Completed, Failed, Cancelled }

// One user-requested export. ffmpeg starts only here (never in the background), runs below normal priority,
// writes to a hidden .partial file, is validated, then moved to a reserved name. The source is only read.
public static class ShareExportJob
{
    static readonly SemaphoreSlim Gate = new(1); // one heavy export at a time
    public static bool Busy => Gate.CurrentCount == 0;

    public static async Task<MediaInfo> Probe(string ffmpeg, string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("The recording is no longer available.", file);
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var r = await Backend.Run(probe, ["-v", "error", "-show_entries", "stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate:format=duration", "-of", "json", file]);
        if (r.Code != 0) throw new IOException("This recording could not be read: " + r.Text.Trim());
        using var doc = JsonDocument.Parse(r.Text);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new IOException("This file has no video stream.");
        var audio = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "audio");
        double fps = Rate(video, "avg_frame_rate"); if (fps <= 0) fps = Rate(video, "r_frame_rate"); if (fps <= 0 || fps > 1000) fps = 60;
        double.TryParse(doc.RootElement.GetProperty("format").TryGetProperty("duration", out var d) ? d.GetString() : null, CultureInfo.InvariantCulture, out double duration);
        if (!(duration > 0)) throw new IOException("This recording has no readable duration.");
        return new(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), duration, Math.Round(fps, 3), video.GetProperty("codec_name").GetString() ?? "",
            audio.ValueKind != JsonValueKind.Undefined, audio.ValueKind != JsonValueKind.Undefined ? audio.GetProperty("codec_name").GetString() ?? "" : "");
    }
    static double Rate(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.GetString() is not { } text) return 0;
        var parts = text.Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var a) && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var b) && b > 0 ? a / b : 0;
    }

    // Single still for the framing preview, written to the cache only when the Share window opens.
    public static async Task<string?> PreviewFrame(string ffmpeg, string file, double at, string cacheFolder)
    {
        Directory.CreateDirectory(cacheFolder);
        var info = new FileInfo(file);
        string path = Path.Combine(cacheFolder, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file + info.LastWriteTimeUtc.Ticks + "@" + Backend.F(at))))[..24] + "-share.jpg");
        if (File.Exists(path)) return path;
        var r = await Backend.Run(ffmpeg, ["-v", "error", "-y", "-ss", Backend.F(Math.Max(0, at)), "-i", file, "-frames:v", "1", "-vf", "scale=640:-2", "-q:v", "4", path], 20);
        return r.Code == 0 && File.Exists(path) ? path : null;
    }

    // Timeline thumbnails: one fast keyframe-seek decode per sample (never the whole video), sequential, cancellable,
    // cached by path + size + write time so a changed source invalidates its stills. Active counts running workers for leak tests.
    static int activeThumbnailJobs;
    public static int ActiveThumbnailJobs => Volatile.Read(ref activeThumbnailJobs);
    public static async Task Thumbnails(string ffmpeg, string file, double duration, int count, int height, string cacheFolder, Action<int, string> ready, CancellationToken cancel)
    {
        Interlocked.Increment(ref activeThumbnailJobs);
        try
        {
            Directory.CreateDirectory(cacheFolder); Prune(cacheFolder);
            var info = new FileInfo(file);
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file + info.Length + info.LastWriteTimeUtc.Ticks)))[..20];
            string Still(int i) => Path.Combine(cacheFolder, $"{key}-tl{count}-{i}.jpg");
            for (int first = 0; first < count; first += 5)
            {
                cancel.ThrowIfCancellationRequested();
                var batch = Enumerable.Range(first, Math.Min(5, count - first)).Where(i => !File.Exists(Still(i))).ToArray();
                if (batch.Length > 0)
                {
                    // Keyframe-seeked inputs, one decoded frame each; a single process amortizes ffmpeg start-up.
                    var args = new List<string> { "-v", "error", "-nostdin", "-y" };
                    foreach (int i in batch) args.AddRange(["-ss", Backend.F(duration * (i + .5) / count), "-i", file]);
                    for (int k = 0; k < batch.Length; k++) args.AddRange(["-map", k + ":v:0", "-frames:v", "1", "-vf", $"scale=-2:{height}", "-q:v", "5", Still(batch[k])]);
                    using var p = Backend.Start(ffmpeg, args);
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    using var kill = cancel.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } });
                    _ = p.StandardError.ReadToEndAsync(CancellationToken.None); _ = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    try { await p.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } foreach (int i in batch) TryDelete(Still(i)); if (cancel.IsCancellationRequested) throw; continue; }
                    if (p.ExitCode != 0) foreach (int i in batch) TryDelete(Still(i)); // partial stills are not trusted
                }
                for (int i = first; i < Math.Min(count, first + 5); i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (File.Exists(Still(i))) ready(i, Still(i));
                }
            }
        }
        finally { Interlocked.Decrement(ref activeThumbnailJobs); }
    }
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    // Bounded disk cache: keep the newest 1500 timeline stills.
    static void Prune(string folder)
    {
        try
        {
            var files = new DirectoryInfo(folder).GetFiles("*-tl*.jpg");
            if (files.Length <= 1500) return;
            foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(1200)) TryDelete(f.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Short, user-facing wording; the raw message stays available for tooltips/logs.
    public static string Friendly(Exception e) => e switch
    {
        FileNotFoundException => "The recording is no longer available.",
        OperationCanceledException => "Cancelled.",
        UnauthorizedAccessException => "FRAME cannot write to that folder.",
        TimeoutException => "The media engine did not respond in time.",
        IOException io when io.Message.StartsWith("Not enough free space") => io.Message,
        IOException io when io.Message.Contains("could not be read") || io.Message.Contains("no video stream") || io.Message.Contains("no readable") => "This video could not be read. It may be damaged or unsupported.",
        IOException io when io.Message.StartsWith("Export failed") => "The export could not be completed. The original is unchanged.",
        IOException io when io.Message.StartsWith("Export produced") || io.Message.Contains("duration does not match") || io.Message.Contains("lost its audio") || io.Message.Contains("verification") || io.Message.Contains("first video frame") => "The exported file did not pass verification and was removed.",
        ArgumentException a => a.Message,
        InvalidOperationException i => i.Message,
        _ => "Something went wrong. The original is unchanged."
    };

    // target: an exact file chosen in a Save As dialog (replace = the user confirmed replacing it); otherwise a new unique
    // name is reserved in request.OutputFolder. lowImpact (capture running): Idle priority and at most two threads.
    public static async Task<string> Run(string ffmpeg, ExportRequest request, Encoder encoder, string label, IProgress<(ExportStage Stage, double Fraction)> progress, CancellationToken cancel, string? target = null, bool replace = false, bool lowImpact = false)
    {
        if (!await Gate.WaitAsync(0, CancellationToken.None)) throw new InvalidOperationException("Another export is already running.");
        string? partial = null, reserved = null, final = null;
        try
        {
            progress.Report((ExportStage.Preparing, 0));
            if (!File.Exists(request.Source)) throw new FileNotFoundException("The recording is no longer available.", request.Source);
            var plan = ExportPlanner.Plan(request);
            Directory.CreateDirectory(request.OutputFolder);
            long need = ExportPlanner.EstimateBytes(request, plan, new FileInfo(request.Source).Length);
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(request.OutputFolder))!).AvailableFreeSpace;
            if (free < need) throw new IOException($"Not enough free space: about {need / 1048576d:0} MB needed, {free / 1048576d:0} MB available.");
            // Reserve the final name first (CreateNew), so no existing file can ever be overwritten.
            if (target == null) final = reserved = Files.Reserve(request.OutputFolder, label, "mp4");
            else
            {
                final = Path.GetFullPath(target);
                if (string.Equals(final, Path.GetFullPath(request.Source), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a different name: the clip cannot replace its own recording.");
                // A new name is reserved (CreateNew) like automatic saves; an existing file is only replaced after the user confirmed it, and only after verification.
                if (!File.Exists(final)) { using (new FileStream(final, FileMode.CreateNew)) { } reserved = final; }
                else if (!replace) throw new IOException("A file with that name already exists.");
            }
            partial = Path.Combine(request.OutputFolder, "." + Path.GetFileNameWithoutExtension(final) + ".partial.mp4");
            if (File.Exists(partial)) File.Delete(partial);
            var args = ExportPlanner.Arguments(request, plan, plan.StreamCopy ? "copy" : encoder.Id, encoder.Hardware, partial, lowImpact ? 2 : 0);
            cancel.ThrowIfCancellationRequested();
            progress.Report((ExportStage.Encoding, 0));
            using (var p = Backend.Start(ffmpeg, args))
            {
                try { p.PriorityClass = lowImpact ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                var errors = p.StandardError.ReadToEndAsync(CancellationToken.None);
                using var kill = cancel.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } });
                string? line;
                while ((line = await p.StandardOutput.ReadLineAsync(CancellationToken.None)) != null)
                    if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), out long us) && us > 0)
                        progress.Report((ExportStage.Encoding, Math.Clamp(us / 1e6 / plan.Duration, 0, 1)));
                await p.WaitForExitAsync(CancellationToken.None);
                cancel.ThrowIfCancellationRequested();
                if (p.ExitCode != 0) throw new IOException("Export failed: " + Summarize(await errors));
            }
            progress.Report((ExportStage.Finalizing, 1));
            await Backend.Verify(ffmpeg, partial);
            var made = await Probe(ffmpeg, partial);
            if (made.Width != plan.Width || made.Height != plan.Height) throw new IOException($"Export produced {made.Width} × {made.Height} instead of {plan.Width} × {plan.Height}.");
            if (request.Media.HasAudio && !made.HasAudio) throw new IOException("The exported file lost its audio track.");
            if (Math.Abs(made.Duration - plan.Duration) > Math.Max(.5, plan.Duration * .02)) throw new IOException("The exported duration does not match the recording.");
            File.Move(partial, final, true); // replaces our own empty reservation, or a file the user chose to replace
            partial = null;
            progress.Report((ExportStage.Completed, 1));
            reserved = null; return final;
        }
        finally
        {
            foreach (var leftover in new[] { partial, reserved })
                try { if (leftover != null && File.Exists(leftover) && (leftover == partial || new FileInfo(leftover).Length == 0)) File.Delete(leftover); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Trace.WriteLine("Export cleanup pending: " + leftover); }
            Gate.Release();
        }
    }
    static string Summarize(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "unknown error";
        return line.Length > 240 ? line[..240] : line;
    }
}
