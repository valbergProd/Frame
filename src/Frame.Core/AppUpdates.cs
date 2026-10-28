using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Frame.Core;

public sealed record ReleaseVersion(Version Number, int? Beta) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion? Parse(string value)
    {
        var m = Regex.Match(value, @"^v?(\d+\.\d+\.\d+)(?:-beta(?:\.(\d+))?)?$", RegexOptions.CultureInvariant);
        if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var number)) return null;
        int? beta = null;
        if (value.Contains("-beta")) { if (!int.TryParse(m.Groups[2].Success ? m.Groups[2].Value : "0", out int n)) return null; beta = n; }
        return new(number, beta);
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other == null) return 1;
        int n = Number.CompareTo(other.Number); if (n != 0) return n;
        return Beta == other.Beta ? 0 : Beta == null ? 1 : other.Beta == null ? -1 : Beta.Value.CompareTo(other.Beta.Value);
    }
    public override string ToString() => Number.ToString(3) + (Beta is { } b ? "-beta" + (b == 0 ? "" : "." + b) : "");
}

public sealed record AppUpdate(ReleaseVersion Version, Uri Download, Uri Notes, string FileName, long Size, string Sha256);

public sealed class AppUpdates(HttpClient client, string repository)
{
    public const long MaxInstallerBytes = 512L * 1024 * 1024;
    public static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FRAME-Updater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
    public async Task<AppUpdate?> Check(ReleaseVersion current, CancellationToken cancel)
    {
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new InvalidOperationException("Update repository is not configured.");
        using var response = await client.GetAsync("https://api.github.com/repos/" + repository + "/releases?per_page=100", HttpCompletionOption.ResponseHeadersRead, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("The update feed is not available yet.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new IOException("The update service is busy. Try again later.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var limited = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer, cancel)) > 0) { if (limited.Length + count > 2 * 1024 * 1024) throw new IOException("Update response is too large."); await limited.WriteAsync(buffer.AsMemory(0, count), cancel); }
        return Select(limited.ToArray(), repository, current);
    }
    public static AppUpdate? Select(byte[] json, string repository, ReleaseVersion current)
    {
        using var doc = JsonDocument.Parse(json); AppUpdate? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = ReleaseVersion.Parse(tag);
            if (version == null || version.CompareTo(current) <= 0 || (current.Beta == null && version.Beta != null)
                || release.GetProperty("prerelease").GetBoolean() != (version.Beta != null)) continue;
            string file = "FRAME-" + version.Number.ToString(3) + (version.Beta != null ? "-Beta" : "") + "-Setup.exe";
            var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == file && a.GetProperty("state").GetString() == "uploaded");
            if (asset.ValueKind == JsonValueKind.Undefined) continue; // a release without an installer is not an app update
            string? digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            long size = asset.GetProperty("size").GetInt64();
            string expected = "https://github.com/" + repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + file;
            if (size <= 0 || size > MaxInstallerBytes || digest == null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")
                || asset.GetProperty("browser_download_url").GetString() != expected) throw new IOException("The published update has invalid download details.");
            var candidate = new AppUpdate(version, new Uri(expected), new Uri("https://github.com/" + repository + "/releases/tag/" + Uri.EscapeDataString(tag)), file, size, digest[7..]);
            if (best == null || candidate.Version.CompareTo(best.Version) > 0) best = candidate;
        }
        return best;
    }
    public async Task<string> Download(AppUpdate update, string folder, IProgress<double>? progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, update.FileName), partial = path + ".partial";
        bool created = false;
        try
        {
            using var response = await client.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, cancel); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != update.Size) throw new IOException("Update size does not match the published file.");
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                created = true; long lastProgress = 0;
                byte[] buffer = new byte[65536]; long total = 0; int n;
                while ((n = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    total += n; if (total > update.Size || total > MaxInstallerBytes) throw new IOException("Update is larger than expected.");
                    hash.AppendData(buffer, 0, n); await output.WriteAsync(buffer.AsMemory(0, n), cancel);
                    long now = Environment.TickCount64;
                    if (now - lastProgress >= 200 || total == update.Size) { progress?.Report(total / (double)update.Size); lastProgress = now; }
                }
                if (total != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update verification failed. Download it again.");
            }
            cancel.ThrowIfCancellationRequested(); File.Move(partial, path); return path;
        }
        catch { if (created) try { File.Delete(partial); } catch (IOException) { } throw; }
    }
}
