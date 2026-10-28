using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frame.App;

public enum LogLevel { Debug, Info, Warning, Error, Critical }

// Local, user-readable diagnostics (0.5.4 beta): <data>\Logs\FRAME-yyyyMMdd[-n].log, where <data> is
// %LOCALAPPDATA%\FRAME (or the --data folder of a diagnostic run). One JSON line per event: UTC time, level, FRAME version,
// component, message and, for failures, the exception type, code and stack. Only start-up, state changes and failures are
// written (never per frame or packet). Callers only enqueue (bounded queue, oldest dropped and counted when full); one
// background thread at below-normal priority appends, so capture paths never wait for the disk. Flush() writes pending
// lines synchronously (normal exit, fatal errors). Retention (Frame.Core.LogRetention): 5 MB per file, 14 days, 50 MB in
// total. A failed write never throws. Nothing is uploaded; Settings › Advanced › Export diagnostics zips a redacted copy.
static class DiagnosticLog
{
    const int QueueLimit = 4096;
    static readonly object QueueGate = new(), FileGate = new();
    static readonly Queue<string> Pending = new();
    static readonly AutoResetEvent Signal = new(false);
    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    static Thread? writer;
    static int dropped;
    static string? folder, currentFile;
    public static string Version { get; } = typeof(DiagnosticLog).Assembly.GetName().Version?.ToString(3) ?? "?";
    // Data folder; logs go to its Logs subfolder. Setting it starts the writer and applies retention.
    public static string? Folder
    {
        get => folder;
        set
        {
            folder = value; currentFile = null; if (value == null) return;
            try { Directory.CreateDirectory(LogsFolder!); ApplyRetention(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (writer == null) { writer = new Thread(WriterLoop) { IsBackground = true, Name = "FRAME diagnostics log", Priority = ThreadPriority.BelowNormal }; writer.Start(); }
        }
    }
    public static string? LogsFolder => folder == null ? null : Path.Combine(folder, "Logs");

    sealed record Entry(DateTimeOffset At, string Level, string Version, string Component, string Message, string? Error, int? Code, string? Exception);
    static string Line(string component, string message, LogLevel level, Exception? error) =>
        JsonSerializer.Serialize(new Entry(DateTimeOffset.UtcNow, level.ToString(), Version, component, message, error?.GetType().FullName,
            error == null ? null : error is System.ComponentModel.Win32Exception w ? w.NativeErrorCode : error.HResult, error?.ToString()), Json);

    // Existing call sites: Write(kind, message) logs at Info; failures pass a level and, where available, the exception.
    public static void Write(string component, string message, LogLevel level = LogLevel.Info, Exception? error = null)
    {
        if (folder == null) return;
        string line;
        try { line = Line(component, message, level, error); } catch (Exception e) when (e is NotSupportedException or InvalidOperationException) { return; }
        lock (QueueGate)
        {
            if (Pending.Count >= QueueLimit) { Pending.Dequeue(); dropped++; }
            Pending.Enqueue(line);
        }
        Signal.Set();
    }
    public static void Warning(string component, string message, Exception? error = null) => Write(component, message, LogLevel.Warning, error);
    public static void Error(string component, string message, Exception? error = null) => Write(component, message, LogLevel.Error, error);
    // Fatal paths: written synchronously on the calling thread (the process may end right after).
    public static void WriteNow(string component, string message, LogLevel level, Exception? error = null)
    {
        if (folder == null) return;
        Write(component, message, level, error); Flush();
    }
    // Holds the file lock from taking the lines until they are written: a Flush() that runs while the writer thread is
    // writing waits for it, so "flushed" means on disk (readers and the export see every line logged before the call).
    public static void Flush()
    {
        lock (FileGate)
        {
            List<string> lines;
            lock (QueueGate)
            {
                lines = [.. Pending]; Pending.Clear();
                if (dropped > 0) { lines.Insert(0, Line("diagnostics", $"{dropped} log lines were dropped (queue full)", LogLevel.Warning, null)); dropped = 0; }
            }
            if (lines.Count > 0) Append(lines);
        }
    }
    static void WriterLoop()
    {
        while (true) { Signal.WaitOne(1000); try { Flush(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException) { } }
    }
    static void Append(List<string> lines)
    {
        if (LogsFolder is not { } logs) return;
        lock (FileGate)
        {
            try
            {
                Directory.CreateDirectory(logs);
                if (currentFile == null || !currentFile.Contains(DateTime.Now.ToString("yyyyMMdd")) || (File.Exists(currentFile) && new FileInfo(currentFile).Length > LogRetention.MaxFileBytes)) currentFile = NextFile(logs);
                using var stream = new FileStream(currentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var text = new StreamWriter(stream);
                foreach (var l in lines) text.WriteLine(l);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }
    static string NextFile(string logs)
    {
        for (int i = 0; ; i++)
        {
            string path = Path.Combine(logs, LogRetention.FileName(DateTime.Now, i));
            if (!File.Exists(path) || new FileInfo(path).Length < LogRetention.MaxFileBytes) return path;
        }
    }
    static void ApplyRetention()
    {
        if (LogsFolder is not { } logs || !Directory.Exists(logs)) return;
        var files = new DirectoryInfo(logs).GetFiles("FRAME-*.log").Select(f => new LogFile(f.FullName, f.Length, f.LastWriteTimeUtc)).ToList();
        foreach (var path in LogRetention.Expired(files, DateTime.UtcNow)) try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    // Newest log lines (diagnostic tests and the export summary): pending lines are flushed first.
    public static IEnumerable<string> ReadLines()
    {
        Flush();
        if (LogsFolder is not { } logs || !Directory.Exists(logs)) yield break;
        foreach (var file in new DirectoryInfo(logs).GetFiles("FRAME-*.log").OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name))
        {
            List<string> lines = [];
            try { using var s = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var r = new StreamReader(s); while (r.ReadLine() is string l) lines.Add(l); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var l in lines) yield return l;
        }
    }
    public static void Startup(string version)
    {
        Write("startup", $"FRAME {version} · {DiagnosticsExport.Windows()} · {(Environment.Is64BitProcess ? "x64" : "x86")} · .NET {Environment.Version} · {AppContext.BaseDirectory}");
    }

    // Session marker: Logs\session.json while FRAME runs, removed at a normal exit. Found at the next start (and its process
    // is gone) → the previous session ended unexpectedly; the details stay in Logs\unexpected-exit.json until the user
    // dismisses the note on the Capture page. Logs\crash.json holds the last fatal exception (if one was caught).
    static string? Marker => LogsFolder is { } l ? Path.Combine(l, "session.json") : null;
    public static string? UnexpectedExitFile => LogsFolder is { } l ? Path.Combine(l, "unexpected-exit.json") : null;
    public static string? CrashFile => LogsFolder is { } l ? Path.Combine(l, "crash.json") : null;
    public static void BeginSession()
    {
        if (Marker is not { } marker) return;
        try
        {
            if (File.Exists(marker))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(marker));
                int? pid = doc.RootElement.TryGetProperty("pid", out var p) && p.TryGetInt32(out var v) ? v : null;
                DateTimeOffset? started = doc.RootElement.TryGetProperty("started", out var s) && s.TryGetDateTimeOffset(out var t) ? t : null;
                bool running = false;
                if (pid is int id && started is { } when) try { using var proc = Process.GetProcessById(id); running = Math.Abs((proc.StartTime.ToUniversalTime() - when.UtcDateTime).TotalSeconds) < 5 && !proc.HasExited; } catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                if (SessionMarker.EndedUnexpectedly(true, pid, running))
                {
                    Warning("session", $"The previous FRAME session (started {started:u}) ended without a normal exit (crash, forced close or power loss).");
                    File.WriteAllText(UnexpectedExitFile!, JsonSerializer.Serialize(new { started, detected = DateTimeOffset.UtcNow, crash = File.Exists(CrashFile!) }));
                }
            }
            File.WriteAllText(marker, JsonSerializer.Serialize(new { pid = Environment.ProcessId, started = Process.GetCurrentProcess().StartTime.ToUniversalTime(), version = Version }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public static void EndSession()
    {
        Write("shutdown", "FRAME closed normally"); Flush();
        try { if (Marker is { } m && File.Exists(m)) File.Delete(m); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    // Fatal exception: logged as Critical and kept in Logs\crash.json (synchronous, no UI).
    public static void Crash(string source, Exception error)
    {
        WriteNow("crash", $"Unhandled exception ({source}); FRAME is closing.", LogLevel.Critical, error);
        try { if (CrashFile is { } f) File.WriteAllText(f, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, version = Version, source, type = error.GetType().FullName, message = error.Message, exception = error.ToString() }, Json)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
