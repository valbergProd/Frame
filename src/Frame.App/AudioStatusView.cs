using System.Windows.Threading;

namespace Frame.App;

// 0.5.4: an unavailable audio source no longer stops recording. Video continues, the source is retried in the engine,
// and the user is told once ("Audio interrupted" / "Audio restored") through the existing top-right note, the Capture
// page status line and the tray tooltip. Event-driven: one one-shot timer for the grace period, no polling.
public partial class MainWindow
{
    readonly AudioNoticePolicy audioNotices = new();
    readonly DispatcherTimer audioGrace = new() { Interval = AudioNoticePolicy.Grace };
    IReadOnlyList<AudioSourceState> audioDown = [];
    internal IReadOnlyList<AudioSourceState> AudioDownForTest => audioDown;

    void SetupAudioStatus()
    {
        engine.AudioStatusChanged += () => Dispatcher.InvokeAsync(OnAudioStatus);
        audioGrace.Tick += (_, _) => { audioGrace.Stop(); AnnounceInterruption(); };
    }

    void OnAudioStatus()
    {
        var before = audioDown;
        var now = engine.Busy ? engine.AudioDown : [];
        audioDown = now;
        foreach (var lost in now.Where(n => before.All(b => b.Kind != n.Kind)))
            DiagnosticLog.Warning("audio-interrupted", $"{AudioHealth.Name(lost.Kind)}: {lost.Reason} Recording continues without it; FRAME retries automatically.");
        foreach (var back in before.Where(b => now.All(n => n.Kind != b.Kind)))
            DiagnosticLog.Write("audio-restored", engine.Busy ? $"{AudioHealth.Name(back.Kind)} is recording again." : $"{AudioHealth.Name(back.Kind)}: recording stopped.");
        if (!engine.Busy) { audioGrace.Stop(); audioNotices.Reset(); }
        else if (now.Count > 0) { if (!audioGrace.IsEnabled && !audioNotices.InterruptionShown) audioGrace.Start(); }
        else
        {
            audioGrace.Stop();
            if (audioNotices.ShouldAnnounceRestore()) SaveOverlay.Present("Audio restored", "Recording with audio again", true);
        }
        UpdateState();
    }

    void AnnounceInterruption()
    {
        if (!engine.Busy || audioDown.Count == 0) return;
        var since = audioDown.Min(a => a.DownSince ?? DateTimeOffset.Now);
        if (audioNotices.ShouldAnnounceInterruption(since, DateTimeOffset.Now, true))
        {
            var (heading, detail) = AudioHealth.Interrupted(audioDown.Select(a => a.Kind).ToArray(), engine.AudioSourcesEnabled);
            SaveOverlay.Present(heading, detail, false); // a warning: shown regardless of the save-notification setting
        }
        else if (!audioNotices.InterruptionShown && DateTimeOffset.Now - since < AudioNoticePolicy.Grace) audioGrace.Start();
    }

    // Status line text while audio is degraded (shown under the Shadow controls); empty when audio is fine.
    string AudioStatusText() => audioDown.Count == 0 || !engine.Busy ? ""
        : string.Join(" ", audioDown.Select(a => $"{AudioHealth.Name(a.Kind)} interrupted ({a.Reason?.TrimEnd('.')}).")) + " Recording continues; FRAME reconnects automatically.";
}
