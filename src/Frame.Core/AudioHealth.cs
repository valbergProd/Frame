namespace Frame.Core;

public enum AudioSourceKind { Desktop, Microphone }

// Ordered spool between the wall-clock audio producer and the FFmpeg pipe writer (0.5.4 audio fix).
// FFmpeg legitimately stops reading the audio input while the video stream does not advance (Windows Graphics Capture
// sends no frames on a still screen and FFmpeg's scheduler throttles inputs that run ahead of the slowest output).
// The producer must never wait for that: it keeps draining the capture devices on the wall clock and spools chunks here.
// At most MaxRealChunks of real audio are kept; older chunks are turned into "owed silence" in place, so the total
// number of chunks (= the audio timeline, = A/V sync) is always preserved while memory stays bounded.
public sealed class AudioSpool<T> where T : class
{
    sealed class Node { public T? Chunk; public long Silent; }
    readonly Queue<Node> order = new();
    readonly Queue<Node> real = new();
    public int MaxRealChunks { get; }
    public long PendingChunks { get; private set; }
    public int RealChunks => real.Count;
    public long EvictedChunks { get; private set; }
    public AudioSpool(int maxRealChunks) { if (maxRealChunks < 1) throw new ArgumentOutOfRangeException(nameof(maxRealChunks)); MaxRealChunks = maxRealChunks; }
    // Returns a buffer whose audio was replaced by silence (for reuse), or null.
    public T? Enqueue(T chunk)
    {
        var node = new Node { Chunk = chunk }; order.Enqueue(node); real.Enqueue(node); PendingChunks++;
        if (real.Count <= MaxRealChunks) return null;
        var oldest = real.Dequeue(); var evicted = oldest.Chunk; oldest.Chunk = null; oldest.Silent = 1; EvictedChunks++;
        return evicted;
    }
    public void EnqueueSilence(long chunks)
    {
        if (chunks <= 0) return;
        order.Enqueue(new Node { Silent = chunks }); PendingChunks += chunks;
    }
    // Next entry in timeline order: a real chunk, or a run of silent chunks (adjacent silence merged).
    public bool TryDequeue(out T? chunk, out long silentChunks)
    {
        chunk = null; silentChunks = 0;
        while (order.Count > 0)
        {
            var node = order.Peek();
            if (node.Chunk != null)
            {
                if (silentChunks > 0) break;
                order.Dequeue(); real.Dequeue(); chunk = node.Chunk; PendingChunks--; return true;
            }
            order.Dequeue(); silentChunks += node.Silent; PendingChunks -= node.Silent;
        }
        return silentChunks > 0;
    }
}

public static class AudioHealth
{
    // Backpressure is normal while audio is AHEAD of video (still screen). A writer that stays blocked although the audio
    // already accepted is well BEHIND the video means FFmpeg is no longer consuming audio at all: a real pipeline failure.
    public const double BlockedSeconds = 10, BehindSeconds = 5;
    public static bool InputBroken(double writerBlockedSeconds, double audioAcceptedSeconds, double videoSeconds)
        => writerBlockedSeconds >= BlockedSeconds && videoSeconds - audioAcceptedSeconds >= BehindSeconds;

    // Restart schedule for a lost audio source (device unplugged, endpoint invalidated, audio service restarted).
    public static TimeSpan RetryDelay(int failedAttempts) => TimeSpan.FromSeconds(failedAttempts switch { <= 0 => .5, 1 => 1, 2 => 2, 3 => 5, _ => 10 });

    public static string Name(AudioSourceKind kind) => kind == AudioSourceKind.Desktop ? "Desktop audio" : "Microphone";

    // Top-right note text for the sources that are currently unavailable.
    public static (string Heading, string Detail) Interrupted(IReadOnlyCollection<AudioSourceKind> down, int enabledSources)
        => down.Count >= enabledSources || down.Count == 0 ? ("Audio interrupted", "Recording continues without audio")
            : (down.Contains(AudioSourceKind.Microphone) ? ("Microphone interrupted", "Recording continues without the microphone") : ("Desktop audio interrupted", "Recording continues without desktop audio"));
}

// When to show audio notes, so brief switches (e.g. a default-device change) stay silent and nothing is spammed:
// "interrupted" only after an outage lasted Grace; "restored" only after an "interrupted" note; at most MaxNotes
// interruption notes per Window (further outages are logged and shown in the app, but not as notes).
public sealed class AudioNoticePolicy
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(1.5), Window = TimeSpan.FromMinutes(10);
    public const int MaxNotes = 3;
    readonly Queue<DateTimeOffset> shown = new();
    public bool InterruptionShown { get; private set; }
    public bool ShouldAnnounceInterruption(DateTimeOffset downSince, DateTimeOffset now, bool stillDown)
    {
        if (!stillDown || InterruptionShown || now - downSince < Grace) return false;
        while (shown.Count > 0 && now - shown.Peek() > Window) shown.Dequeue();
        if (shown.Count >= MaxNotes) return false;
        shown.Enqueue(now); InterruptionShown = true; return true;
    }
    public bool ShouldAnnounceRestore()
    {
        if (!InterruptionShown) return false;
        InterruptionShown = false; return true;
    }
    public void Reset() => InterruptionShown = false;
}
