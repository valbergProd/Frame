using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Frame.App;

// Beta support surfaces (0.5.4): the Capture-page panel when FFmpeg is missing or unsuitable (with a user-started,
// visible winget install), the note after an unexpected exit, and Export diagnostics / Open log folder (Settings › Advanced).
public partial class MainWindow
{
    Exception? engineProblem;
    static string WingetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
    const string FfmpegDownloadPage = "https://ffmpeg.org/download.html#build-windows";

    // FFmpeg is not part of FRAME (license: the FFmpeg builds that include every encoder FRAME uses are GPL). FRAME finds an
    // installed copy (Settings path, tools\ beside FRAME, winget, PATH); without one it explains what to do here. The winget
    // install runs only when the user clicks, in a visible console, from the winget community repository (Gyan.FFmpeg).
    void ShowEngineProblem(Exception error)
    {
        engineProblem = error;
        bool missing = error is FileNotFoundException, winget = File.Exists(WingetPath);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = missing ? "FFmpeg is needed to record" : "This FFmpeg cannot be used", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(214, 110, 70)) });
        body.Children.Add(new TextBlock { Text = missing
            ? "FRAME records and saves clips with FFmpeg, a free video tool that is installed separately (8.1 or newer, with ffmpeg.exe and ffprobe.exe)."
            : error.Message, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        body.Children.Add(new TextBlock { Text = winget
            ? "Install FFmpeg opens a window that runs: winget install --id Gyan.FFmpeg -e --source winget. Follow it, close it when it is done, then click Check again."
            : "winget is not available on this PC. Download a Windows build of FFmpeg 8.1 or newer, unzip it, and choose its ffmpeg.exe.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (winget)
        {
            var install = ActionButton("Install FFmpeg (winget)", InstallFfmpegWithWinget); install.SetResourceReference(BorderBrushProperty, "Accent");
            TooltipPolish.Describe(install, "Opens a console window with the winget command; nothing is installed without it"); row.Children.Add(install);
        }
        else { var page = ActionButton("Open download page", () => OpenLink(FfmpegDownloadPage)); TooltipPolish.Describe(page, "ffmpeg.org: links to Windows builds"); row.Children.Add(page); }
        var choose = ActionButton("Choose ffmpeg.exe…", async () => await ChooseFfmpeg()); TooltipPolish.Describe(choose, "Use an FFmpeg you already have"); row.Children.Add(choose);
        var again = ActionButton("Check again", async () => await Probe()); TooltipPolish.Describe(again, "Look for FFmpeg again"); row.Children.Add(again);
        body.Children.Add(row);
        EnginePanel.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 214, 110, 70)); EnginePanel.Child = body; EnginePanel.Visibility = Visibility.Visible;
    }
    void HideEngineProblem() { engineProblem = null; EnginePanel.Visibility = Visibility.Collapsed; EnginePanel.Child = null; }
    void InstallFfmpegWithWinget()
    {
        try
        {
            const string script = "echo Installing FFmpeg (package Gyan.FFmpeg) from the winget community repository. & echo. & winget install --id Gyan.FFmpeg --exact --source winget & echo. & echo When it has finished, close this window and click Check again in FRAME.";
            Process.Start(new ProcessStartInfo("cmd.exe", "/k " + script) { UseShellExecute = true });
            DiagnosticLog.Write("ffmpeg-install", "User started: winget install --id Gyan.FFmpeg --exact --source winget");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Notify("winget could not be started: " + e.Message); DiagnosticLog.Error("ffmpeg-install", "winget could not be started", e); }
    }
    async Task ChooseFfmpeg()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe", Title = "Choose ffmpeg.exe (FFmpeg 8.1 or newer)" };
        if (dialog.ShowDialog(this) != true) return;
        try { var next = CloneSettings(); next.FfmpegPath = dialog.FileName; next.Validate(); SettingsStore.Save(configPath, next); settings = next; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Notify("Not saved: " + e.Message); DiagnosticLog.Error("settings", "FFmpeg path not saved", e); return; }
        await Probe();
    }
    static void OpenLink(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception e) { DiagnosticLog.Warning("link", "Could not open " + url, e); } }

    // After a session that did not end normally (DiagnosticLog.BeginSession found the marker): one discreet note on the
    // Capture page until the user dismisses it or exports diagnostics from it. Never automatic, never a pop-up.
    void ShowUnexpectedExitNote()
    {
        if (DiagnosticLog.UnexpectedExitFile is not { } file || !File.Exists(file)) { UnexpectedExitNote.Visibility = Visibility.Collapsed; return; }
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "FRAME closed unexpectedly during the previous session. You can export diagnostic information to help investigate the issue.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var row = new WrapPanel { Margin = new Thickness(-8, 6, 0, 0) };
        var export = new Button { Content = "Export diagnostics", Style = (Style)FindResource("QuietButton"), FontSize = 12 }; export.SetResourceReference(ForegroundProperty, "Text");
        export.Click += async (_, _) => { if (await ExportDiagnosticsInteractive() != null) DismissUnexpectedExit(); };
        var dismiss = new Button { Content = "Dismiss", Style = (Style)FindResource("QuietButton"), FontSize = 12 }; dismiss.Click += (_, _) => DismissUnexpectedExit();
        TooltipPolish.Describe(export, "Save a ZIP with logs and system details to share with the FRAME team"); TooltipPolish.Describe(dismiss, "Hide this note; the logs are kept");
        row.Children.Add(export); row.Children.Add(dismiss); body.Children.Add(row);
        UnexpectedExitNote.Child = body; UnexpectedExitNote.Visibility = Visibility.Visible;
    }
    void DismissUnexpectedExit()
    {
        try
        {
            if (DiagnosticLog.UnexpectedExitFile is { } f && File.Exists(f)) File.Delete(f);
            // The crash file belongs to that session; keep it for exports, but out of the way of the next detection.
            if (DiagnosticLog.CrashFile is { } c && File.Exists(c)) File.Move(c, Path.Combine(Path.GetDirectoryName(c)!, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DiagnosticLog.Warning("session", "Unexpected-exit note could not be cleared", e); }
        UnexpectedExitNote.Visibility = Visibility.Collapsed;
    }

    void OpenLogFolder()
    {
        if (DiagnosticLog.LogsFolder is not { } logs) return;
        try { Directory.CreateDirectory(logs); DiagnosticLog.Flush(); OpenFolder(logs); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notify("The log folder could not be opened: " + e.Message); }
    }
    // Save dialog → ZIP. Returns the saved path, or null when cancelled or failed (the reason is in the status line).
    async Task<string?> ExportDiagnosticsInteractive(TextBlock? status = null)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export FRAME diagnostics", FileName = $"FRAME-Diagnostics-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip", DefaultExt = ".zip", AddExtension = true, Filter = "ZIP archive|*.zip", OverwritePrompt = true, InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        if (dialog.ShowDialog(this) != true) return null;
        try
        {
            await ExportDiagnosticsTo(dialog.FileName);
            string done = $"Saved {Path.GetFileName(dialog.FileName)}. Review it before sharing: logs can still mention details of this PC. Nothing was uploaded.";
            Notify(done); if (status != null) { status.Text = done; status.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
            return dialog.FileName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            string failed = "Diagnostics were not exported: " + e.Message;
            Notify(failed); if (status != null) { status.Text = failed; status.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); }
            DiagnosticLog.Error("diagnostics", "Export failed", e); return null;
        }
    }
    internal async Task<DiagnosticsExport.Result> ExportDiagnosticsTo(string zip)
    {
        var context = CollectDiagnosticsContext(); // UI thread: settings, engine, displays, audio endpoints
        var result = await Task.Run(() => DiagnosticsExport.Create(zip, context));
        DiagnosticLog.Write("diagnostics", $"Diagnostics exported ({result.Entries.Count} files, {new FileInfo(zip).Length / 1024} KB)");
        return result;
    }
    DiagnosticsExport.Context CollectDiagnosticsContext()
    {
        string Safe(Func<string> read) { try { return read(); } catch (Exception e) when (e is not OutOfMemoryException) { return "unavailable (" + e.GetType().Name + ")"; } }
        var displays = Safe(() => string.Join(Environment.NewLine, Native.Displays().Select(d => $"  {d.Name}: {d.Bounds.Width} × {d.Bounds.Height} at ({d.Bounds.X}, {d.Bounds.Y}), scale {Native.Scale((nint)d.MonitorHandle) * 100:0} %")));
        var audio = Safe(() => $"  Desktop audio {(settings.DesktopAudio ? "on" : "off")}, microphone {(settings.Microphone ? "on" : "off")}; selected output: {(string.IsNullOrEmpty(settings.OutputDevice) ? "Windows default" : "a specific device")}, input: {(string.IsNullOrEmpty(settings.InputDevice) ? "Windows default" : "a specific device")}"
            + Environment.NewLine + "  Playback devices: " + string.Join("; ", AudioDevices.List(true).Skip(1).Select(d => d.Name)) + Environment.NewLine + "  Recording devices: " + string.Join("; ", AudioDevices.List(false).Skip(1).Select(d => d.Name)));
        var recording = $"  Shadow Recording {(engine.Replay ? "on" : "off")}, state {engine.State}, replay length {settings.ReplaySeconds} s, disk budget {settings.ReplayBudgetMb} MB{Environment.NewLine}" +
            $"  {settings.Fps} FPS, output {(settings.OutputHeight == 0 ? "native" : settings.OutputHeight + "p")}, {settings.BitrateMbps} Mbps, encoder setting '{settings.Encoder}' → {(ready ? ChooseEncoder().Id : "engine not ready")}, combined {settings.Combined}, cursor {settings.Cursor}{Environment.NewLine}" +
            $"  Source: {(testSource.Length > 0 ? "diagnostics test source" : selected.Count + " display(s)")}; hotkeys: {string.Join(", ", settings.Hotkeys.Select(h => h.Action + " " + FormatHotkey(h)))}" + (hotkeyIssue.Length > 0 ? Environment.NewLine + "  " + hotkeyIssue : "") +
            (fault != null ? $"{Environment.NewLine}  Last recording fault: {fault.At:u} · {fault.Kind} · {fault.Explanation}" : "");
        var engineText = ready ? $"  {Backend.EngineVersion}{Environment.NewLine}  Path: {Backend.EnginePath}{Environment.NewLine}  Encoders that passed the probe: {string.Join(", ", encoders.Select(e => e.Id))}"
            : "  Not ready" + (engineProblem != null ? ": " + engineProblem.Message : "");
        return new(data, displays, audio, recording, engineText, File.Exists(configPath) ? configPath : null);
    }
}
