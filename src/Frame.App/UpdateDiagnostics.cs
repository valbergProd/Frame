using System.Security.Cryptography;
using System.Text.Json;

namespace Frame.App;

// Explicit, headless diagnostics. No network, capture, settings changes or actual installer launch.
internal static class UpdateDiagnostics
{
    internal static async Task Run(string folder, string fixture)
    {
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, Path.GetFileName(fixture)); File.Copy(fixture, destination);
        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(destination);
        string number = version.FileMajorPart + "." + version.FileMinorPart + "." + version.FileBuildPart;
        using var input = File.OpenRead(destination); string hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
        var ticket = new UpdateInstaller.Ticket(int.MaxValue, 0, destination, hash, input.Length, number);
        string file = Path.Combine(folder, "install.json"); var checks = new List<object>();
        async Task Check(string name, UpdateInstaller.Ticket t, bool expected)
        {
            bool launched = false; string? error = null;
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(t));
            try { await UpdateInstaller.Apply(file, _ => launched = true); } catch (Exception e) { error = e.Message; }
            checks.Add(new { name, passed = launched == expected && (expected ? error == null : error != null), error });
        }
        await Check("Verified FRAME installer reaches the launch boundary", ticket, true);
        await Check("Changed installer digest blocks execution", ticket with { Sha256 = new string('0', 64) }, false);
        await Check("Wrong installer length blocks execution", ticket with { Size = ticket.Size + 1 }, false);
        await Check("Wrong installer version blocks execution", ticket with { Version = "99.0.0" }, false);
        await Check("Installer outside ticket directory is rejected", ticket with { Installer = fixture }, false);
        string handoff = Path.Combine(folder, "handoff.json");
        var update = new AppUpdate(ReleaseVersion.Parse(number + "-beta")!, new Uri("https://github.com/valbergProd/Frame"), new Uri("https://github.com/valbergProd/Frame"), Path.GetFileName(destination), ticket.Size, hash);
        UpdateInstaller.Start(destination, update, handoff);
        await Task.Delay(1500);
        checks.Add(new { name = "Helper waits while parent FRAME is alive", passed = !File.Exists(handoff), error = (string?)null });
        await File.WriteAllTextAsync(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    }
}
