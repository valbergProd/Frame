# FRAME 0.5.4 — Replay export performance investigation (adaptive export)

Date: 2026-10-14. Investigation only: no FRAME code changed. Test machine: i7-12700 (12 cores / 20 threads), RTX 4090, FFmpeg 8.1.

## Current pipeline (source of truth: `MediaExport.cs`, `Backend.StartAsync`, `CaptureCoordinator.SaveReplay`)

- **Recording:** 1 s Matroska segments, `-reset_timestamps 1`. Each segment starts on a forced keyframe (`-g fps`, `-force_key_frames` every 1 s, `-bf 0`). H.264 from NVENC/QSV/libx264 at the configured bitrate, AAC 192k.
- **Replay save, normal case (`Export`):**
  - concat demuxer over the segments;
  - full re-encode: software H.264 decode, then the same encoder at the same bitrate, plus AAC re-encode;
  - concat copy and a `+faststart` rewrite.
- **Replay save, still-screen tail (`ExportLive`):** a full re-encode of the whole window as well, with a tpad/apad filter graph.
- **Process priority:** the ffmpeg child runs at Normal priority with all threads. Nothing is throttled or scheduled.

## Measurements

Test setup:
- Synthetic 120 s, 2560x1440, 60 fps.
- h264_nvenc 20 Mbps and AAC, using the recording's segment arguments.
- 120 segments in `work/export-probe`.
- Scripts in the session scratchpad: `export-probe.ps1`, `export-probe2.ps1`.

| Path | Wall | CPU time | Output duration (content 120.0 s) | Video steps > 25 ms |
|---|---|---|---|---|
| A: current (re-encode + finalize) | 27.9 s | 83.7 s (~3 cores) | **123.62 s** | **119** (61–67 ms, one per segment) |
| B: stream copy, plain concat list | 7.5 s | 6.7 s | 123.62 s | 119 |
| C: stream copy + `duration` per segment (from segments.csv) | 8.8 s | 7.8 s | 120.04 s | 1 (38 ms) |
| F: C + per-file `option analyzeduration 0` / `probesize` | 3.3 s | 2.9 s | 120.04 s | — (warns: non-monotonic DTS) |
| Single-file remux (reference) | 1.2 s | 0.5 s | — | — |

All outputs contain 7200 frames, and audio and video lengths agree within about 22 ms.

Scaled linearly to a 5-minute save:
- current path: about 70 s and about 210 CPU-seconds, spread over about 3 cores;
- stream copy: about 8–22 s and 7–20 CPU-seconds on roughly one core, with negligible GPU use.

These figures use synthetic content. Real game footage decodes at a similar per-frame cost, but this has not been measured on a real recording.

### Findings

1. **Re-encoding is the cost.** About 92 % of export time and almost all CPU go to decoding and re-encoding footage that is already H.264 at the target bitrate. It also adds generation loss: the output is re-compressed at the same 20 Mbps.
2. **Existing defect: plain concat stretches the timeline.** Each segment's probed duration overshoots by about 30 ms, so every segment boundary leaves a 61–67 ms frame gap. That is one micro-stutter per second, plus about 3 % timeline stretch. Because `Export` trims with `-t` on that stretched timeline, a saved replay likely contains about 3 % less real time than requested. The current re-encode path inherits this (row A).
   - With `duration` directives taken from `segments.csv`, the timeline is exact.
   - Still to confirm: that this appears in real saves, using a frame-step check on a recording made in isolated `--data`.
3. Concat cost is dominated by opening and probing each 1 s segment, not by copying bytes. Per-file probe options cut it by about 60 %, but they trigger DTS warnings, so they need validation before use.
4. Segments are keyframe-aligned every second. Stream copy is therefore exact at segment boundaries; a cut can only be off by up to 1 s at the start of the window. The earlier decision to avoid range copy (1–2 frame overrun, about 0.1 s A/V drift) applied to cutting arbitrary files mid-GOP for Share/Trim, not to segment-aligned replay concat.

## Options compared

