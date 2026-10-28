using Frame.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

internal static class UpdateTests
{
    internal static void Run(Action<string, Action> test, Action<bool> assert, Action<Action> throws, string folder)
    {
        const string repo = "valbergProd/Frame";
        var beta = ReleaseVersion.Parse("0.6.6-beta")!; var stable = ReleaseVersion.Parse("0.6.6")!;
        byte[] payload = [0x4d, 0x5a, 1, 2, 3, 4]; string sha = Convert.ToHexString(SHA256.HashData(payload));
        object Release(string tag, bool pre = true, bool draft = false, string? digest = null, string? url = null, long? size = null, bool installer = true)
        {
            var v = ReleaseVersion.Parse(tag)!; string file = "FRAME-" + v.Number.ToString(3) + (v.Beta != null ? "-Beta" : "") + "-Setup.exe";
            return new { tag_name = tag, prerelease = pre, draft, assets = installer ? new[] { new { name = file, state = "uploaded", size = size ?? payload.Length, digest = digest ?? "sha256:" + sha, browser_download_url = url ?? "https://github.com/" + repo + "/releases/download/" + tag + "/" + file } } : [] };
        }
        byte[] Feed(params object[] releases) => JsonSerializer.SerializeToUtf8Bytes(releases);
        AppUpdate? Select(ReleaseVersion current, params object[] releases) => AppUpdates.Select(Feed(releases), repo, current);
        var update = Select(beta, Release("v0.6.7-beta"))!;
        test("Updates: release versions order beta, numbered betas and stable correctly", () => assert(beta.CompareTo(stable) < 0 && ReleaseVersion.Parse("0.6.6-beta.2")!.CompareTo(beta) > 0 && ReleaseVersion.Parse("0.7.0-beta")!.CompareTo(stable) > 0));
        test("Updates: reject unsupported, malformed and overflow versions", () => assert(new[] { "v1.2", "1.2.3-rc.1", "1.2.3/path", "999999999999.1.0", "1.2.3-beta.999999999999" }.All(s => ReleaseVersion.Parse(s) == null)));
        test("Updates: stable installations never receive beta releases", () => assert(Select(stable, Release("v0.7.0-beta")) == null));
        test("Updates: beta can advance to stable with the same numeric version", () => assert(Select(beta, Release("v0.6.6", false))?.Version == stable));
        test("Updates: skip drafts, old/current versions and releases without installers", () => assert(Select(beta, Release("v0.8.0-beta", draft: true), Release("v0.6.6-beta"), Release("v0.6.5-beta"), Release("v0.9.0-beta", installer: false)) == null));
        test("Updates: choose highest version independent of publication ordering", () => assert(Select(beta, Release("v0.7.0-beta"), Release("v0.9.0-beta"), Release("v0.8.0-beta"))!.Version.Number.Minor == 9));
        test("Updates: prerelease flag must match the tag", () => assert(Select(beta, Release("v0.7.0-beta", false), Release("v0.8.0", true)) == null));
        test("Updates: empty release feed is valid", () => assert(Select(beta) == null));
        test("Updates: reject untrusted installer URL", () => throws(() => Select(beta, Release("v0.7.0-beta", url: "https://example.org/FRAME-Setup.exe"))));
        test("Updates: reject corrupt checksum metadata and oversized files", () => { throws(() => Select(beta, Release("v0.7.0-beta", digest: "sha256:bad"))); throws(() => Select(beta, Release("v0.7.0-beta", size: AppUpdates.MaxInstallerBytes + 1))); });
        test("Updates: successful download verifies hash and commits exactly once", () =>
        {
            using var client = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
            string path = new AppUpdates(client, repo).Download(update, Path.Combine(folder, "good-update"), null, default).GetAwaiter().GetResult();
            assert(File.ReadAllBytes(path).SequenceEqual(payload) && !File.Exists(path + ".partial"));
            throws(() => new AppUpdates(client, repo).Download(update, Path.GetDirectoryName(path)!, null, default).GetAwaiter().GetResult());
            assert(File.ReadAllBytes(path).SequenceEqual(payload));
        });
        test("Updates: tampered download rejected with no executable or partial left", () =>
        {
            using var client = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([0, 0, 0, 0, 0, 0]) }));
            string dir = Path.Combine(folder, "bad-update"); throws(() => new AppUpdates(client, repo).Download(update, dir, null, default).GetAwaiter().GetResult()); assert(!Directory.EnumerateFiles(dir).Any());
        });
        test("Updates: truncated download rejected", () =>
        {
            using var client = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }));
            throws(() => new AppUpdates(client, repo).Download(update, Path.Combine(folder, "short-update"), null, default).GetAwaiter().GetResult());
        });
        test("Updates: cancellation never publishes an installer", () =>
        {
            using var client = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            string dir = Path.Combine(folder, "cancel-update"); throws(() => new AppUpdates(client, repo).Download(update, dir, null, cancel.Token).GetAwaiter().GetResult()); assert(!Directory.EnumerateFiles(dir).Any());
        });
        test("Updates: existing partial download is never overwritten or deleted", () =>
        {
            using var client = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
            string dir = Path.Combine(folder, "existing-update"); Directory.CreateDirectory(dir); string file = Path.Combine(dir, update.FileName + ".partial"); File.WriteAllText(file, "keep");
            throws(() => new AppUpdates(client, repo).Download(update, dir, null, default).GetAwaiter().GetResult()); assert(File.ReadAllText(file) == "keep");
        });
        test("Updates: API request targets only configured public releases, without credentials", () =>
        {
            using var client = new HttpClient(new Reply(request => { assert(request.RequestUri!.ToString() == "https://api.github.com/repos/" + repo + "/releases?per_page=100" && request.Headers.Authorization == null); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Feed(Release("v0.6.7-beta"))) }; }));
            assert(new AppUpdates(client, repo).Check(beta, default).GetAwaiter().GetResult() == update);
        });
        test("Updates: unavailable feed and rate limits never mean up to date", () =>
        {
            foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
            { using var client = new HttpClient(new Reply(_ => new(status))); throws(() => new AppUpdates(client, repo).Check(beta, default).GetAwaiter().GetResult()); }
        });
        test("Updates: startup preference persists without changing capture settings", () =>
        {
            string file = Path.Combine(folder, "update-settings.json"); var before = new Settings(); var after = new Settings { CheckForUpdatesOnStartup = false };
            SettingsStore.Save(file, after); assert(!SettingsStore.Load(file).CheckForUpdatesOnStartup && before.CheckForUpdatesOnStartup && !CaptureSettings.Differ(before, after));
        });
    }
    sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
}
