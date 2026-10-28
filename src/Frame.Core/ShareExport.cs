using System.Globalization;

namespace Frame.Core;

// Local-first sharing: presets, framing math and ffmpeg argument planning. No accounts, uploads or network calls.
// Pure and deterministic so the Phase 3 trimming editor can reuse it with a time range.
public enum Framing { Original, Crop, Fit, Blur }
public enum ExportQuality { High, Balanced, Small }

public sealed record SharePreset(string Id, string Name, string Hint, int AspectW, int AspectH, int ShortSide, Framing Framing, bool CopyWhenPossible, int MaxFps, string Url);

public static class SharePresets
{
    // Sensible starting points, not platform requirements. ShortSide 0 keeps the source size.
    public static readonly SharePreset YouTube = new("youtube", "YouTube", "Original landscape, high quality", 0, 0, 0, Framing.Original, true, 0, "https://www.youtube.com/upload");
    public static readonly SharePreset Shorts = new("shorts", "YouTube Shorts", "Vertical 1080 × 1920", 9, 16, 1080, Framing.Blur, false, 60, "https://www.youtube.com/upload");
    public static readonly SharePreset TikTok = new("tiktok", "TikTok", "Vertical 1080 × 1920", 9, 16, 1080, Framing.Crop, false, 60, "https://www.tiktok.com/upload");
    public static readonly SharePreset Discord = new("discord", "Discord", "Original file, no re-encode", 0, 0, 0, Framing.Original, true, 0, "https://discord.com/app");
    public static readonly SharePreset Custom = new("custom", "Custom", "Your format and destination", 0, 0, 0, Framing.Original, true, 0, "");
    public static IReadOnlyList<SharePreset> All { get; } = [YouTube, Shorts, TikTok, Discord, Custom];
    public static SharePreset Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? YouTube;
}

public sealed record MediaInfo(int Width, int Height, double Duration, double Fps, string VideoCodec, bool HasAudio, string AudioCodec);

// Everything an export needs; Range is null for the whole recording (Phase 3 passes a trimmed range).
public sealed record ExportRequest(string Source, MediaInfo Media, int AspectW, int AspectH, int ShortSide, Framing Framing, double FocusX, double FocusY, ExportQuality Quality, int MaxFps, bool CopyWhenPossible, string OutputFolder, TimeRange? Range = null);

public sealed record ExportPlan(int Width, int Height, bool StreamCopy, string? Filter, double Fps, double Duration);

