using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Frame.App;

// Native WASAPI audio for the capture pipeline; no external audio dependency.
// 0.5.4 audio reliability design (root cause: FFmpeg stops reading the audio pipe while the video stream does not
// advance, i.e. on a still screen, because its scheduler throttles inputs that run ahead of the slowest output):
//  - Producer (wall-clock, 20 ms chunks) drains every source and never waits for FFmpeg; missing data = silence.
//  - Writer feeds the pipe from an AudioSpool and may wait as long as FFmpeg is not reading; that is not an error.
//    At most 30 s of real audio is kept meanwhile; older audio becomes silence, so the timeline (A/V sync) is exact.
//  - A source that fails (device removed, endpoint invalidated, audio service restart, cannot start) is restarted with
//    backoff while silence is mixed in its place; video and the FFmpeg process keep running. Default-device changes
//    (IMMNotificationClient, event-driven) switch sources that follow the Windows default.
//  - Failure is only set for a broken pipeline (FFmpeg closed the input); CapturePipeline additionally detects FFmpeg
//    not consuming audio while audio is already behind the video (AudioHealth.InputBroken).
// 0.5.4 A/V sync: the timeline starts at the capture's origin (taken before FFmpeg starts, shared with the video), not at
// the pipe connection; the time before the connection is timed silence. Audio waiting in a source's buffer is latency:
// a source that (re)starts keeps only what is due now, and a running source keeps at most LatencyCap beyond what is due
// (device clocks that run slightly fast against the system clock can otherwise build up to the 1 s ring over hours).
public sealed record AudioSourceState(AudioSourceKind Kind, bool Live, string? Reason, DateTimeOffset? DownSince);

public sealed class AudioPipe : IAsyncDisposable
{
    const int ChunkSamples = 1920, ChunkBytes = ChunkSamples * 4; // 20 ms of 48 kHz stereo float32
    const int MaxBacklogChunks = 1500;                            // 30 s of real audio while FFmpeg is not reading
    const int LatencyCap = 2 * 2400;                              // 50 ms of stereo samples waiting beyond what is due
    static readonly byte[] Zeros = new byte[ChunkBytes * 25];     // 0.5 s of silence per write
    readonly NamedPipeServerStream pipe;
    readonly CancellationTokenSource stop = new();
    readonly List<Slot> slots = [];
    readonly AudioSpool<byte[]> spool = new(MaxBacklogChunks);
    readonly SemaphoreSlim queued = new(0);
    readonly object sync = new();
    // A bounded mirror of the last 30 s, on the existing audio timeline. Manual Stop can retain the PCM still waiting
    // for gfxcapture/FFmpeg on a static screen. This does not change mixing, devices, timing or pipe delivery.
    readonly byte[] recent = new byte[ChunkBytes * MaxBacklogChunks];
    long recentChunks;
    readonly Stopwatch clock = new();
    readonly long origin; // Stopwatch timestamp of the shared A/V origin: audio time 0
    Task? producer, writer;
    EndpointWatcher? watcher;
    volatile bool renderDefaultChanged, captureDefaultChanged, devicesChanged;
    long writeStarted; // Stopwatch ticks of a pending write, 0 when idle
    Task? disposal;
    volatile bool stopping;
    public string PipePath { get; }
    // Broken pipeline only (FFmpeg closed the audio input). Source problems are degradations, see Sources.
    public Exception? Failure { get; private set; }
    public event Action? StatusChanged;
    // Diagnostics/tests only: replaces WASAPI with another source (e.g. synthetic). Null in normal use.
    internal static Func<AudioSourceKind, string?, IAudioSource>? SourceFactory { get; set; }
    // Instrumentation (cheap counters for diagnostics and the health check).
    public double LongestWriteMs { get; private set; }
    public long ChunksWritten => Interlocked.Read(ref written);
    public long ChunksProduced => Interlocked.Read(ref produced);
    public long BacklogChunks { get { lock (sync) return spool.PendingChunks; } }
    public long EvictedChunks { get { lock (sync) return spool.EvictedChunks; } }
    public int RealBacklogChunks { get { lock (sync) return spool.RealChunks; } }
    public long Glitches { get; private set; }
    public int Interruptions { get; private set; }
    public int Recoveries { get; private set; }
    public double AcceptedSeconds => ChunksWritten * .02;
    public double? ConnectedAfterSeconds { get; private set; } // origin -> FFmpeg opened the pipe (filled with silence)
    public long TrimmedSamples => Interlocked.Read(ref trimmed);
    long trimmed;
    double SinceOrigin => (Stopwatch.GetTimestamp() - origin) / (double)Stopwatch.Frequency;
    public double WriteBlockedSeconds { get { long t = Interlocked.Read(ref writeStarted); return t == 0 ? 0 : (clock.ElapsedTicks - t) / (double)Stopwatch.Frequency; } }
    long written, produced;

