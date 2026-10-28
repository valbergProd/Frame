# FRAME 0.5.3 Beta validation (Shadow Recording reliability, unified notifications), 2026-10-13

## Root causes found (measured on this PC with real Windows Graphics Capture)
1. **gfxcapture is change-driven.** Frames arrive only when the compositor delivers a change; timestamps follow the wall clock (pts gaps of 1.4 s observed). 0.5.1's "no frames for 15 s" fault was removed in 0.5.2.
2. **Encoders held frames.** NVENC's default output delay kept every frame of a static source inside the encoder (`frame=0` for a still window). Effects: Shadow Recording could not *start* on an unchanged screen ("Capture did not produce frames" after 12 s, e.g. an autostart onto a quiet desktop) and the newest frames never reached disk. Fix: `-delay 0 -zerolatency 1` (NVENC), `-tune zerolatency` (x264), `-async_depth 1` (QSV), `-usage lowlatency` (AMF), each used only if the encoder accepts it at probe time (`Encoder.LowLatency`).
3. **The fps filter held the newest frame** until a later one arrived. Single sources now use output-side `-fps_mode cfr -r <fps>` (each frame passes immediately; gaps are filled with duplicates when the next frame arrives). Combined multi-display layouts keep `fps` before xstack.
4. **The save ended at the last closed segment.** On a still screen the open segment never closes, and Matroska keeps its newest cluster in memory, so even the open file lacks the final ~100–200 ms. Fix below.

## Save timeline (implemented)
- The save request's media time is the pipeline **live edge** = media time of the last delivered frame + wall time since (`CapturePipeline.LiveEdge`), captured before any waiting.
- `ReplayWindow.Live` (Core) chooses `[max(buffer start, edge − history), edge]`; closed history must be continuous; the remainder after the last closed segment is the **tail**.
- `CaptureCoordinator.SaveReplay` waits up to 1.5 s for the current segment to close (moving content: ~1.1 s measured). If the tail remains, `MediaExport.ExportLive` encodes closed segments + a copy of the open segment (clusters flushed every 100 ms) + a **fresh single frame of the unchanged source** (`CaptureStillAsync`, same graph/size; single sources only), then `tpad=clone` + `trim` to the exact window; audio `apad` + `atrim`. A torn open segment falls back to closed segments only. Cost: one short ffmpeg capture and encode, only for saves with a still tail; nothing while recording.

## Failures, notifications, tray
- `CaptureCoordinator.Fail` classifies (Core `RecordingHealth.Classify`: Disk, Encoder, Source, Audio, Start, Engine; exit code 0 = source closed), sets state Faulted, still raises `Notice` (FRAME Advanced) and `FaultRaised`.
- `RecordingStatusView` (MainWindow): log `recording-fault`, persist `recording-status.json`, tray error badge; Shadow faults of retryable kinds get one automatic restart after 3 s (max 3/hour); the user then sees **one** final note ("Shadow Recording resumed" or "Shadow Recording stopped · Open FRAME for details"). Start failures (incl. autostart) → "Shadow Recording did not start", explained, no retry loop.
- Capture page panel (hidden when healthy): plain summary, suggestion, time and retry outcome, Restart Shadow Recording, Copy details, Dismiss, collapsed Technical details. After a restart of FRAME an unresolved stop shows as "Last session: …", never as the current state.
- "Shadow Recording active · Ready to save clips" after a confirmed start (first frames encoded), once per activation, including automatic start in the tray.
- All tray balloons removed (`Notify` is the in-window status line only); hidden-window action errors and unhandled errors use the same overlay. Tray: gold mark + badge (none = off, red dot = recording, gold ring = starting, orange triangle = stopped), tooltip text, changed only on state change.
- Setting "Show save notifications" covers saved/started/resumed notes; stop warnings always show.

## Results
- Release build: 0 warnings, 0 errors. Core 121/121 (live window, short buffer, gap rejection, fault classification).
- Media (`--media-test`): 36/36, incl. still tail 3 s + 4 s still (freeze starts at 3.03 s, 7.0 s long), torn open segment with audio padding, window entirely in the still tail, short buffer, plus all Share/trim regressions.
- **Real capture** (`--capture-test`, outside the sandbox, isolated data, NVIDIA h264_nvenc, test window): 10/10 for dev and packaged builds. **Test configuration: Shadow Recording history (`ReplaySeconds`) = 8 s**, 30 FPS, 6 Mbps, audio off; so every save is expected to be at most 8 s, or shorter only when less buffer exists. Results: start on a still window with exactly one "active" note; ~0.5 s idle + 5 s motion + 7 s still, saved at live edge 12.2 s → the clip is the **last 8 s** (4.2–12.2 s): 8.0 s long, about 1 s of motion then frozen from 1.07 s (expected 8 − 7 = 1.0 s), **ends on the final green still image**; save during motion → 8.0 s, no frozen tail (save took ~1.1 s); 25 s still → whole clip is the final image; killed ffmpeg → classified, restarted automatically, only "resumed" shown, buffer usable; intentional stop → no warning; closed source window → Source fault, retry failed, one "stopped" note, tray Error, panel explained.
- **Real autostart** (`--background` + StartShadowOnLaunch, display capture): hidden, Shadow active, one "active" note, tray Active, 4 hotkeys; test data deleted afterwards.
- Native UI: 92/92 (dev and packaged), incl. disk-fault panel/overlay/tray/persistence/last-session/dismiss and four distinct tray icons; hotkey dialogs 4/4. Background launch: hidden, tray, ready.
- Packaged FRAME-Advanced.exe render: exit 0.

## Test-isolation incident (fixed)
The PowerShell validation scripts wrote `FfmpegPath` as a JSON array (`(Get-Item).Target` is an array), so test settings failed to load and diagnostic runs since 0.5.0 used defaults, writing synthetic test clips into the user's `Videos\FRAME`. 32 test files (all with test names or this session's `.last.rgb` markers) were moved to `work\quarantine-from-Videos-FRAME\`; no user recordings were present or touched. Scripts now coerce the path to a string, and the UI and capture tests fail immediately unless the output folder lies inside the isolated data folder.

## Not verified
AMD/Intel-only encoders on real capture (AMF low-latency option untested), several monitors and combined layouts with still screens, real games (exclusive fullscreen), audio-enabled still periods on hardware (audio is opt-in; no physical audio tests by policy), hour-long still periods, display sleep/lock in practice, multi-monitor toast placement. Still-screen saves on combined layouts use the last frame on disk (no fresh-frame capture).