public static class ExportPlanner
{
    public static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);

    public static (int Width, int Height) OutputSize(MediaInfo media, int aspectW, int aspectH, int shortSide)
    {
        if (media.Width <= 0 || media.Height <= 0) throw new ArgumentException("The recording has no readable video size.");
        double aspect = aspectW > 0 && aspectH > 0 ? (double)aspectW / aspectH : (double)media.Width / media.Height;
        double side = shortSide > 0 ? shortSide : Math.Min(media.Width, media.Height);
        return aspect >= 1 ? (Even(side * aspect), Even(side)) : (Even(side), Even(side / aspect));
    }

    public static ExportPlan Plan(ExportRequest r)
    {
        if (r.FocusX is < 0 or > 1 || r.FocusY is < 0 or > 1 || !double.IsFinite(r.FocusX) || !double.IsFinite(r.FocusY)) throw new ArgumentException("Framing position is out of range.");
        var (w, h) = OutputSize(r.Media, r.AspectW, r.AspectH, r.ShortSide);
        double duration = r.Range is { } range ? Math.Min(range.End, r.Media.Duration) - Math.Max(0, range.Start) : r.Media.Duration;
        if (!(duration > 0)) throw new ArgumentException("The selected range is empty.");
        double fps = r.MaxFps > 0 && r.Media.Fps > r.MaxFps + .5 ? r.MaxFps : r.Media.Fps;
        bool sameSize = w == r.Media.Width && h == r.Media.Height;
        bool compatible = r.Media.VideoCodec is "h264" or "hevc" && (!r.Media.HasAudio || r.Media.AudioCodec is "aac" or "mp3" or "opus");
        // Trimmed ranges are always re-encoded. Measured with ffmpeg 8.1: stream copy snaps the start to a keyframe,
        // copied B-frame packets overrun the end by 1-2 frames, and even keyframe-to-end copies drifted ~0.1 s between
        // audio and video. Exact boundaries and sync matter more than avoiding an encode; whole files still copy.
        bool copy = r.CopyWhenPossible && sameSize && r.Range == null && fps == r.Media.Fps && compatible;
        return new(w, h, copy, copy ? null : Filter(r.Media, w, h, r.Framing, r.FocusX, r.FocusY, fps == r.Media.Fps ? 0 : fps), fps, duration);
    }

    static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public static string Filter(MediaInfo m, int w, int h, Framing framing, double focusX, double focusY, double fps)
    {
        bool sameAspect = Math.Abs((double)m.Width / m.Height - (double)w / h) < .01;
        if (sameAspect || framing == Framing.Original) framing = Framing.Fit;
        string tail = (fps > 0 ? $",fps={N(fps)}" : "") + ",setsar=1,format=yuv420p";
        // Crop keeps the chosen focus point; preview uses the identical CropOffset math.
        return framing switch
        {
            Framing.Crop => $"[0:v]scale={w}:{h}:force_original_aspect_ratio=increase:flags=lanczos,crop={w}:{h}:(iw-{w})*{N(focusX)}:(ih-{h})*{N(focusY)}{tail}[v]",
            Framing.Blur => $"[0:v]split=2[bg][fg];[bg]scale={Even(w / 8d)}:{Even(h / 8d)}:force_original_aspect_ratio=increase,crop={Even(w / 8d)}:{Even(h / 8d)},boxblur=6:2,scale={w}:{h},eq=brightness=-0.08[b];[fg]scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos[f];[b][f]overlay=(W-w)/2:(H-h)/2{tail}[v]",
            _ => $"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black{tail}[v]",
        };
    }

    // Fraction (0..1) of the scaled overflow to skip on each axis; shared by preview and filter.
    public static (double X, double Y) CropOffset(double sourceW, double sourceH, double frameW, double frameH, double focusX, double focusY)
    {
        double scale = Math.Max(frameW / sourceW, frameH / sourceH);
        return ((sourceW * scale - frameW) * focusX, (sourceH * scale - frameH) * focusY);
    }

    public static int BitrateKbps(int w, int h, double fps, ExportQuality quality)
    {
        double bitsPerPixel = quality switch { ExportQuality.High => .11, ExportQuality.Balanced => .075, _ => .045 };
        return (int)Math.Clamp(w * h * Math.Min(fps, 120) * bitsPerPixel / 1000, 1500, 80000);
    }

    // threads > 0 caps decoder, filter and encoder threads (low-impact export while capture is running).
    public static List<string> Arguments(ExportRequest r, ExportPlan plan, string encoderId, bool hardware, string output, int threads = 0)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-progress", "pipe:1", "-nostats", "-n" };
        if (threads > 0) a.AddRange(["-threads", threads.ToString(System.Globalization.CultureInfo.InvariantCulture), "-filter_complex_threads", "1"]);
        if (r.Range is { } range) a.AddRange(["-ss", N(Math.Max(0, range.Start))]);
        a.AddRange(["-i", r.Source]);
        if (r.Range != null) a.AddRange(["-t", N(plan.Duration)]);
        if (plan.StreamCopy) a.AddRange(["-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
        else
        {
            a.AddRange(["-filter_complex", plan.Filter!, "-map", "[v]", "-map", "0:a:0?", "-c:v", encoderId]);
            // Keep the sub-frame timestamp of an exact trim. The default encoder time base (1/fps) rounds it again
            // while audio retains the exact cut, adding up to half a video frame of A/V offset.
            a.AddRange(["-fps_mode:v", "passthrough", "-enc_time_base:v", "filter"]);
            int kbps = BitrateKbps(plan.Width, plan.Height, plan.Fps, r.Quality);
            if (hardware) a.AddRange(["-b:v", kbps + "k", "-maxrate", kbps * 3 / 2 + "k", "-bufsize", kbps * 2 + "k"]);
            else a.AddRange(["-preset", "medium", "-crf", r.Quality switch { ExportQuality.High => "18", ExportQuality.Balanced => "21", _ => "25" }]);
            if (threads > 0) a.AddRange(["-threads", threads.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            a.AddRange(["-c:a", "aac", "-b:a", "192k", "-ar", "48000"]);
        }
        a.AddRange(["-movflags", "+faststart", "-f", "mp4", output]);
        return a;
    }

    // Conservative worst-case estimate so a full disk is refused before encoding starts.
    public static long EstimateBytes(ExportRequest r, ExportPlan plan, long sourceBytes)
    {
        if (plan.StreamCopy) return sourceBytes + (64L << 20);
        double kbps = BitrateKbps(plan.Width, plan.Height, plan.Fps, r.Quality) * 1.5 + 192;
        return (long)(kbps * 1000 / 8 * plan.Duration) + (64L << 20);
    }
}

public static class ShareDestinations
{
    public static bool ValidUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(u.Host);
}

public sealed record CustomDestination(string Name, string Url, string Preset);