    sealed class Slot(AudioSourceKind kind, string? deviceId)
    {
        public readonly AudioSourceKind Kind = kind; public readonly string? DeviceId = deviceId;
        public IAudioSource? Source; public Task? Starting; public IAudioSource? Candidate;
        public bool Live, EverLive, Fresh; public string? Reason; public DateTimeOffset? DownSince;
        public int Attempts; public long NextAttempt; public bool SwitchRequested;
        public bool FollowsDefault => string.IsNullOrEmpty(DeviceId);
    }

    public AudioPipe(Settings settings, long origin)
    {
        this.origin = origin;
        string name = "FrameAudio-" + Guid.NewGuid().ToString("N"); PipePath = @"\\.\pipe\" + name;
        pipe = new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 38400);
        if (settings.DesktopAudio) slots.Add(new(AudioSourceKind.Desktop, settings.OutputDevice));
        if (settings.Microphone) slots.Add(new(AudioSourceKind.Microphone, settings.InputDevice));
    }
    public int EnabledSources => slots.Count;
    public AudioSourceState[] Sources { get { lock (sync) return slots.Select(s => new AudioSourceState(s.Kind, s.Live, s.Reason, s.DownSince)).ToArray(); } }

    public Task ConnectAsync()
    {
        producer = Task.Run(Produce);
        writer = Task.Run(Write);
        return Task.WhenAll(producer, writer);
    }

    static IAudioSource Create(AudioSourceKind kind, string? deviceId) => SourceFactory?.Invoke(kind, deviceId) ?? new WasapiSource(kind == AudioSourceKind.Desktop, deviceId);

    async Task Produce()
    {
        try
        {
            await pipe.WaitForConnectionAsync(stop.Token);
            clock.Start();
            // Everything between the origin and now is silence on the shared timeline (no source is open yet).
            ConnectedAfterSeconds = SinceOrigin;
            long lead = (long)(SinceOrigin * 1000) / 20;
            if (lead > 0) { lock (sync) { spool.EnqueueSilence(lead); Remember(null, lead); } Interlocked.Add(ref produced, lead); queued.Release(); }
            if (SourceFactory == null)
                try { watcher = new EndpointWatcher(); watcher.DefaultChanged += flow => { if (flow == 0) renderDefaultChanged = true; else captureDefaultChanged = true; }; watcher.DevicesChanged += () => devicesChanged = true; }
                catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException) { watcher = null; } // default switching then relies on endpoint errors
            float[] mix = new float[ChunkSamples];
            while (!stop.IsCancellationRequested)
            {
                Service();
                long elapsedMs = (long)(SinceOrigin * 1000);
                long due = elapsedMs / 20 + 1 - Interlocked.Read(ref produced);
                // Starved for more than 2 s (e.g. the PC stalled): the devices' 1 s buffers are gone anyway. Keep the
                // timeline with silence instead of failing, and count it.
                if (due > 100) { long skip = due - 50; lock (sync) { spool.EnqueueSilence(skip); Remember(null, skip); } Interlocked.Add(ref produced, skip); queued.Release(); Glitches++; due = 50; }
                // Keep sources current: a fresh source starts at "now", a running one may lag at most LatencyCap.
                lock (sync) foreach (var s in slots) if (s.Live && s.Source is { Failure: null } src)
                {
                    Interlocked.Add(ref trimmed, src.Limit((int)(Math.Max(0, due) * ChunkSamples) + (s.Fresh ? 0 : LatencyCap)));
                    s.Fresh = false;
                }
                for (; due > 0; due--)
                {
                    Array.Clear(mix);
                    lock (sync) foreach (var s in slots) if (s.Live && s.Source is { Failure: null } src) src.Mix(mix);
                    for (int i = 0; i < mix.Length; i++) mix[i] = Math.Clamp(mix[i], -1, 1);
                    var chunk = ArrayPool<byte>.Shared.Rent(ChunkBytes);
                    Buffer.BlockCopy(mix, 0, chunk, 0, ChunkBytes);
                    byte[]? evicted; lock (sync) { evicted = spool.Enqueue(chunk); Remember(chunk, 1); }
                    if (evicted != null) ArrayPool<byte>.Shared.Return(evicted);
                    Interlocked.Increment(ref produced); queued.Release();
                }
                long next = (Interlocked.Read(ref produced)) * 20 - (long)(SinceOrigin * 1000);
                await Task.Delay((int)Math.Clamp(next, 1, 20), stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (!stop.IsCancellationRequested && !stopping) { Failure ??= new IOException("The audio input stopped: " + e.Message, e); }
    }

    // Caller holds sync; large gaps clear at most the bounded history, never allocate per elapsed second.
    void Remember(byte[]? chunk, long count)
    {
        long end = recentChunks + count;
        for (long i = Math.Max(recentChunks, end - MaxBacklogChunks); i < end; i++)
        {
            int at = (int)(i % MaxBacklogChunks) * ChunkBytes;
            if (chunk == null) Array.Clear(recent, at, ChunkBytes);
            else Buffer.BlockCopy(chunk, 0, recent, at, ChunkBytes);
        }
        recentChunks = end;
    }
    internal sealed record Snapshot(string Path, double Start, double End);
    internal async Task<Snapshot?> SnapshotAsync(string path)
    {
        byte[] copy; long first, end;
        lock (sync)
        {
            end = recentChunks; first = Math.Max(0, end - MaxBacklogChunks);
            if (end == 0) return null;
            copy = new byte[(int)(end - first) * ChunkBytes];
            for (long i = first; i < end; i++) Buffer.BlockCopy(recent, (int)(i % MaxBacklogChunks) * ChunkBytes, copy, (int)(i - first) * ChunkBytes, ChunkBytes);
        }
        await File.WriteAllBytesAsync(path, copy);
        return new(path, first * .02, end * .02);
    }

    // Source lifecycle, run on the producer every 20 ms: cheap field checks; starts happen in the background.
    void Service()
    {
        bool render = renderDefaultChanged, capture = captureDefaultChanged, devices = devicesChanged;
        if (render) renderDefaultChanged = false; if (capture) captureDefaultChanged = false; if (devices) devicesChanged = false;
        long now = clock.ElapsedMilliseconds; bool changed = false;
        foreach (var s in slots)
        {
            if (s.FollowsDefault && ((s.Kind == AudioSourceKind.Desktop && render) || (s.Kind == AudioSourceKind.Microphone && capture))) s.SwitchRequested = true;
            if (devices && !s.Live) s.NextAttempt = now; // a device appeared or changed state: try again at once
            // A running source failed: degrade to silence for this source, restart with backoff.
            if (s.Live && s.Source?.Failure is { } failure)
            {
                lock (sync) { s.Live = false; s.Reason = Describe(failure); s.DownSince = DateTimeOffset.Now; }
                Retire(s.Source); s.Source = null; s.Attempts = 0; s.NextAttempt = now + (long)AudioHealth.RetryDelay(0).TotalMilliseconds;
                Interruptions++; changed = true;
            }
            // Default device changed: start the new default first, then swap (no outage note for a quick switch).
            if (s.SwitchRequested && s.Starting == null) { s.SwitchRequested = false; if (s.Live || s.Source != null) BeginStart(s); else s.NextAttempt = now; }
            if (s.Starting == null && s.Source == null && now >= s.NextAttempt) BeginStart(s);
            if (s.Starting is { IsCompleted: true } started)
            {
                var candidate = s.Candidate!; s.Starting = null; s.Candidate = null;
                if (started.IsCompletedSuccessfully && candidate.Failure == null)
                {
                    var old = s.Source; bool wasDown = !s.Live && s.EverLive;
                    lock (sync) { s.Source = candidate; s.Live = true; s.Fresh = true; s.EverLive = true; s.Reason = null; s.DownSince = null; }
                    if (old != null && !ReferenceEquals(old, candidate)) Retire(old);
                    s.Attempts = 0; if (wasDown) Recoveries++; changed = true;
                }
                else
                {
                    Retire(candidate);
                    var error = started.Exception?.GetBaseException() ?? candidate.Failure ?? new IOException("The audio device did not start.");
                    if (s.Source == null)
                    {
                        bool first = s.DownSince == null;
                        lock (sync) { s.Live = false; s.Reason = Describe(error); s.DownSince ??= DateTimeOffset.Now; }
                        if (first) { Interruptions++; changed = true; }
                        s.Attempts++; s.NextAttempt = now + (long)AudioHealth.RetryDelay(s.Attempts).TotalMilliseconds;
                    }
                }
            }
        }
        if (changed) StatusChanged?.Invoke();
    }
    void BeginStart(Slot s)
    {
        var candidate = Create(s.Kind, s.DeviceId); s.Candidate = candidate;
        s.Starting = Task.Run(candidate.Start);
    }
    static void Retire(IAudioSource? source) { if (source != null) _ = Task.Run(async () => { try { await source.DisposeAsync(); } catch { } }); }
    static string Describe(Exception e) => e switch
    {
        COMException c when (uint)c.HResult == 0x88890004 => "The audio device was removed or changed (AUDCLNT_E_DEVICE_INVALIDATED).",
        COMException c when (uint)c.HResult == 0x80070490 => "No audio device is available.",
        COMException c when (uint)c.HResult == 0x88890010 => "The Windows audio service is not running.",
        COMException c when (uint)c.HResult == 0x80070005 => "Windows denied access to the device (check microphone privacy settings).",
        TimeoutException => "The audio device did not respond.",
        _ => e.Message
    };

    async Task Write()
    {
        try
        {
            await pipe.WaitForConnectionAsync(stop.Token);
            while (!stop.IsCancellationRequested)
            {
                await queued.WaitAsync(stop.Token);
                byte[]? chunk; long silent; bool got;
                lock (sync) got = spool.TryDequeue(out chunk, out silent);
                if (!got) continue;
                if (chunk != null)
                {
                    try { await Timed(chunk, ChunkBytes); } finally { ArrayPool<byte>.Shared.Return(chunk); }
                    Interlocked.Increment(ref written);
                }
                for (long left = silent; left > 0;)
                {
                    int n = (int)Math.Min(left, Zeros.Length / ChunkBytes);
                    await Timed(Zeros, n * ChunkBytes); Interlocked.Add(ref written, n); left -= n;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (!stop.IsCancellationRequested && !stopping) { Failure ??= new IOException("FFmpeg closed the audio input: " + e.Message, e); }
    }
    async Task Timed(byte[] data, int count)
    {
        long before = clock.ElapsedTicks; Interlocked.Exchange(ref writeStarted, Math.Max(1, before));
        try { await pipe.WriteAsync(data.AsMemory(0, count), stop.Token); }
        finally { Interlocked.Exchange(ref writeStarted, 0); LongestWriteMs = Math.Max(LongestWriteMs, (clock.ElapsedTicks - before) * 1000d / Stopwatch.Frequency); }
    }

    // Diagnostics: same path as a real Windows default-device notification (tests must not change the user's devices).
    internal void SimulateDefaultChange(AudioSourceKind kind) { if (kind == AudioSourceKind.Desktop) renderDefaultChanged = true; else captureDefaultChanged = true; }
    internal bool WatcherActive => watcher != null;
    internal (long Dropped, long Discontinuities) DeviceGlitches { get { lock (sync) return (slots.Sum(s => (s.Source as WasapiSource)?.Dropped ?? 0), slots.Sum(s => (s.Source as WasapiSource)?.Discontinuities ?? 0)); } }

    // Called before FFmpeg is asked to finish: from now on a closed pipe is expected, not a failure.
    public void Stopping() => stopping = true;

    public ValueTask DisposeAsync()
    {
        // Device/COM cleanup must not block the UI thread which owns the shutdown deadline. Every caller awaits
        // the same cleanup, even when a preceding capture-stop call already timed out waiting for it.
        lock (sync) return new(disposal ??= Task.Run(DisposeCore));
    }
    async Task DisposeCore()
    {
        stopping = true; stop.Cancel(); pipe.Dispose();
        foreach (var t in new[] { producer, writer }) if (t != null) try { await t; } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
        watcher?.Dispose();
        foreach (var s in slots)
        {
            if (s.Starting != null) try { await s.Starting; } catch { }
            foreach (var src in new[] { s.Source, s.Candidate }) if (src != null) try { await src.DisposeAsync(); } catch { }
        }
        lock (sync) while (spool.TryDequeue(out var chunk, out _)) if (chunk != null) ArrayPool<byte>.Shared.Return(chunk);
        stop.Dispose();
    }
}

// One audio input mixed into the pipe. Mix adds up to output.Length interleaved stereo 48 kHz samples (missing = silence).
internal interface IAudioSource : IAsyncDisposable
{
    Task Start();
    void Mix(float[] output);
    // Drops the oldest waiting samples beyond maxSamples (interleaved stereo); returns how many were dropped.
    int Limit(int maxSamples);
    Exception? Failure { get; }
}

// Diagnostics/tests: deterministic 48 kHz stereo source paced by the wall clock (like a real endpoint), no hardware.
// Silence unless a tone burst is scheduled; Fail() simulates a device error, FailStart makes Start() throw.
internal sealed class SyntheticAudioSource : IAudioSource
{
    readonly Stopwatch clock = new();
    readonly object sync = new();
    readonly List<(long From, long To, double Hz)> bursts = [];
    long produced; // stereo frames handed out
    public Exception? Failure { get; private set; }
    public Exception? FailStart { get; init; }
    public long Overflows { get; private set; }
    public bool Disposed { get; private set; }
    public Task? DisposeWait { get; init; } // diagnostic: a device which never completes its cleanup
    public AudioSourceKind Kind { get; init; }
    public double Amplitude { get; init; } = .3; // tone peak (full scale = 1); the level test (--capture-ui-test) uses known levels
    public Task Start() { if (FailStart != null) throw FailStart; clock.Start(); return Task.CompletedTask; }
    public void Fail(Exception e) => Failure = e;
    // Tone from "now" (source clock) for the given duration; sample-exact, so A/V tests can align it with a flash.
    public void Beep(double seconds, double hz = 1000) { lock (sync) { long from = (long)(clock.Elapsed.TotalSeconds * 48000); bursts.Add((from, from + (long)(seconds * 48000), hz)); } }
    // Like WASAPI with FRAME's 1 s ring: frames become available with real time; more than 1 s unread drops the oldest.
    public void Mix(float[] output)
    {
        lock (sync)
        {
            long available = (long)(clock.Elapsed.TotalSeconds * 48000) - produced;
            if (available > 48000) { Overflows++; produced += available - 48000; available = 48000; }
            long frames = Math.Min(available, output.Length / 2);
            for (int f = 0; f < frames; f++)
            {
                long index = produced + f; float v = 0;
                foreach (var b in bursts) if (index >= b.From && index < b.To) v = (float)(Math.Sin(2 * Math.PI * b.Hz * index / 48000) * Amplitude);
                output[2 * f] += v; output[2 * f + 1] += v;
            }
            produced += Math.Max(0, frames);
        }
    }
    public int Limit(int maxSamples)
    {
        lock (sync)
        {
            long excess = (long)(clock.Elapsed.TotalSeconds * 48000) - produced - maxSamples / 2;
            if (excess <= 0) return 0;
            produced += excess; return (int)excess * 2;
        }
    }
    public ValueTask DisposeAsync() { Disposed = true; return DisposeWait == null ? ValueTask.CompletedTask : new(DisposeWait); }
}

// Default-device and device-state notifications (IMMNotificationClient). Callbacks arrive on a Windows audio thread and
// only set flags; the producer acts on them. No polling.
internal sealed class EndpointWatcher : IDisposable
{
    readonly IMMDeviceEnumeratorEx enumerator;
    readonly Callback callback;
    public event Action<int>? DefaultChanged; // 0 = render (desktop audio), 1 = capture (microphone)
    public event Action? DevicesChanged;
    public EndpointWatcher()
    {
        enumerator = (IMMDeviceEnumeratorEx)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        callback = new Callback(this);
        Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(callback));
    }
    public void Dispose() { try { enumerator.UnregisterEndpointNotificationCallback(callback); } catch (COMException) { } Marshal.ReleaseComObject(enumerator); }
    [ComVisible(true)]
    sealed class Callback(EndpointWatcher owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState) => owner.DevicesChanged?.Invoke();
        public void OnDeviceAdded(string deviceId) => owner.DevicesChanged?.Invoke();
        public void OnDeviceRemoved(string deviceId) => owner.DevicesChanged?.Invoke();
        // FRAME opens the multimedia-role default (role 1), like WasapiSource.
        public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId) { if (role == 1 && flow is 0 or 1) owner.DefaultChanged?.Invoke(flow); }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
    [StructLayout(LayoutKind.Sequential)] public struct PropertyKey { public Guid Format; public uint Id; }
    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumeratorEx
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out nint device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out nint device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }
}

internal sealed class WasapiSource(bool loopback, string? deviceId) : IAudioSource
{
    readonly float[] ring = new float[96000];
    readonly object sync = new();
    readonly CancellationTokenSource stop = new();
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int read, write, count;
    Task? worker;
    public Exception? Failure { get; private set; }
    public long Dropped { get; private set; }
    public long Discontinuities { get; private set; }
    public Task Start() { worker = Task.Run(Capture); return ready.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
    void Push(float left, float right)
    {
        lock (sync)
        {
            // Full ring (consumer starved, or slow device-clock drift over hours): drop the oldest frame and count it.
            // Before 0.5.4 this threw and stopped Shadow Recording ("Audio device interrupted").
            if (count + 2 > ring.Length) { read = (read + 2) % ring.Length; count -= 2; Dropped++; }
            ring[write] = left; write = (write + 1) % ring.Length; ring[write] = right; write = (write + 1) % ring.Length; count += 2;
        }
    }
    public void Mix(float[] output)
    {
        lock (sync) for (int i = 0; i < output.Length && count > 0; i++) { output[i] += ring[read] * .8f; read = (read + 1) % ring.Length; count--; }
    }
    public int Limit(int maxSamples)
    {
        lock (sync)
        {
            int excess = (count - maxSamples) & ~1; // whole stereo frames
            if (excess <= 0) return 0;
            read = (read + excess) % ring.Length; count -= excess; return excess;
        }
    }
    void Capture()
    {
        object? enumerator = null; IMMDevice? device = null; IAudioClient? client = null; IAudioCaptureClient? capture = null;
        nint format = 0;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            var e = (IMMDeviceEnumerator)enumerator;
            if (string.IsNullOrEmpty(deviceId)) Check(e.GetDefaultAudioEndpoint(loopback ? 0 : 1, 1, out device)); else Check(e.GetDevice(deviceId, out device));
            var iid = typeof(IAudioClient).GUID; Check(device.Activate(ref iid, 23, 0, out var obj)); client = (IAudioClient)obj;
            Check(client.GetMixFormat(out format));
            var fmt = Marshal.PtrToStructure<WaveFormat>(format);
            bool floating = fmt.Tag == 3 || (fmt.Tag == 65534 && Marshal.ReadInt16(format, 24) == 3);
            if (fmt.Channels is < 1 or > 2 || (floating ? fmt.Bits != 32 : fmt.Bits != 16)) throw new NotSupportedException("Audio requires a mono/stereo PCM16 or float32 endpoint. Set the endpoint to stereo in Windows sound settings.");
            using var signal = new AutoResetEvent(false);
            Check(client.Initialize(0, (loopback ? 0x20000u : 0u) | 0x40000, 1_000_000, 0, format, 0));
            Check(client.SetEventHandle(signal.SafeWaitHandle.DangerousGetHandle()));
            var service = typeof(IAudioCaptureClient).GUID; Check(client.GetService(ref service, out var cap)); capture = (IAudioCaptureClient)cap;
            Check(client.Start()); ready.TrySetResult();
            double phase = 0, ratio = (double)fmt.Rate / 48000; bool firstPacket = true;
            while (!stop.IsCancellationRequested)
            {
                signal.WaitOne(50); Check(capture.GetNextPacketSize(out var size));
                while (size > 0)
                {
                    Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    try
                    {
                        // DATA_DISCONTINUITY is a glitch report (e.g. loopback resuming after silence), not a lost device.
                        if ((flags & 1) != 0 && !firstPacket) Discontinuities++; firstPacket = false;
                        var raw = new byte[frames * fmt.Align]; if ((flags & 2) == 0) Marshal.Copy(data, raw, 0, raw.Length);
                        float Sample(int f, int c) { int offset = f * fmt.Align + Math.Min(c, fmt.Channels - 1) * (fmt.Bits / 8); return floating ? BitConverter.ToSingle(raw, offset) : BitConverter.ToInt16(raw, offset) / 32768f; }
                        while (phase < frames) { int f = (int)phase; Push(Sample(f, 0), Sample(f, 1)); phase += ratio; }
                        phase -= frames;
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }
                    Check(capture.GetNextPacketSize(out size));
                }
            }
        }
        catch (Exception e) { Failure = e; ready.TrySetException(e); }
        finally
        {
            client?.Stop(); if (format != 0) Marshal.FreeCoTaskMem(format);
            foreach (var o in new object?[] { capture, client, device, enumerator }) if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }
    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    public async ValueTask DisposeAsync() { stop.Cancel(); if (worker != null) await worker; stop.Dispose(); }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] struct WaveFormat { public ushort Tag, Channels; public uint Rate, Bytes; public ushort Align, Bits, Extra; }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice
    { [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioClient
    {
        [PreserveSig] int Initialize(int share, uint flags, long duration, long period, nint format, nint session);
        [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding); [PreserveSig] int IsFormatSupported(int share, nint format, out nint closest);
        [PreserveSig] int GetMixFormat(out nint format); [PreserveSig] int GetDevicePeriod(out long period, out long minimum);
        [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset(); [PreserveSig] int SetEventHandle(nint handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong position, out ulong qpc);
        [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
