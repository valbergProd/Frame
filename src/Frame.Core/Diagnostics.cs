using System.Text.RegularExpressions;

namespace Frame.Core;

// Pure rules behind FRAME's local diagnostics (unit-tested): log retention and the redaction applied to exported reports.
public sealed record LogFile(string Name, long Bytes, DateTime LastWriteUtc);

public static class LogRetention
{
    public const long MaxFileBytes = 5L * 1024 * 1024;   // one log file is rolled over at 5 MB
    public const int MaxAgeDays = 14;                    // files untouched for 14 days are removed
    public const long MaxTotalBytes = 50L * 1024 * 1024; // all logs together stay under 50 MB

    // Files to delete: older than MaxAgeDays, then the oldest until the rest fits in MaxTotalBytes. The newest file
    // (the one being written) is always kept.
    public static IReadOnlyList<string> Expired(IEnumerable<LogFile> files, DateTime nowUtc)
    {
        var ordered = files.OrderByDescending(f => f.LastWriteUtc).ToList();
        var delete = new List<string>(); long total = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var f = ordered[i];
            if (i > 0 && (nowUtc - f.LastWriteUtc > TimeSpan.FromDays(MaxAgeDays) || total + f.Bytes > MaxTotalBytes)) { delete.Add(f.Name); continue; }
            total += f.Bytes;
        }
        return delete;
    }
    // Daily file name with a rollover index: FRAME-20261015.log, FRAME-20261015-1.log, …
    public static string FileName(DateTime localDate, int index) => $"FRAME-{localDate:yyyyMMdd}{(index > 0 ? "-" + index : "")}.log";
}

public static class DiagnosticsRedaction
{
    static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    static readonly Regex Secret = new(@"(?i)\b(password|passwd|pwd|token|secret|api[_-]?key|access[_-]?key|authorization)\b(\s*[""']?\s*[:=]\s*[""']?)[^\s""',;}]+", RegexOptions.Compiled);

    // Removes what identifies the person rather than the problem: the user-profile folder (in plain, JSON-escaped and
    // forward-slash form) becomes %USERPROFILE%, the Windows user name <user>, the computer name <computer>, e-mail
    // addresses <email>, and values of password/token/secret/key fields <redacted>. Paths outside the profile, device
    // and GPU names, versions and error texts stay, because they are what a bug report needs.
    public static string Redact(string text, string? userProfile, string? userName, string? computerName)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!string.IsNullOrEmpty(userProfile) && userProfile.Length > 3)
        {
            string trimmed = userProfile.TrimEnd('\\', '/');
            foreach (var form in new[] { trimmed.Replace("\\", "\\\\"), trimmed, trimmed.Replace('\\', '/') }.Distinct())
                text = text.Replace(form, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        text = Email.Replace(text, "<email>");
        text = Secret.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + "<redacted>");
        if (!string.IsNullOrEmpty(userName) && userName.Length >= 3) text = Regex.Replace(text, @"(?<![A-Za-z0-9])" + Regex.Escape(userName) + @"(?![A-Za-z0-9])", "<user>", RegexOptions.IgnoreCase);
        if (!string.IsNullOrEmpty(computerName) && computerName.Length >= 3) text = Regex.Replace(text, @"(?<![A-Za-z0-9])" + Regex.Escape(computerName) + @"(?![A-Za-z0-9])", "<computer>", RegexOptions.IgnoreCase);
        return text;
    }
}

// Detecting that the previous session ended without a normal exit (crash, kill, power loss): a session marker written at
// start and removed at a normal exit. Pure decision so it can be tested; the App owns the files.
public static class SessionMarker
{
    public static bool EndedUnexpectedly(bool markerExists, int? markerPid, bool markerProcessStillRunning) => markerExists && !(markerPid.HasValue && markerProcessStillRunning);
}
