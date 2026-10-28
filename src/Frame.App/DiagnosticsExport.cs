using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Frame.App;

// Settings › Advanced › Export diagnostics: one ZIP the user saves and may attach to a bug report. Started only by the
// user; nothing is uploaded. Contents: summary.txt (versions, Windows, CPU/GPU/driver, displays, audio, recording and
// FFmpeg configuration, the last errors), FRAME's logs, crash/unexpected-exit details, recording-status.json, settings
// (without the custom share destination) and FFmpeg's own error logs of failed sessions (Recovery\*\engine.log). Never:
// videos, segments, screenshots, thumbnails, audio. Every text file is redacted (Frame.Core.DiagnosticsRedaction: profile
// folder, user and computer names, e-mail addresses, secret-looking values). A missing optional part is noted in the
// summary instead of failing the export.
static class DiagnosticsExport
{
    public sealed record Context(string DataFolder, string Displays, string Audio, string Recording, string Engine, string? SettingsFile);
    public sealed record Result(IReadOnlyList<string> Entries, IReadOnlyList<string> Skipped);
    const long MaxLogBytes = 20L * 1024 * 1024, EngineLogTail = 256 * 1024;

    public static Result Create(string zipPath, Context context)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), user = Environment.UserName, computer = Environment.MachineName;
        string Clean(string text) => DiagnosticsRedaction.Redact(text, profile, user, computer);
        var entries = new List<string>(); var skipped = new List<string>();
        string partial = zipPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(partial)) File.Delete(partial);
        using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            void Add(string name, string text) { var e = zip.CreateEntry(name, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(Clean(text)); entries.Add(name); }
            string? Read(string path, long tail = long.MaxValue)
            {
                try
                {
                    using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (s.Length > tail) s.Seek(-tail, SeekOrigin.End);
                    using var r = new StreamReader(s); return r.ReadToEnd();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add($"{Path.GetFileName(path)}: {e.Message}"); return null; }
            }

            // Logs, newest first, until 20 MB.
            var logLines = new List<string>(); long logBytes = 0;
            string logs = Path.Combine(context.DataFolder, "Logs");
            if (Directory.Exists(logs))
                foreach (var file in new DirectoryInfo(logs).GetFiles("FRAME-*.log").OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    if (logBytes + file.Length > MaxLogBytes) { skipped.Add($"{file.Name}: over the 20 MB log limit"); continue; }
                    if (Read(file.FullName) is { } text) { Add("logs/" + file.Name, text); logBytes += file.Length; logLines.AddRange(text.Split('\n')); }
                }
            else skipped.Add("Logs folder: not created yet");
            foreach (var legacy in new[] { "diagnostics.log", "diagnostics.log.previous" })
                if (File.Exists(Path.Combine(context.DataFolder, legacy)) && Read(Path.Combine(context.DataFolder, legacy), 2 * 1024 * 1024) is { } text) Add("logs/legacy-" + legacy, text);
            if (Directory.Exists(logs))
                foreach (var file in new DirectoryInfo(logs).GetFiles("*.json").Where(f => f.Name.StartsWith("crash") || f.Name == "unexpected-exit.json"))
                    if (Read(file.FullName) is { } text) Add("crash/" + file.Name, text);
            if (File.Exists(Path.Combine(context.DataFolder, "recording-status.json")) && Read(Path.Combine(context.DataFolder, "recording-status.json")) is { } status) Add("recording-status.json", status);
            if (context.SettingsFile != null && Read(context.SettingsFile) is { } settingsText)
            {
                try { var node = JsonNode.Parse(settingsText)!.AsObject(); node.Remove("ShareCustom"); Add("settings.json", node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
                catch (JsonException) { skipped.Add("settings.json: not valid JSON (not included)"); }
            }
            // FFmpeg's own messages of sessions that failed (their folders are kept for recovery; segments are never copied).
            string recovery = Path.Combine(context.DataFolder, "Recovery");
            if (Directory.Exists(recovery))
                foreach (var log in new DirectoryInfo(recovery).GetFiles("engine.log", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).Take(3))
                    if (Read(log.FullName, EngineLogTail) is { } text) Add($"engine/{log.Directory!.Name}-engine.log", text);

            var errors = logLines.Select(ParseError).Where(e => e != null).TakeLast(25).ToList();
            Add("summary.txt", Summary(context, errors!, entries, skipped));
            Add("README.txt", "FRAME diagnostics\r\n\r\nThis archive was created on your PC when you clicked Export diagnostics. Nothing was uploaded.\r\n" +
                "It contains FRAME's logs, version and system information and your FRAME settings. It does not contain videos, screenshots, audio or other files.\r\n" +
                "Your Windows user name, computer name, user-folder paths and e-mail addresses were replaced (for example %USERPROFILE%, <user>).\r\n" +
                "Please look through the files before you send them: technical logs can still mention details such as folder or device names.\r\n");
        }
        if (File.Exists(zipPath)) File.Delete(zipPath);
        File.Move(partial, zipPath);
        return new(entries, skipped);
    }

    static string? ParseError(string line)
    {
        if (!line.TrimStart().StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
            string level = r.TryGetProperty("Level", out var l) ? l.GetString() ?? "" : "";
            if (level is not ("Error" or "Critical" or "Warning")) return null;
            return $"{r.GetProperty("At").GetString()} · {level} · {r.GetProperty("Component").GetString()} · {r.GetProperty("Message").GetString()?.Split('\n')[0]}";
        }
        catch (JsonException) { return null; }
    }

    static string Summary(Context c, IEnumerable<string> errors, IReadOnlyList<string> entries, IReadOnlyList<string> skipped)
    {
        var s = new StringBuilder();
        var exe = Environment.ProcessPath ?? "";
        string build = File.Exists(exe) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion + $" (exe built {File.GetLastWriteTimeUtc(exe):u})" : "unknown";
        s.AppendLine($"FRAME diagnostics · exported {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        s.AppendLine($"FRAME {DiagnosticLog.Version} · build {build}");
        s.AppendLine($"Installed at: {AppContext.BaseDirectory}");
        s.AppendLine();
        s.AppendLine("System");
        s.AppendLine("  " + Windows());
        s.AppendLine($"  .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process, {Environment.ProcessorCount} logical processors, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824d:0.#} GB memory");
        s.AppendLine("  CPU: " + Cpu());
        foreach (var gpu in Gpus()) s.AppendLine("  GPU: " + gpu);
        s.AppendLine();
        s.AppendLine("Displays"); s.AppendLine(c.Displays); s.AppendLine();
        s.AppendLine("Audio"); s.AppendLine(c.Audio); s.AppendLine();
        s.AppendLine("Recording"); s.AppendLine(c.Recording); s.AppendLine();
        s.AppendLine("FFmpeg"); s.AppendLine(c.Engine); s.AppendLine();
        s.AppendLine("Recent warnings and errors (newest last)");
        var list = errors.ToList();
        if (list.Count == 0) s.AppendLine("  none logged"); else foreach (var e in list) s.AppendLine("  " + e);
        s.AppendLine();
        s.AppendLine("Files in this archive: " + string.Join(", ", entries.Append("summary.txt").Append("README.txt")));
        if (skipped.Count > 0) { s.AppendLine("Not included:"); foreach (var x in skipped) s.AppendLine("  " + x); }
        return s.ToString();
    }
    // Windows 11 still reports "Windows 10" in ProductName; the build number (22000 or newer) tells them apart.
    internal static string Windows()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = k?.GetValue("ProductName") as string ?? "Windows", build = k?.GetValue("CurrentBuild") as string ?? "?";
            if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            return $"{product} {k?.GetValue("DisplayVersion")} (build {build}.{k?.GetValue("UBR")})";
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return Environment.OSVersion.VersionString; }
    }
    static string Cpu()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"); return (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? "unknown"; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return "unknown"; }
    }
    // Display adapters from the device class key (readable without admin): name and driver version.
    static IEnumerable<string> Gpus()
    {
        var list = new List<string>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var name in cls?.GetSubKeyNames().Where(n => n.All(char.IsDigit)) ?? [])
            {
                try { using var k = cls!.OpenSubKey(name); if (k?.GetValue("DriverDesc") is string desc) list.Add($"{desc} · driver {k.GetValue("DriverVersion")} ({k.GetValue("DriverDate")})"); }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { list.Add("unavailable"); }
        return list.Count > 0 ? list.Distinct() : ["unavailable"];
    }
}
