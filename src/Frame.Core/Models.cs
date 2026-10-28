using System.Text.Json;

namespace Frame.Core;

public enum RecordingState { Idle, Starting, Recording, Paused, Finalizing, Faulted }
public sealed record Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
    public long Area => (long)Width * Height;
    public static Box Union(IEnumerable<Box> boxes)
    {
        var a = boxes.ToArray();
        if (a.Length == 0) throw new ArgumentException("Select at least one display.");
        var x = a.Min(b => b.X); var y = a.Min(b => b.Y);
        return new(x, y, a.Max(b => b.Right) - x, a.Max(b => b.Bottom) - y);
    }
}
public sealed record CaptureTarget(string Id, string Name, Box Bounds, long MonitorHandle = 0, long WindowHandle = 0, Box? Crop = null);
// LowLatency: the encoder accepted FRAME's no-delay options at probe time (see Backend.LowLatencyArgs).
public sealed record Encoder(string Id, string Name, bool Hardware, bool DirectGpu, bool LowLatency = false);
public sealed record Hotkey(string Action, uint Modifiers, uint Key);
public sealed class Settings
{
    public string OutputDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FRAME");
    public string FfmpegPath { get; set; } = "";
    public string Encoder { get; set; } = "auto";
    public string[] SelectedDisplays { get; set; } = [];
    // null: migrate older files once displays are available. true follows Windows' current primary, rather than a device id.
    // SelectedDisplays holds deliberate secondary/multi-display choices; temporary recovery never overwrites them.
    public bool? FollowPrimaryDisplay { get; set; }
    public int Fps { get; set; } = 60;
    public int BitrateMbps { get; set; } = 20;
    public int OutputHeight { get; set; }
    public bool Combined { get; set; }
    public bool Cursor { get; set; } = true;
    // Desktop audio is on for new installations (user decision 2026-10-13); a saved choice always wins (settings files
    // contain every key). Microphone stays opt-in. Both follow the Windows default devices unless a device is chosen.
    public bool DesktopAudio { get; set; } = true;
    public bool Microphone { get; set; }
    public string? OutputDevice { get; set; }
    public string? InputDevice { get; set; }
    public int ReplaySeconds { get; set; } = 300;
    public int ShadowUiVersion { get; set; } = 1;
    public int ReplayBudgetMb { get; set; } = 1024;
    // 0.5.5: Sound/Volume (recording cues) and ReduceMotion/AtmosphereVersion were removed (FRAME makes no sounds; decorative
    // motion follows visibility and the Windows animation setting). 0.6.0: Toolbar (the floating "On-screen controls" for manual
    // recording) and LightTheme (the "Light appearance" checkbox, replaced by the visual modes) were removed. Older settings
    // files load unchanged: unknown keys are ignored and dropped at the next save.
    // Visual mode (VisualModes.Key); null/unknown = Cinematic D. Saved the moment the mode changes (no Apply).
    public string? VisualMode { get; set; }
    // Pink mode: background key and active effects (PinkOptions); null = Pink mode never entered (first-entry defaults).
    public string? PinkBackground { get; set; }
    public string[]? PinkEffects { get; set; }
    // Emo mode: background, effects (EmoOptions) and the quote rotation (EmoQuotes: last shown index and the rest of the round).
    public string? EmoBackground { get; set; }
    public string[]? EmoEffects { get; set; }
    public int EmoQuoteLast { get; set; } = -1;
    public int[]? EmoQuoteBag { get; set; }
    public bool Onboarded { get; set; }
    public bool ShowSaveNotifications { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartShadowOnLaunch { get; set; }
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    // Hide FRAME to the tray once when a game starts (GameDetection/GameMinimizePolicy). Default on.
    public bool MinimizeOnGameStart { get; set; } = true;
    // Last folder used by the editor's Save to… ("" = none yet). Save clip always uses <OutputDirectory>\Trimmed Clips.
    public string TrimSaveDirectory { get; set; } = "";
    // Sharing preferences: local and non-sensitive only (no accounts or tokens).
    public string SharePreset { get; set; } = "youtube";
    public string ShareExportDirectory { get; set; } = "";
    public ExportQuality ShareQuality { get; set; } = ExportQuality.High;
    public CustomDestination? ShareCustom { get; set; }
    // "Open" (0.5.4): brings FRAME to the front from anywhere, also from the tray. "Shadow" (0.5.4): starts/stops Shadow
    // Recording; not set until the user chooses one (never takes over a combination). Key 0 = not set.
    public Hotkey[] Hotkeys { get; set; } = [new("Record", 6, 0x52), new("Pause", 6, 0x50), new("Replay", 6, 0x53), new("Monitors", 6, 0x4D), new("Open", 3, 0x46), new("Shadow", 0, 0)];
    public static readonly Hotkey DefaultOpen = new("Open", 3, 0x46); // Ctrl + Alt + F
    public void Validate()
    {
        if (!Path.IsPathFullyQualified(OutputDirectory)) throw new ArgumentException("Choose an absolute capture folder.");
        if (Fps is not (30 or 60 or 120)) throw new ArgumentException("Choose 30, 60 or 120 FPS.");
        if (BitrateMbps is < 2 or > 200 || ReplaySeconds is < 5 or > 3600 || ReplayBudgetMb is < 64 or > 32768) throw new ArgumentException("Quality or replay limits are outside the supported range.");
        if (OutputHeight is not (0 or 720 or 1080 or 1440 or 2160)) throw new ArgumentException("Unsupported output size.");
        if (Hotkeys.Where(h => h.Key != 0).GroupBy(h => (h.Modifiers, h.Key)).Any(g => g.Count() > 1)) throw new ArgumentException("Two actions use the same hotkey.");
        if (TrimSaveDirectory.Length > 0 && !Path.IsPathFullyQualified(TrimSaveDirectory)) throw new ArgumentException("Choose an absolute save folder.");
        if (ShareExportDirectory.Length > 0 && !Path.IsPathFullyQualified(ShareExportDirectory)) throw new ArgumentException("Choose an absolute export folder.");
        if (ShareCustom is { } custom && (custom.Name.Trim().Length is 0 or > 40 || !ShareDestinations.ValidUrl(custom.Url))) throw new ArgumentException("Custom destination needs a name and an http(s) address.");
        if (!Enum.IsDefined(ShareQuality)) throw new ArgumentException("Unknown export quality.");
    }
}
// Settings that change how video/audio is captured (Settings › Apply): with capture running they need Shadow Recording to
// restart; everything else (appearance, notifications, start-up, replay length, hotkeys) applies while recording.
public static class CaptureSettings
{
    public static bool Differ(Settings a, Settings b) =>
        a.Fps != b.Fps || a.OutputHeight != b.OutputHeight || a.BitrateMbps != b.BitrateMbps || a.ReplayBudgetMb != b.ReplayBudgetMb
        || (a.OutputDevice ?? "") != (b.OutputDevice ?? "") || (a.InputDevice ?? "") != (b.InputDevice ?? "") || a.Cursor != b.Cursor || a.Combined != b.Combined
        || a.FfmpegPath != b.FfmpegPath || a.Encoder != b.Encoder || a.DesktopAudio != b.DesktopAudio || a.Microphone != b.Microphone;
}
public static class SettingsStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return new();
        string json = File.ReadAllText(path);
        var s = JsonSerializer.Deserialize<Settings>(json) ?? throw new InvalidDataException("Empty settings file.");
        using var document = JsonDocument.Parse(json);
        // Upgrade only the previous default; retain explicitly saved custom durations.
        if (!document.RootElement.TryGetProperty(nameof(Settings.ShadowUiVersion), out _) && s.ReplaySeconds == 30) s.ReplaySeconds = 300;
        s.Hotkeys = s.Hotkeys.Where(h => h.Action != "Screenshot").ToArray();
        // Settings from before the Open hotkey: add Ctrl+Alt+F, or "not set" when another action already uses it.
        if (!s.Hotkeys.Any(h => h.Action == "Open"))
            s.Hotkeys = [.. s.Hotkeys, s.Hotkeys.Any(h => h.Modifiers == Settings.DefaultOpen.Modifiers && h.Key == Settings.DefaultOpen.Key) ? new Hotkey("Open", 0, 0) : Settings.DefaultOpen];
        // Settings from before the Shadow Recording hotkey: add it as "not set" (every existing binding is kept).
        if (!s.Hotkeys.Any(h => h.Action == "Shadow")) s.Hotkeys = [.. s.Hotkeys, new Hotkey("Shadow", 0, 0)];
        s.Validate(); return s;
    }
    public static void Save(string path, Settings settings)
    {
        settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(f, settings, Options); f.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class Files
{
    public static string Reserve(string folder, string kind, string extension)
    {
        Directory.CreateDirectory(folder);
        for (int i = 0; i < 1000; i++)
        {
            var name = $"FRAME {DateTime.Now:yyyy-MM-dd HH-mm-ss} {kind}{(i == 0 ? "" : $" ({i})")}.{extension}";
            var path = Path.Combine(folder, name);
            try { using var _ = new FileStream(path, FileMode.CreateNew); return path; }
            catch (IOException) when (File.Exists(path)) { }
        }
        throw new IOException("Could not reserve a unique filename.");
    }
}