**1. Optimise the FFmpeg pipeline (recommended core)**
- **Segment-aligned stream copy:**
  - concat the closed segments with `duration` directives from `segments.csv`;
  - `-c copy`;
  - the start snaps to the segment containing the window start (at most about 1 s extra);
  - the end is the last closed segment.
- **Still-screen tail (`NeedsTail`):** re-encode only the tail (the open segment plus the fresh still frame, padded audio) with the recording's encoder settings, then join it to the copied body. The splice must be verified by decoding across the joint; if that fails, fall back to the current full re-encode.
- **Fallback:** keep the full re-encode only for cases that cannot be copied (gaps, incompatible streams, verification failure).
- **Optional:** drop or keep `+faststart`. Keeping it costs one extra read and write of the file (about 1 s per 300 MB on NVMe), and it helps uploads.
- **Expected effect:**
  - about 5–10x faster;
  - about 10x less CPU;
  - no GPU encoder or decoder use;
  - bit-identical quality;
  - fixes the per-second stutter.
- **Risks to validate:**
  - A/V sync must be unchanged against the known pre-existing offset (use `--audio-test --sync-only`);
  - segment durations during still periods;
  - AAC splice at the tail;
  - Explorer, browser and Discord playback;
  - multi-display pipelines.

**2. Adaptive resource-aware scheduling only**
- Re-encoding is inherently about 3 cores × 0.23 × clip length on this CPU. Throttling it only stretches the same work over more time: a slower save with the same total cost.
- Every metric costs something or has limits:
  - CPU load via `GetSystemTimes` and RAM via `GlobalMemoryStatusEx`: very cheap.
  - GPU per-engine use via the PDH "GPU Engine" counters: about 1 Hz is fine, but enumerating instances costs a few ms.
  - Disk activity via PDH PhysicalDisk: cheap.
  - Game active: FRAME already knows this (`GameFocus` / game tracker).
  - Game FPS or frametime: only safely available through ETW present events (PresentMon-style). That needs admin rights or the Performance Log Users group, and adds complexity. Not recommended; no injection or hooks.
- Hardware specs do not predict free capacity, so a fixed policy keyed to CPU or GPU model would be wrong.
- **Verdict:** high complexity, and it does not remove the root cost.

**3. Combination (recommended form)**
- Use option 1 for nearly all saves.
- Apply a small, bounded policy only to the re-encode paths (fallback, tail, and manual-recording trims with pauses):
  - ffmpeg at `BelowNormal` priority, as Share export already does;
  - when a game is active: a Job Object CPU-rate cap (for example 25–35 % of the machine), `-threads` limited, hardware decode (`-hwaccel d3d11va` or `cuda`) to move decode off the CPU, and the hardware encoder;
  - when no game is active: Normal priority, uncapped.
- Optionally relax the cap one step when `GetSystemTimes` shows lots of idle CPU over 2–3 s, and tighten it when idle CPU is low. Bounded steps, 1 Hz, no GPU or FPS telemetry needed. If a metric is unavailable, use the static "game active" policy.
- The copy path needs no adaptation: about one core of mostly I/O for a few seconds. Running it at `BelowNormal` is enough.

## Recommendation

Implement option 1 first: segment-aligned stream copy with `duration` directives and tail-only encoding, keeping the existing re-encode as a verified fallback. It is the fastest and most reliable route, keeps the original recording quality exactly, fixes the per-second gap defect, and removes almost all export load from the CPU and GPU while gaming.

Then add the minimal static part of option 3 to the remaining re-encode paths:
- `BelowNormal` priority;
- a CPU cap when a game is active;
- hardware decode.

Full adaptive telemetry, including FPS measurement, is not justified by the measured numbers. Revisit it only if real-hardware tests show measurable FPS loss from the copy path, for example disk contention on HDDs.

Validation plan before shipping:
1. Frame-step and duration check on real isolated recordings, including still periods.
2. A/V sync parity using the existing audio test harness.
3. Splice decode check for the tail.
4. Playback in common players.
5. Timing and FPS impact while a GPU-heavy game runs, before and after.

Test data: `work/export-probe` (about 2.6 GB of synthetic files). It can be deleted.
