# FRAME 0.6.4 — bounded shutdown and manual Stop on still screens (2026-10-28)

## Problem and implementation

Native reproduction with FFmpeg 8.1: gfxcapture acknowledged `q` but remained alive while its source was still. Previously manual Stop waited for the engine before exporting; Quit could wait indefinitely for an action or finalization, and an error dialog could also keep the app open. Evidence: `work/stop-064/baseline`.

- Manual recording intervals and the elapsed timer use the existing live edge, so still time counts and paused time does not. Stop waits at most 1.5 seconds for the segment tail, then snapshots the unfinished video, a fresh final frame and available pending PCM. All selected pipelines are snapshotted before any export. Manual-only capture processes are released before encoding; Shadow stays running when enabled.
- Closed-segment recordings use the existing exporter. Still tails use the existing manual encoding settings, trim each recording interval, and verify the output before publishing it. Failures retain original segments and snapshot files. Combined-source still snapshots omit the continuous-capture FPS filter, which otherwise needs another frame.
- Audio snapshots mirror the existing last 30 seconds of mixed PCM (11,520,000 bytes per audio pipeline, bounded). No changes to audio mixing, devices, latency, encoder settings or live capture cadence. The existing limitation remains: audio older than the 30-second backlog during a longer complete video stall can be silence. This change does not promise continuous audio retention for arbitrarily long still screens.
- Capture disposal bounds FFmpeg input/exit/drain and audio cleanup. Audio cleanup runs off the UI thread and repeated disposal awaits the same task. Quit has one 60-second deadline covering the action gate and engine finalization; late capture requests are ignored. If finalization fails or times out, only this instance's registered media children are terminated, new media starts are refused, recovery files stay, and an unresolved recovery notice is persisted for the next launch. No blocking error dialog during exit.
- The 60-second quit deadline can interrupt a long export. Its source data remains in Recovery; this is intentional bounded exit, not a guarantee that every long recording finishes exporting within a minute. Ordinary Stop has no new global export deadline.

## Validation

Initial development validation: Release build without warnings/errors; 141/141 Core tests. Native five-scenario still-stop suite passed after fixing a test-only FFmpeg duration argument (`.5` was invalid; `0.5` works). Native Close moving/still and injected stuck action/export passed 9/9 checks; normal exit took 2.46/7.85 seconds, injected 2-second deadlines exited in 2.18/2.25 seconds with recovery and no owned media process left. Failed/intermediate evidence remains under `work/stop-064`.

Final executable:

- Still-stop native suite: **50/50** checks across nine scenarios: no screen changes from startup (CPU H.264 and NVENC with audio), motion then still with audio, pause/resume with Shadow retained, combined sources, separate sources with audio, >35 seconds of stillness (PCM ring wrap), moving control, and a deliberately blocked output directory. Nine successful clips decode completely; longest duration discrepancy 37.1 ms; last-frame color and tail PCM verified. Stops took 4.11–15.61 seconds. The failed export reported failure, stopped capture, and retained original segments and snapshots. `work/stop-064/published-still/result.json`.
- Media/trim/share regression: **36/36**, `work/stop-064/published-media/media-validation.json`.
- Close via the real title-bar button: **11/11** across moving, still, stuck action, stuck export and a synthetic audio device whose disposal never completes. Moving/still exit 6.72/7.92 s with complete clips; stuck-audio exit 13.92 s with a complete clip; stuck action/export exit 2.20/2.24 s using a test-only 2 s global deadline, with original segments and unresolved recovery notice retained. No owned capture/export processes survive. `work/stop-064/published-close-checks.json`. The stuck-audio case exercises the production capture/audio bounds and default 60 s quit deadline; it logs the expected cleanup timeout.
- Audio regression: **16/16**, including A/V sync across stillness, 45 s backpressure with the existing 30 s cap, lost/recovered desktop/microphone sources, default-device change, unavailable devices at startup, and four clean start/stop cycles. `work/stop-064/published-audio/result.json`.
- Capture regression: **11/11**, including replay saves during motion/stillness, final-frame correctness, immediate save feedback, recovery after an intentionally killed capture process, intentional Stop without warnings, and source-loss handling. Its failed restart log after deliberately closing the test window is expected test evidence. `work/stop-064/published-capture/result.json`.

Total: **265/265** Core/native/media checks passed. Final executable hash was rechecked after testing. No C# source changes followed the final publish.

## Build identity

`C:\dev\Frame\builds\FRAME-0.6.4-beta-stop-shutdown-20261028\FRAME.exe` — self-contained single-file win-x64, product version `0.6.4-beta`, **182,069,769 bytes**, SHA-256 **A227AB7B1B346BA1E842D8C40A351EBD31315C2D5740F7AE2272663839F29624**. Runtime startup log confirms .NET **8.0.32**. Published successfully with cached packages, `NuGetAudit=false --ignore-failed-sources`; dependency versions unchanged. Test bundle extraction is directed to `work/stop-064/bundle-cache`. Earlier builds remain intact.

## Scope and limits

All tests use isolated data/output under `work/stop-064`, owned test windows and synthetic audio. No user settings, recordings or running FRAME are changed. Native capture tests need the active Windows desktop and run outside the capture-restricted sandbox. This task does not qualify reboot, sleep/resume, physical monitor changes or an overnight soak (the next release-readiness work was not requested yet). No installer is produced.
