# Known limitations and unfinished acceptance

The application has real implementations of capture, replay, screenshots, library operations and (in the separate app FRAME Advanced Capture, since 2026-10-11) advanced selection. A working build and successful synthetic tests do not establish production readiness. The following limitations must remain visible.

## Release blockers for a production claim

1. **Live capture is unverified in this environment.** The initial WGC attempt returned `0x80070005` (access denied); no bypass was attempted. Monitor/window/region capture, 120 FPS and GPU frame interoperability require an ordinary interactive Windows session.
2. **Physical audio is unverified.** A reported E50 loss of playback was resolved by disconnecting/reconnecting its power. Audit found no sound-setting/driver mutations and no physical audio recording test; the cause remains unknown. Subsequent validation uses synthetic file audio. Audio devices are now enumerated only when the user requests a refresh, and all sound/capture options default off.
3. **Synchronization is limited.** Independent displays and their audio sources start separately. Audio uses basic rate conversion and bounded queues, not a common QPC clock or adaptive drift compensation. Long-run A/V and cross-display sync are not qualified. Mono/stereo PCM16 or float32 mix formats are supported; surround/downmix and other formats are not.
4. **Gaming performance is unmeasured.** No active-capture FPS-impact, frame-pacing, GPU-utilization, encoder-latency, duplicated/dropped-frame or A/V-drift measurements have been obtained. CPU/RAM display is process usage, not a comprehensive capture profiler. No claim of lower overhead than OBS/ShadowPlay is justified.
5. **Mixed DPI and long sessions need acceptance.** Native UI renders were inspected; mixed 100–200% monitor layouts, display hotplug, minimized windows, device loss, sleep/resume and hours-long memory behavior remain unverified.

## Functional boundaries

| Area | Current behavior / gap |
|---|---|
| Replay latency | (0.5.4, 2026-10-14) Shadow replays stream-copy closed segments with exact lengths; only a partial first segment (≤ 1 s) and a still-screen tail are encoded; full re-encode is the verified fallback. Export still holds the command gate. Copy cost: fast concat form (explicit in/out points, minimal probe) ~35–70 ms per 1 s segment at 1440p (game load); window ends at the request. Manual-recording trims re-encode. |
| Pause precision | Bookmarks use media progress reported about four times per second. Frame-perfect button timing is not promised. |
| Capture startup | WGC/encoder process startup and first-frame validation take time. Instant start is not guaranteed. |
| Multi-monitor | Explicit independent or combined groups; no silently omitted target. Combined mode copies to CPU memory. Tight synchronization is unfinished. Region video must stay within one display. |
| Audio | One stereo mix; no independent exported tracks, live mic/desktop toggles, automatic device reconnection or QPC drift correction. Device interruption stops capture and preserves recovery data. |
| Hotkeys | Custom bindings, no-repeat handling and Win32 error reporting implemented. Restricted test processes could not register shortcuts. Exclusive-input games may prevent them. |
| Smart Capture | Native/UIA geometry and manual correction only. No visual edge/letterbox detector, semantic video-versus-animation classifier, continuous hover discovery, scrolling target tracking or remembered corrected regions. |
| Motion mode | Explicitly armed 5 Hz region sampling. It still captures the selected region at full size before downsampling; large regions cost more. False positives from animation/occlusion are possible. |
| Screenshots | Desktop, selected display bounds, visible window/region and delayed selection. Multiple display screenshots cover their bounding rectangle, including intervening content. No cursor compositing or capture of an obscured window's private surface. |
| Annotation | Pen, highlighter, stroke eraser and stroke removal. No text, arrows, shapes, blur/redaction or comprehensive undo history. |
| Toolbar | Draggable timer/pause/stop/replay/hide controls. No live audio toggles or expandable mode. Capture exclusion depends on Windows support. |
| Library | Background full directory enumeration, newest-first sort, search/filter and 50-row increments. Thumbnail tasks are bounded; disk cache retention is not yet configured. No alternate sort order or embedded video preview. |
| Recovery | Failed segments retained. Remux script requires a valid surviving set; a truncated last segment may need manual exclusion. Pause ranges are journaled but not automatically restored by the script. |
| Shutdown | Normal Quit finalizes or reports recovery failure, then releases capture. Forced process termination/power loss cannot guarantee the last open segment. Windows session-ending behavior needs testing. |
| Deployment | Framework-dependent unsigned build, external FFmpeg/ffprobe. No installer, signing, updates or redistributable FFmpeg license bundle. Self-contained publishing is scripted but unvalidated. |

Windows startup replay, GIF conversion, click effects, DND integration and third-party privacy exclusions are not implemented. They are not exposed as fake controls. Optional output/device improvements should follow hardware qualification rather than inflate the feature list.

## Recommended engineering order

1. Run the explicit [hardware acceptance sequence](ACCEPTANCE.md) with audio initially off, then test an agreed audio device separately.
2. Introduce shared QPC timestamps, resampler drift correction, multi-display startup alignment and measured drift/error counters.
3. Decouple export from Stop/maintenance, add cancellation, and implement remux fast paths with keyframe-aligned edges.
4. Move combined composition to GPU; measure frame pacing/game impact before optimizing further.
5. Add device-loss/reconnect and session-ending integration tests, long-run soak tests and bounded disk caches.
6. Strengthen selection geometry/temporal semantics, screenshot refinements, accessibility and DPI acceptance.
7. Establish licensing, code signing, an installer/update path and a supported runtime maintenance policy before public release.
