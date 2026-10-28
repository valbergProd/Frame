using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Frame.App;

public partial class MainWindow
{
    internal const string UpdateRepository = "torsteinvalberg/Frame";
    static readonly HttpClient updateClient = AppUpdates.CreateClient();
    static ReleaseVersion CurrentRelease => ReleaseVersion.Parse(typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0])!;
    AppUpdate? availableUpdate;
    string? downloadedUpdate;
    string updateMessage = "Updates are downloaded only when you choose.";
    bool checkingUpdate;
    CancellationTokenSource? updateDownload;
    TextBlock? updateStatus;
    Button? checkUpdateButton, downloadUpdateButton, installUpdateButton, cancelUpdateButton;

    void BuildUpdates(Panel general)
    {
        var box = new StackPanel(); var buttons = new WrapPanel();
        checkUpdateButton = ActionButton("Check for updates", async () => await CheckUpdates(false));
        downloadUpdateButton = ActionButton("Download update", async () => await DownloadUpdate());
        installUpdateButton = ActionButton("Install update", async () => await InstallUpdate());
        cancelUpdateButton = ActionButton("Cancel download", () => updateDownload?.Cancel());
        buttons.Children.Add(checkUpdateButton); buttons.Children.Add(downloadUpdateButton); buttons.Children.Add(installUpdateButton); buttons.Children.Add(cancelUpdateButton);
        box.Children.Add(buttons); updateStatus = MutedText(updateMessage, 11, new Thickness(0, 6, 0, 0)); box.Children.Add(updateStatus);
        Pair(general, "FRAME " + CurrentRelease, box, "Updates from the official FRAME release page"); RefreshUpdates();
    }
    void RefreshUpdates()
    {
        if (updateStatus == null) return;
        updateStatus.Text = updateMessage;
        checkUpdateButton!.IsEnabled = !checkingUpdate && updateDownload == null;
        downloadUpdateButton!.Visibility = availableUpdate != null && downloadedUpdate == null && updateDownload == null ? Visibility.Visible : Visibility.Collapsed;
        installUpdateButton!.Visibility = downloadedUpdate != null ? Visibility.Visible : Visibility.Collapsed;
        cancelUpdateButton!.Visibility = updateDownload != null ? Visibility.Visible : Visibility.Collapsed;
    }
    async Task CheckUpdates(bool startup)
    {
        if (checkingUpdate || updateDownload != null || quitRequested) return;
        checkingUpdate = true; updateMessage = "Checking for updates…"; RefreshUpdates();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var next = await new AppUpdates(updateClient, UpdateRepository).Check(CurrentRelease, timeout.Token);
            if (availableUpdate?.Version != next?.Version) downloadedUpdate = null;
            availableUpdate = next;
            updateMessage = next == null ? "No newer installer is published for this release channel." : "FRAME " + next.Version + " is available.";
            if (startup && next != null && !quitRequested) Notify(updateMessage + " Open Settings › General to update.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            updateMessage = "Could not check for updates. Try again later.";
            DiagnosticLog.Warning("updates", "Update check failed: " + e.Message);
        }
        finally { checkingUpdate = false; RefreshUpdates(); }
    }
    async Task DownloadUpdate()
    {
        if (availableUpdate is not { } next || updateDownload != null || quitRequested) return;
        updateDownload = new CancellationTokenSource(); updateMessage = "Downloading FRAME " + next.Version + "…"; RefreshUpdates();
        try
        {
            string directory = Path.Combine(data, "Updates", next.Version.ToString(), Guid.NewGuid().ToString("N"));
            var progress = new Progress<double>(fraction => { if (updateDownload == null) return; updateMessage = $"Downloading FRAME {next.Version} · {fraction:P0}"; RefreshUpdates(); });
            downloadedUpdate = await new AppUpdates(updateClient, UpdateRepository).Download(next, directory, progress, updateDownload.Token);
            updateMessage = "Update verified. Stop recording, then choose Install update.";
        }
        catch (OperationCanceledException) { updateMessage = "Download cancelled. You can try again."; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException)
        { updateMessage = "Update download failed. " + e.Message; DiagnosticLog.Warning("updates", updateMessage); }
        finally { updateDownload.Dispose(); updateDownload = null; RefreshUpdates(); }
    }
    bool UpdateInstallBusy() => engine.Busy || savingReplay || actions.CurrentCount == 0 || ShareExportJob.Busy
        || Application.Current.Windows.OfType<Window>().Any(w => w is TrimWindow or ShareWindow);
    async Task InstallUpdate()
    {
        if (downloadedUpdate == null || availableUpdate == null || quitRequested) return;
        if (UpdateInstallBusy()) { updateMessage = "Stop recording and close the trim/share editor before installing."; RefreshUpdates(); return; }
        if (!QuietDialog.Confirm(this, "Install FRAME " + availableUpdate.Version, "FRAME will close and open the update installer. Your settings and recordings will be kept.", "Close and install")) return;
        try
        {
            if (UpdateInstallBusy() || quitRequested) return;
            UpdateInstaller.Start(downloadedUpdate, availableUpdate);
            await Quit();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { updateMessage = "Could not start the installer. " + e.Message; RefreshUpdates(); }
    }
    async Task ValidateUpdateControls(string report)
    {
        var checks = new List<object>();
        void Check(string name, bool passed) => checks.Add(new { name, passed });
        Check("Initial state: check available, download and install hidden", checkUpdateButton!.IsEnabled && downloadUpdateButton!.Visibility == Visibility.Collapsed && installUpdateButton!.Visibility == Visibility.Collapsed);
        availableUpdate = new(ReleaseVersion.Parse("9.0.0-beta")!, new Uri("https://github.com/" + UpdateRepository), new Uri("https://github.com/" + UpdateRepository), "FRAME-9.0.0-Beta-Setup.exe", 1, new string('0', 64)); RefreshUpdates();
        Check("New version enables download", downloadUpdateButton!.Visibility == Visibility.Visible && installUpdateButton!.Visibility == Visibility.Collapsed);
        updateDownload = new CancellationTokenSource(); RefreshUpdates();
        Check("During download: check disabled, cancel visible", !checkUpdateButton.IsEnabled && cancelUpdateButton!.Visibility == Visibility.Visible && downloadUpdateButton.Visibility == Visibility.Collapsed);
        updateDownload.Dispose(); updateDownload = null; downloadedUpdate = "diagnostic-only"; RefreshUpdates();
        Check("Verified download exposes install", installUpdateButton!.Visibility == Visibility.Visible && cancelUpdateButton!.Visibility == Visibility.Collapsed);
        actions.Wait(); Check("In-flight action prevents installation", UpdateInstallBusy()); actions.Release();
        availableUpdate = null; downloadedUpdate = null; RefreshUpdates();
        Check("Reset returns to initial state", checkUpdateButton.IsEnabled && installUpdateButton.Visibility == Visibility.Collapsed);
        await File.WriteAllTextAsync(report, System.Text.Json.JsonSerializer.Serialize(checks, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
