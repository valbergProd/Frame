using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using D = System.Drawing;

namespace Frame.App;

// Recording health: what happens after an unexpected stop, the in-app explanation panel, persisted status and tray state.
// Everything here is event-driven (engine fault, state changes); nothing polls or animates.
public partial class MainWindow
{
    sealed record StatusFile(RecordingFault? Fault, bool Resolved, bool Dismissed, bool PreviousSession);
    RecordingFault? fault;
    bool faultResolved, faultDismissed, faultFromPreviousSession;
    readonly Queue<DateTimeOffset> autoRecoveries = new();
    string StatusPath => Path.Combine(data, "recording-status.json");

    void LoadRecordingStatus()
    {
        try
        {
            if (!File.Exists(StatusPath)) return;
            var saved = JsonSerializer.Deserialize<StatusFile>(File.ReadAllText(StatusPath));
            // An unresolved stop from an earlier run is shown as "last session", never as the current state.
            if (saved?.Fault != null && !saved.Dismissed && !saved.Resolved) { fault = saved.Fault; faultFromPreviousSession = true; }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
    }
    void SaveRecordingStatus()
    {
        try { File.WriteAllText(StatusPath, JsonSerializer.Serialize(new StatusFile(fault, faultResolved, faultDismissed, faultFromPreviousSession))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Engine reported an unexpected stop (state is already Faulted). Retry once when it can help, then tell the user the outcome.
    async void OnFault(RecordingFault f)
    {
        if (quitRequested || quitting) return;
        fault = f; faultResolved = faultDismissed = faultFromPreviousSession = false;
        DiagnosticLog.Error("recording-fault", $"{f.Kind} · {f.Explanation}{Environment.NewLine}{f.Details}");
        SaveRecordingStatus(); UpdateState();
        while (autoRecoveries.Count > 0 && DateTimeOffset.Now - autoRecoveries.Peek() > TimeSpan.FromHours(1)) autoRecoveries.Dequeue();
        if (f.Shadow && f.Retryable && ready && autoRecoveries.Count < 3)
        {
            autoRecoveries.Enqueue(DateTimeOffset.Now);
            await Task.Delay(3000);
            bool ok = await TryRestartShadow();
            fault = f with { AutoRetryTried = true, Recovered = ok, RecoveredAt = ok ? DateTimeOffset.Now : null };
            faultResolved = ok; SaveRecordingStatus(); UpdateState();
            DiagnosticLog.Write("recording-recovery", ok ? "Shadow Recording restarted automatically" : "Automatic restart failed", ok ? LogLevel.Info : LogLevel.Error);
            if (ok) { if (settings.ShowSaveNotifications) SaveOverlay.Present("Shadow Recording resumed", "Recovered after an interruption", true); return; }
        }
        SaveOverlay.Present(f.Shadow ? "Shadow Recording stopped" : "Recording stopped", "Open FRAME for details", false);
    }
    async Task<bool> TryRestartShadow()
    {
        if (quitRequested || quitting) return false;
        if (!await actions.WaitAsync(TimeSpan.FromSeconds(10))) return false;
        try
        {
            if (quitRequested || quitting) return false;
            if (engine.Replay) return true;
            engine.ResetFault(); shadowTransition = true; UpdateState();
            await engine.SetReplay(true, CloneSettings(), ChooseEncoder(), Targets());
            return true;
        }
        catch (Exception e) { DiagnosticLog.Error("recording-recovery", "Automatic restart failed", e); return false; }
        finally { shadowTransition = false; actions.Release(); UpdateState(); }
    }
    // A start that failed (including the automatic start after sign-in) is explained the same way, without a retry loop.
    void OnStartFailure(Exception e)
    {
        var f = RecordingHealth.Classify(new FaultSignals(e.Message.Contains("disk space"), false, true, null, e.Message), true, DateTimeOffset.Now);
        fault = f; faultResolved = faultDismissed = faultFromPreviousSession = false;
        DiagnosticLog.Error("recording-start-failed", "Shadow Recording did not start: " + e.Message, e); SaveRecordingStatus();
        SaveOverlay.Present("Shadow Recording did not start", "Open FRAME for details", false);
    }
    void MarkRecordingHealthy()
    {
        if (fault == null || faultResolved) return;
        faultResolved = true; fault = fault with { Recovered = true, RecoveredAt = DateTimeOffset.Now }; SaveRecordingStatus();
    }

    // Quiet panel on the Capture page: summary first, technical details collapsed. Hidden when there is nothing to report.
    void UpdateFailurePanel()
    {
        if (fault == null || faultDismissed) { FailurePanel.Visibility = Visibility.Collapsed; return; }
        FailurePanel.Visibility = Visibility.Visible;
        var f = fault; bool active = !faultResolved && !engine.Replay;
        var key = (f.At, f.AutoRetryTried, f.Recovered, faultResolved, active, faultFromPreviousSession, engine.Busy, ready);
        if (Equals(FailurePanel.Tag, key)) return; FailurePanel.Tag = key;
        var ember = new SolidColorBrush(Color.FromRgb(214, 110, 70));
        FailurePanel.BorderBrush = faultResolved ? (Brush)FindResource("Line") : new SolidColorBrush(Color.FromArgb(120, 214, 110, 70));
        var body = new StackPanel();
        string heading = faultResolved ? (f.AutoRetryTried && f.RecoveredAt != null ? "Recovered automatically" : "Resolved") : faultFromPreviousSession ? "Last session: " + f.Title : f.Title;
        body.Children.Add(new TextBlock { Text = heading, FontSize = 14, Foreground = faultResolved ? (Brush)FindResource("Text") : ember });
        body.Children.Add(new TextBlock { Text = f.Explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (!faultResolved) body.Children.Add(new TextBlock { Text = f.Suggestion, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        string retry = f.AutoRetryTried ? (f.Recovered ? $" · restarted automatically {f.RecoveredAt:HH:mm}" : " · automatic restart failed") : "";
        body.Children.Add(new TextBlock { Text = $"{f.At:g}{retry}" + (faultResolved && f.RecoveredAt != null && !f.AutoRetryTried ? $" · restarted {f.RecoveredAt:HH:mm}" : ""), FontSize = 10, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 6, 0, 0) });
        var actionsRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (active && ready)
        {
            var restart = ActionButton("Restart Shadow Recording", async () => await Act(ToggleShadow)); restart.SetResourceReference(BorderBrushProperty, "Accent");
            TooltipPolish.Describe(restart, "Start buffering again with your settings"); actionsRow.Children.Add(restart);
        }
        string report = $"FRAME {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · {Environment.OSVersion.VersionString}{Environment.NewLine}{f.At:O} · {f.Kind} · {f.Explanation}{Environment.NewLine}{f.Details}{Environment.NewLine}Logs: Settings › Advanced › Open log folder (Export diagnostics creates a ZIP for a bug report)";
        var copy = ActionButton("Copy details", () => { Clipboard.SetText(report); Notify("Diagnostic details copied."); }); TooltipPolish.Describe(copy, "Copy a short report for a bug report"); actionsRow.Children.Add(copy);
        var dismiss = ActionButton("Dismiss", () => { faultDismissed = true; SaveRecordingStatus(); UpdateFailurePanel(); }); TooltipPolish.Describe(dismiss, "Hide this message; the log keeps it"); actionsRow.Children.Add(dismiss);
        body.Children.Add(actionsRow);
        var technical = new Expander { Header = "Technical details", Margin = new Thickness(0, 10, 0, 0), FontSize = 12 };
        technical.SetResourceReference(StyleProperty, "QuietExpander"); technical.SetResourceReference(ForegroundProperty, "Text");
        technical.Content = new TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(technical);
        FailurePanel.Child = body;
    }

    // Tray: gold mark plus a tiny state badge; icons are drawn once and swapped only when the state changes.
    enum TrayState { Idle, Starting, Active, Error }
    TrayState? trayState;
    readonly Dictionary<TrayState, D.Icon> trayIcons = [];
    TrayState ComputeTrayState()
    {
        bool error = fault != null && !faultResolved && !faultFromPreviousSession && !engine.Replay && engine.State is not (RecordingState.Recording or RecordingState.Paused);
        return error ? TrayState.Error : shadowTransition && !engine.Replay ? TrayState.Starting : engine.Replay || engine.State is RecordingState.Recording or RecordingState.Paused ? TrayState.Active : TrayState.Idle;
    }
    string TrayTextFor(TrayState state) => state switch
    {
        TrayState.Active => (engine.Replay ? "FRAME · Shadow Recording active" : "FRAME · Recording") + (audioDown.Count > 0 ? " – audio interrupted" : ""),
        TrayState.Starting => "FRAME · Starting Shadow Recording…",
        TrayState.Error => "FRAME · Recording stopped – open for details",
        _ => "FRAME · Shadow Recording off"
    };
    void UpdateTray()
    {
        if (tray == null) return;
        var state = ComputeTrayState();
        tray.Text = TrayTextFor(state);
        if (trayState == state) return;
        trayState = state;
        if (!trayIcons.TryGetValue(state, out var icon)) trayIcons[state] = icon = TrayIcon(state);
        tray.Icon = icon;
    }
    internal static D.Icon TrayIcon(TrayStateKind kind) => TrayIcon((TrayState)(int)kind);
    internal enum TrayStateKind { Idle, Starting, Active, Error }
    static D.Icon TrayIcon(TrayState state)
    {
        using var baseIcon = CreateIcon(); using var bitmap = baseIcon.ToBitmap();
        if (state != TrayState.Idle)
        {
            using var g = D.Graphics.FromImage(bitmap); g.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
            float s = bitmap.Width, r = s * .24f, cx = s - r - .5f, cy = s - r - .5f;
            using var shade = new D.SolidBrush(D.Color.FromArgb(230, 23, 19, 16)); g.FillEllipse(shade, cx - r - 1, cy - r - 1, 2 * r + 2, 2 * r + 2);
            if (state == TrayState.Active) { using var dot = new D.SolidBrush(D.Color.FromArgb(232, 84, 60)); g.FillEllipse(dot, cx - r * .8f, cy - r * .8f, r * 1.6f, r * 1.6f); }
            else if (state == TrayState.Starting) { using var ring = new D.Pen(D.Color.FromArgb(229, 172, 82), Math.Max(1, s / 16)); g.DrawEllipse(ring, cx - r * .7f, cy - r * .7f, r * 1.4f, r * 1.4f); }
            else { using var warn = new D.SolidBrush(D.Color.FromArgb(214, 110, 70)); g.FillPolygon(warn, [new D.PointF(cx, cy - r), new D.PointF(cx + r, cy + r * .8f), new D.PointF(cx - r, cy + r * .8f)]); }
        }
        var handle = bitmap.GetHicon();
        try { return (D.Icon)D.Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    internal string? TrayText => tray?.Text;
    internal string TrayStateName => (trayState ?? ComputeTrayState()).ToString();
    internal string ExpectedTrayText => TrayTextFor(ComputeTrayState());
}
