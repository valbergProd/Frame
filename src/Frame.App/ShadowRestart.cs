using System.Diagnostics;
using System.Windows.Controls;

namespace Frame.App;

// Changing capture settings while Shadow Recording runs (0.5.5). A running FFmpeg pipeline cannot change resolution or frame
// rate, so Shadow Recording restarts: the same coordinator and the same start/stop calls as the stone (no second pipeline), one
// transition at a time. The configured replay length (ReplaySeconds: slider, labels, hotkey label, saved value, rolling buffer)
// is part of the settings and stays exactly as chosen; only footage buffered before the restart is lost, and the status line
// says so. Capture › quality uses it now; Settings › Apply of capture settings can reuse it.
public partial class MainWindow
{
    // Restarts Shadow Recording with `next` (already validated or validated here) and makes it the saved profile.
    // The CALLER HOLDS THE ACTION GATE (call it inside Act), so saves, starts, stops, quits and other changes wait or are
    // refused meanwhile. Steps: stop Shadow (FFmpeg finalizes its segments and is released; the unsaved buffer is discarded as
    // on Stop) → save `next` → start Shadow with it. If the new profile does not start: the previous profile is restored in
    // settings, the saved file and the quality combo, Shadow is started once more with it when that is safe (not a disk
    // problem, not quitting), everything is logged, and the status line tells what happened; FRAME never stays "Recording"
    // unless capture really runs. Returns true when Shadow Recording runs with `next`. `reason` opens the status text
    // ("Quality changed to …."). Manual recording shares Shadow's pipelines, so it must not be running (precondition).
    async Task<bool> RestartShadowWith(Settings next, string reason)
    {
        if (!engine.Replay || engine.State != RecordingState.Idle) throw new InvalidOperationException("Shadow Recording settings can change only while Shadow Recording runs without a manual recording.");
        if (!ready) throw new InvalidOperationException("Capture engine is not ready.");
        next.Validate();
        var previous = CloneSettings(); var watch = Stopwatch.StartNew();
        DiagnosticLog.Write("shadow-restart", $"{reason} {Profile(previous)} → {Profile(next)}; replay length {previous.ReplaySeconds} → {next.ReplaySeconds} s");
        shadowTransition = true; UpdateState();
        try
        {
            Notify(reason + " Restarting Shadow Recording…");
            await engine.SetReplay(false, previous, ChooseEncoder(), []); // FFmpeg "q" → segments closed, processes and files released
            Exception? startError = null;
            try
            {
                SettingsStore.Save(configPath, next); settings = next; SyncPreset(); SyncDuration();
                engine.ResetFault();
                await engine.SetReplay(true, CloneSettings(), ChooseEncoder(), Targets());
            }
            catch (Exception e) { startError = e; }
            if (startError == null)
            {
                MarkRecordingHealthy(); BuildSettings();
                Notify($"{reason} Shadow Recording restarted; replay length stays {DurationText(settings.ReplaySeconds)}, earlier footage was cleared.");
                DiagnosticLog.Write("shadow-restart", $"Shadow Recording runs with {Profile(settings)} after {watch.Elapsed.TotalSeconds:0.0} s; replay length {settings.ReplaySeconds} s");
                return true;
            }
            // The new profile did not start (or could not be saved): back to the previous one everywhere.
            DiagnosticLog.Error("shadow-restart", $"{Profile(next)} did not start; restoring {Profile(previous)}", startError);
            settings = previous;
            try { SettingsStore.Save(configPath, previous); } catch (Exception e) { DiagnosticLog.Error("shadow-restart", "The previous profile could not be saved back", e); }
            SyncPreset(); SyncDuration();
            string why = Brief(startError.Message);
            bool safe = !quitting && !quitRequested && ready && !startError.Message.Contains("disk space", StringComparison.OrdinalIgnoreCase);
            Exception? retryError = null;
            if (safe)
            {
                try { engine.ResetFault(); await engine.SetReplay(true, CloneSettings(), ChooseEncoder(), Targets()); }
                catch (Exception e) { retryError = e; DiagnosticLog.Error("shadow-restart", $"Restart with the previous profile {Profile(previous)} failed too", e); }
            }
            if (safe && retryError == null)
            {
                MarkRecordingHealthy();
                Notify($"{PresetName(next)} could not start ({why}). Shadow Recording continues with {PresetName(previous)}; earlier footage was cleared.");
                DiagnosticLog.Write("shadow-restart", $"Shadow Recording runs again with the previous profile {Profile(previous)}");
            }
            else
            {
                OnStartFailure(retryError ?? startError);
                Notify($"{PresetName(next)} could not start ({why}). Shadow Recording is off; quality stays {PresetName(previous)}. Start it again when ready.");
            }
            return false;
        }
        finally { shadowTransition = false; SyncPreset(); UpdateState(); }
    }
    static string Profile(Settings s) => $"{(s.OutputHeight == 0 ? "native" : s.OutputHeight + "p")}/{s.Fps} fps/{s.BitrateMbps} Mbps/encoder {s.Encoder}";
    static string Brief(string message) { var line = message.Split('\n', 2)[0].Trim(); return line.Length > 140 ? line[..140] + "…" : line; }
    static string DurationText(int seconds) => seconds < 60 ? $"{seconds} seconds" : $"{seconds / 60d:0.##} " + (seconds == 60 ? "minute" : "minutes");
    // Combo names for the three presets (same mapping as SyncPreset), otherwise the resolution and frame rate.
    string PresetName(Settings s) => (s.OutputHeight, s.Fps, s.BitrateMbps) switch
    {
        (1080, 60, 20) => (string)((ComboBoxItem)PresetCombo.Items[0]).Content,
        (0, 60, 20) => (string)((ComboBoxItem)PresetCombo.Items[1]).Content,
        (720, 30, 6) => (string)((ComboBoxItem)PresetCombo.Items[2]).Content,
        _ => $"{(s.OutputHeight == 0 ? "Native" : s.OutputHeight + "p")} / {s.Fps} FPS"
    };
    // Why quality cannot change right now (null = it can). Manual recording shares Shadow's pipelines (a restart would end it);
    // a start/stop/restart or another action (e.g. a save) must finish first. The combo is disabled with this as its tooltip.
    string? PresetBlockedReason() =>
        quitting || quitRequested ? "FRAME is closing."
        : engine.State is RecordingState.Recording or RecordingState.Paused ? "Stop manual recording to change quality; changing it would end the recording."
        : shadowTransition || stopRequested || engine.State is RecordingState.Starting or RecordingState.Finalizing ? "Shadow Recording is starting or stopping; change quality when it has finished."
        : actions.CurrentCount == 0 ? "Another action is running (for example a save); change quality when it has finished."
        : null;
    void UpdatePresetAvailability()
    {
        var blocked = PresetBlockedReason();
        PresetCombo.IsEnabled = blocked == null;
        PresetCombo.ToolTip = blocked ?? (engine.Replay ? "Choose resolution and frame rate. Shadow Recording restarts with the new quality: the replay length is kept, footage recorded so far is cleared." : "Choose resolution and recording frame rate");
    }
}
