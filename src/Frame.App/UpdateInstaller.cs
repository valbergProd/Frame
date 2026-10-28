using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Frame.App;

// A second instance waits for the current process to exit before starting Setup. It never acquires the app mutex,
// loads settings, captures, or installs silently. Inno Setup retains its normal upgrade checks and UI.
internal static class UpdateInstaller
{
    internal sealed record Ticket(int Parent, long Started, string Installer, string Sha256, long Size, string Version);
    internal static void Start(string installer, AppUpdate update, string? diagnosticReport = null)
    {
        using var self = Process.GetCurrentProcess();
        var ticket = new Ticket(self.Id, self.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(installer), update.Sha256, update.Size, update.Version.Number.ToString(3));
        string file = Path.Combine(Path.GetDirectoryName(installer)!, "install.json");
        File.WriteAllText(file, JsonSerializer.Serialize(ticket));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(diagnosticReport == null ? "--apply-update" : "--update-handoff-test"); start.ArgumentList.Add(file);
        if (diagnosticReport != null) start.ArgumentList.Add(diagnosticReport);
        using var helper = Process.Start(start) ?? throw new IOException("Update helper could not start.");
    }
    internal static async Task Apply(string ticketPath, Action<string>? diagnosticLaunch = null)
    {
        string file = Path.GetFullPath(ticketPath);
        var ticket = JsonSerializer.Deserialize<Ticket>(await File.ReadAllTextAsync(file)) ?? throw new IOException("Update details are missing.");
        string installer = Path.GetFullPath(ticket.Installer);
        if (!string.Equals(Path.GetDirectoryName(file), Path.GetDirectoryName(installer), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(installer).StartsWith("FRAME-", StringComparison.Ordinal) || !installer.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Update installer path is invalid.");
        Process? parent = null;
        try { parent = Process.GetProcessById(ticket.Parent); } catch (ArgumentException) { }
        using (parent)
        {
            if (parent != null && parent.StartTime.ToUniversalTime().Ticks == ticket.Started)
            {
                if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update parent does not match FRAME.");
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            }
        }
        // Hold the file against modification through the launch, and verify again after waiting for shutdown.
        using var input = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != ticket.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(ticket.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The downloaded installer changed. Open FRAME and download it again.");
        var version = FileVersionInfo.GetVersionInfo(installer);
        if (version.ProductName?.Trim() != "FRAME" || version.FileMajorPart + "." + version.FileMinorPart + "." + version.FileBuildPart != ticket.Version)
            throw new IOException("The installer does not match the published FRAME version.");
        if (diagnosticLaunch != null) diagnosticLaunch(installer);
        else { using var setup = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true }) ?? throw new IOException("Setup could not start."); }
    }
}
