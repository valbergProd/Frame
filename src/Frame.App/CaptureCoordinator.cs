using System.Diagnostics;
using System.Text.Json;

namespace Frame.App;

public sealed class CaptureCoordinator : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1);
    readonly StateMachine machine = new();
    readonly List<CapturePipeline> pipelines = [];
    readonly Dictionary<CapturePipeline, (CaptureTarget[] Sources, ReplayGameHistory History)> games = [];
    public Func<CaptureTarget[], GameObservation>? ObserveReplayGame { get; set; }
    readonly Dictionary<CapturePipeline, List<TimeRange>> ranges = [];
    readonly Dictionary<CapturePipeline, double> open = [];
    readonly string recovery;
    string exe = "";
    Settings settings = new();
    string? sessionRoot;
    bool preserveRecovery;
    internal void PreserveRecovery() => preserveRecovery = true;
    public RecordingState State => machine.State;
    public bool Replay { get; private set; }
    public bool Busy => pipelines.Count > 0;
    public IEnumerable<int> ProcessIds => pipelines.Where(p => p.Running).Select(p => p.ProcessId!.Value);
    internal IReadOnlyList<CapturePipeline> Pipelines => pipelines; // diagnostics
    internal (int ReplaySeconds, int RetainSeconds, int Fps, int OutputHeight, int BitrateMbps) SessionProfile => (settings.ReplaySeconds, retainSeconds, settings.Fps, settings.OutputHeight, settings.BitrateMbps); // diagnostics: what the running session uses
    // Audio sources currently unavailable while video keeps recording (raised on a worker thread; read on the UI thread).
    public event Action? AudioStatusChanged;
    public IReadOnlyList<AudioSourceState> AudioDown
    {
        get
        {
            try { return pipelines.ToArray().SelectMany(p => p.AudioSources).Where(a => !a.Live && a.DownSince != null).GroupBy(a => a.Kind).Select(g => g.OrderBy(a => a.DownSince).First()).ToArray(); }
            catch (InvalidOperationException) { return []; }
        }
    }
    public int AudioSourcesEnabled => pipelines.ToArray().Select(p => p.AudioSourcesEnabled).DefaultIfEmpty(0).Max();
    public double RecordedSeconds => pipelines.Count == 0 ? 0 : ranges.GetValueOrDefault(pipelines[0], []).Sum(r => r.End - r.Start) + (open.TryGetValue(pipelines[0], out var from) ? Math.Max(0, pipelines[0].LiveEdge - from) : 0);
    public double BufferedSeconds => pipelines.Count == 0 ? 0 : pipelines.Min(p => ReplayWindow.Live(p.Segments, settings.ReplaySeconds, p.LiveEdge) is { } w ? w.Range.End - w.Range.Start : 0);
    public string? ReplayUnavailableReason => !Replay ? "Start shadow recording to build history."
        : pipelines.Count == 0 || pipelines.Any(p => p.Frames == 0) ? "Shadow Recording is starting. Clips can be saved as soon as the first frames arrive."
        : pipelines.Any(p => ReplayWindow.Live(p.Segments, settings.ReplaySeconds, p.LiveEdge) == null) ? "History has a gap. Wait for continuous footage before saving." : null;
    public event Action<string>? Notice;
    // Unexpected stops only (never user stops, quits or segment rotation). Raised after the engine state is already Faulted.
    public event Action<RecordingFault>? FaultRaised;
    public RecordingFault? LastFault { get; private set; }
    public event Action? Changed;
    public CaptureCoordinator(string recovery) { this.recovery = recovery; Directory.CreateDirectory(recovery); }
    // Shadow history length can change while recording (0.5.4): saves use the new length at once. The buffer keeps the
    // longest length chosen during this session (within the disk budget), so turning the slider down and up again, or a
    // stray wheel step, never deletes footage that was already recorded. A new session starts from the chosen length.
    int retainSeconds;
    public void SetReplaySeconds(int seconds) { settings.ReplaySeconds = seconds; retainSeconds = Math.Max(retainSeconds, seconds); Changed?.Invoke(); }
    void Move(RecordingState state) { machine.Move(state); Changed?.Invoke(); }
    async Task Ensure(Settings s, Encoder encoder, CaptureTarget[] targets)
    {
        if (pipelines.Count > 0) return;
        var groups = CapturePlan.Groups(targets, s.Combined);
        s.Validate(); settings = s; retainSeconds = s.ReplaySeconds; exe = Backend.Resolve(s.FfmpegPath);
        Directory.CreateDirectory(s.OutputDirectory); CheckDisk();
        sessionRoot = Path.Combine(recovery, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(sessionRoot);
        try
        {
            for (int i = 0; i < groups.Length; i++)
            {
                var pipeline = new CapturePipeline(exe, s, encoder, groups[i], sessionRoot, "Capture-" + (i + 1)); pipelines.Add(pipeline);
                pipeline.AudioChanged += () => AudioStatusChanged?.Invoke();
                if (ObserveReplayGame != null) { var history = new ReplayGameHistory(); history.Add(0, ObserveReplayGame(groups[i])); games[pipeline] = (groups[i], history); }
            }
            await Task.WhenAll(pipelines.Select(p => p.StartAsync()));
        }
        catch { await StopPipelines(); throw; }
    }
    public async Task StartRecording(Settings s, Encoder encoder, CaptureTarget[] targets)
    {
        await gate.WaitAsync(); try
        {
            if (State != RecordingState.Idle) return; Move(RecordingState.Starting);
            try
            {
                bool shared = pipelines.Count > 0; await Ensure(s, encoder, targets); ranges.Clear(); open.Clear();
                foreach (var p in pipelines) { ranges[p] = []; open[p] = shared ? p.LiveEdge : 0; }
                Move(RecordingState.Recording); await Journal();
            }
            catch { Replay = false; Move(RecordingState.Faulted); await StopPipelines(); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task PauseResume()
    {
        await gate.WaitAsync(); try
        {
            if (State == RecordingState.Recording) { CloseRanges(); Move(RecordingState.Paused); }
            else if (State == RecordingState.Paused) { foreach (var p in pipelines) open[p] = p.LiveEdge; Move(RecordingState.Recording); }
            await Journal();
        }
        finally { gate.Release(); }
    }
    void CloseRanges()
    {
        foreach (var p in pipelines) if (open.Remove(p, out var from) && p.LiveEdge is var end && end > from) ranges[p].Add(new(from, end));
    }
    public async Task SetReplay(bool enabled, Settings s, Encoder encoder, CaptureTarget[] targets)
    {
        await gate.WaitAsync(); try
        {
            if (enabled) await Ensure(s, encoder, targets); Replay = enabled;
            if (!enabled && State == RecordingState.Idle) await StopPipelines(true); Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<string>> StopRecording()
    {
        await gate.WaitAsync(); try
        {
            if (State is not (RecordingState.Recording or RecordingState.Paused)) return [];
            CloseRanges(); Move(RecordingState.Finalizing);
            try
            {
                await Journal();
                // Do not make a manual save depend on gfxcapture closing its open segment. A still source never sends
                // the next frame; export the recorded data plus its still tail before releasing the pipelines.
                await WaitForTail();
                foreach (var p in pipelines) p.Refresh();
                var exports = await Task.WhenAll(pipelines.Where(p => ranges[p].Count > 0)
                    .Select(p => MediaExport.PrepareRecording(exe, settings, p, ranges[p], "Recording " + p.Label)));
                // Keep Shadow running, but release manual-only encoders before the full recording export.
                if (!Replay) await Task.WhenAll(pipelines.Select(p => p.DisposeAsync().AsTask()));
                var files = new List<string>();
                foreach (var export in exports) files.Add(await export());
                if (files.Count == 0) throw new IOException("Recording was too short to contain a video frame.");
                ranges.Clear(); open.Clear(); Move(RecordingState.Idle);
                if (!Replay) await StopPipelines(true); else await Journal();
                Notice?.Invoke($"Saved {files.Count} recording{(files.Count > 1 ? "s" : "")}."); return files;
            }
            catch { Move(RecordingState.Faulted); Replay = false; await StopPipelines(); throw; }
        }
        finally { gate.Release(); }
    }
    async Task WaitForTail()
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed.TotalSeconds < 1.5)
        {
            foreach (var p in pipelines) p.Refresh();
            if (pipelines.All(p => !ranges.TryGetValue(p, out var rr) || rr.Count == 0 || (p.Segments.LastOrDefault()?.End ?? 0) >= rr[^1].End)) return;
            await Task.Delay(100);
        }
        // Static screen: the live export path supplies the unfinished tail without waiting for display activity.
    }
    // Live edges captured at the request, before waiting for the command gate (e.g. a Tick in progress).
    public Dictionary<string, double> RequestEdges() => pipelines.ToDictionary(p => p.Label, p => p.LiveEdge);
    public async Task<IReadOnlyList<string>> SaveReplay(Dictionary<string, double>? requestEdges = null)
    {
        await gate.WaitAsync(); try
        {
            if (!Replay) throw new InvalidOperationException("Start shadow recording first.");
            if (pipelines.Any(p => p.Frames == 0)) throw new InvalidOperationException("Shadow Recording is starting; no video yet. Try again in a moment.");
            var files = new List<string>();
            // The clip ends at the moment of the request, including a still screen before it.
            var edges = pipelines.ToDictionary(p => p, p => requestEdges?.GetValueOrDefault(p.Label) ?? p.LiveEdge);
            // The exact-start piece (≤ 1 s of encoding) is prepared during the wait below; the start is already known.
            var prepared = pipelines.ToDictionary(p => p, p => { p.Refresh(); return p.Segments.Count == 0 ? null : MediaExport.PrepareHead(exe, settings, p, Math.Max(p.Segments.Min(s => s.Start), edges[p] - settings.ReplaySeconds)); });
            try {
            var wait = Stopwatch.StartNew(); // with moving content the current one-second segment closes within ~1.2 s
            while (wait.Elapsed.TotalSeconds < 1.5) { foreach (var p in pipelines) p.Refresh(); if (pipelines.All(p => (p.Segments.LastOrDefault()?.End ?? 0) >= edges[p] - .05)) break; await Task.Delay(100); }
            DiagnosticLog.Write("replay-wait", $"segment close wait {wait.Elapsed.TotalSeconds:0.00} s");
            // Validate all sources before exporting any; partial multi-source success must not masquerade as complete.
            var windows = new Dictionary<CapturePipeline, LiveWindow>();
            foreach (var p in pipelines) { p.Refresh(); windows[p] = ReplayWindow.Live(p.Segments, settings.ReplaySeconds, edges[p]) ?? throw new InvalidOperationException("Replay is warming up or has a gap. Wait for continuous history, then try again."); }
            foreach (var p in pipelines)
            {
                var window = windows[p];
                string? folder = games.TryGetValue(p, out var game) ? game.History.Classify(window.Range) : null;
                string verified = await MediaExport.ExportReplay(exe, settings, p, window, "Replay " + p.Label, prepared[p]); // stream copy, full re-encode as fallback
                files.Add(ReplayFolders.RouteVerified(settings.OutputDirectory, verified, folder));
            }
            Notice?.Invoke($"Saved {files.Count} replay{(files.Count > 1 ? "s" : "")}."); return files;
            }
            finally { foreach (var head in prepared.Values) await MediaExport.Discard(head); } // unused when a window check failed
        }
        finally { gate.Release(); }
    }
    readonly HashSet<CapturePipeline> idleNoted = [];
    public event Action<string>? IdleNoticed;
    public async Task Tick()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            if (!Busy) return; CheckDisk();
            foreach (var p in pipelines)
            {
                if (!p.Running || p.AudioFailed) { await Fail(new FaultSignals(p.StorageFailed, p.AudioDeviceFailed, false, p.ExitCode, p.Error)); return; }
                // No "no frames for N seconds" fault: gfxcapture only emits frames when the screen changes (static desktop,
                // lock screen, display sleep). The fps filter fills the gap when frames resume; real failures end the process above.
                if (p.IdleProgressSeconds > 15 && !idleNoted.Contains(p)) { idleNoted.Add(p); IdleNoticed?.Invoke(p.Label); } else if (p.IdleProgressSeconds < 1) idleNoted.Remove(p);
                p.Refresh();
                if (ObserveReplayGame != null && games.TryGetValue(p, out var game)) game.History.Add(p.Position, ObserveReplayGame(game.Sources));
                double? pin = ranges.TryGetValue(p, out var r) && r.Count > 0 ? r[0].Start : open.TryGetValue(p, out var start) ? start : null;
                foreach (var segment in ReplayLogic.Trim(p.Segments, Replay ? Math.Max(settings.ReplaySeconds, retainSeconds) + 2 : 2, (long)settings.ReplayBudgetMb * 1024 * 1024 / pipelines.Count, pin)) { File.Delete(segment.Path); p.Segments.Remove(segment); }
            }
        }
        catch (Exception e) { await Fail(new FaultSignals(e.Message.Contains("disk space"), false, false, null, e.Message)); }
        finally { gate.Release(); }
    }
    async Task Fail(FaultSignals signals)
    {
        bool shadow = Replay; string details = string.Join(Environment.NewLine, pipelines.Select(p => p.Label + ": " + p.Error).Prepend(signals.ErrorText).Distinct());
        Replay = false; Move(RecordingState.Faulted); await StopPipelines();
        LastFault = RecordingHealth.Classify(signals with { ErrorText = details }, shadow, DateTimeOffset.Now);
        Notice?.Invoke(LastFault.Explanation + " Recovery data is available in Settings."); // in-window status text (FRAME Advanced, now a separate repository, used it too)
        FaultRaised?.Invoke(LastFault);
    }
    void CheckDisk()
    {
        foreach (var path in new[] { settings.OutputDirectory, recovery })
            if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace < 512L * 1024 * 1024) throw new IOException("Less than 512 MB of disk space remains. Capture stopped to protect existing files.");
    }
    Task Journal() => sessionRoot == null ? Task.CompletedTask : File.WriteAllTextAsync(Path.Combine(sessionRoot, "recording.json"), JsonSerializer.Serialize(new { State, Replay, ranges = ranges.Select(p => new { p.Key.Label, Ranges = p.Value, Open = open.GetValueOrDefault(p.Key, -1) }) }));
    async Task StopPipelines(bool clean = false)
    {
        foreach (var p in pipelines) await p.DisposeAsync(); pipelines.Clear(); games.Clear(); AudioStatusChanged?.Invoke();
        if (clean && !preserveRecovery && sessionRoot != null)
        {
            string root = Path.GetFullPath(recovery).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(sessionRoot);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Recovery directory is outside the expected location.");
            try { Directory.Delete(target, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notice?.Invoke("Capture stopped. Some temporary files could not be removed; review Recovery files in Settings."); }
            sessionRoot = null;
        }
    }
    public void ResetFault() { if (State == RecordingState.Faulted) Move(RecordingState.Idle); }
    public async ValueTask DisposeAsync()
    {
        try { if (State is RecordingState.Recording or RecordingState.Paused) await StopRecording(); }
        finally { await gate.WaitAsync(); try { Replay = false; await StopPipelines(State != RecordingState.Faulted); } finally { gate.Release(); } }
    }
}
