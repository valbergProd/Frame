# Validation · 10 October 2026

## Scope

Verified: Release compilation, deterministic core tests, synthetic media export/failure tests and native UI rendering. Not verified: successful live WGC capture, physical audio capture, game impact, cross-display synchronization or long-session reliability. Hardware acceptance is explicitly pending.

## Build and deterministic tests

Environment: installed .NET SDK 8.0.402, native Windows Desktop target, FFmpeg 8.1. No external NuGet dependency. Release compilation completed with zero warnings and zero errors.

**45 core tests passed; zero failed.** [Full test output](core-tests.txt) covers lifecycle transitions, hotkey debounce, atomic naming, settings/monitor persistence, invalid settings, independent/combined grouping, duplicate targets, replay retention and pinning, complete segment coverage, semantic candidate priority, motion sustain/hysteresis/cooldown and opt-in audio defaults. These are logic tests, not physical multi-monitor acceptance.

Reproduce from the source directory with `Build.ps1`. Test working files are created in a unique `work/FrameTests-*` folder and removed by the test runner.

## Version 0.2 UI revision

Release compilation and 45 core checks pass. New checks cover the five-minute default, legacy migration, preservation of custom durations and persistence of a newly chosen 30-second duration. Dark and light native renders were inspected after the revision. Live capture and physical audio were not exercised for this UI update. The media, recovery and idle figures below belong to the prior 0.1 validation; the capture/export engine is unchanged.

## Media integration

**Seven checks passed** in [media-validation.json](media-validation.json). They use generated 640×360/30 FPS video and a generated 48 kHz sine-wave file stream. Nothing is played to a speaker or recorded from an audio endpoint.

| Check | Observed result |
|---|---|
| Segment generation | 5 finalized segments; 5.021 seconds |
| Fractional replay trim | 2 seconds requested; 2.021333 seconds output; 321,447 bytes; all frames decoded |
| Pause/resume intervals | 1.9 seconds requested; 1.942667 seconds output; 298,370 bytes; all frames decoded |
| Empty output | Rejected |
| Missing interior footage | Export rejected before encoding; original preserved |
| Invalid output destination | Save rejected; every source segment preserved |
| Encoder initialization | NVIDIA H.264/HEVC/AV1 and Intel H.264/HEVC succeeded; CPU H.264 available |

The replay export took 5.037 seconds and paused export 5.071 seconds on these tiny CPU-encoded fixtures, including tool startup and verification. This is evidence that the current exact-trim path is not instant. It is not a sustained hardware-encoding benchmark. AMD AMF and Intel AV1 were not offered after their probes.

Reproduce with `FRAME.exe --media-test <empty-test-folder> --ffmpeg <real-ffmpeg-path>`. This developer mode has no UI and uses synthetic media only. Re-running on an occupied folder is not a recording recovery workflow.

## Native UI and idle measurements

The supplied dark/light images are actual WPF renders, not webpage mockups. Layout has been inspected at the rendered size. They do not establish correct interaction across physical mixed-DPI monitors.

The raw ten-second idle report is [idle-benchmark.json](idle-benchmark.json). It measures the native UI/tray with no capture and reports CPU normalized across logical processors, working set and private bytes. This short sample is descriptive, not a stable performance guarantee. Its `registeredHotkeys` value reflects restrictions in the test process.

## Environment restrictions and incident

- Live WGC returned access denied (`0x80070005`). No alternate capture route was used to circumvent it.
- Global hotkey registration was unavailable in the restricted process; interactive registration is pending.
- E50 playback stopped during the earlier development session and returned after the user power-cycled E50. Audited commands contained no audio-setting/driver writes and the exercised code paths did not start physical audio capture. Event-log access was denied, so causation was not established.
- After that incident, startup audio discovery was removed and sound cues default off. Subsequent UI/media checks do not enumerate, open or play to E50 or another audio endpoint.
- Self-contained runtime restore/publishing and signed installer deployment are unverified.

See [ACCEPTANCE.md](ACCEPTANCE.md) for the pending physical-device, long-session and failure-injection checks. No recording FPS overhead, end-to-end latency or A/V drift figure is claimed.

## Recovery script

The recovery script successfully remuxed all five synthetic segments into an MP4 with both video and audio streams (5.164167 seconds). Its first frame decoded. This tests the script on intact fixtures, not recovery after a real device crash. The longer duration reflects per-segment timestamp/audio padding; timing should be reviewed before deleting originals.

## Published executable smoke test

The framework-dependent published FRAME.exe completed the native UI/tray idle test and exited successfully. Over 9.9999 seconds it measured 0.015624% normalized process CPU, 132.5664 MB working set and 81.1133 MB private bytes. Two displays were found; zero hotkeys registered in the restricted process. Audio endpoints were not enumerated and no audio stream was opened. This replaces the earlier pre-hardening idle sample.

The published executable also generated both final dark/light native UI renders successfully; each was visually inspected. The x64 apphost header was verified, and all five PowerShell scripts passed syntax parsing. The framework-dependent publish command succeeded.

## Clean source-package build

The source archive was extracted into a separate workspace directory with no bin/obj files. Initial solution restore failed in this restricted environment without diagnostics; forcing single-node MSBuild restore resolved it. Build.ps1 was corrected accordingly. The extracted source then built with zero warnings/errors and all 41 core tests passed. Runtime/source archive required files and relative documentation links are checked during packaging.

## Version 0.2.1 shell revision

Release build succeeds with zero warnings/errors. Dark and light native offscreen renders were inspected for the new background, sidebar and custom caption. The window uses native WindowChrome for drag/resize behavior; interactive Snap, taskbar and multi-monitor window behavior have not been manually verified in this environment. Capture/audio were not started for this visual update. The 45 core checks and media figures above belong to the previous validation.


## Version 0.2.2 hotkeys and navigation

Release build: zero warnings/errors. 51 deterministic tests pass. Added coverage: Alt+Z, combined modifiers, unmodified function keys, invalid/modifier-only input, Escape rejection and duplicate bindings. Settings and Advanced native renders verify the hotkey section and exclusive navigation highlight. Live physical key input, OS registration conflicts and dialog cancellation with real keyboard input still need user verification; no capture or audio test was started.


## Version 0.2.3 dialog and hotkey persistence fix

Four regression checks exercise actual offscreen WPF modal windows with programmatic input to the production key handler: Alt+Z selection, Escape, window close, and rejection/correction of a duplicate. See [dialog results](hotkey-dialog-tests.json). All pass. No physical keyboard, capture or audio is used. Windows global registration and rollback under real conflicts need user verification. The user reported a saved recording using Alt+Z in 0.2.2; this is user feedback, not an independently verified hardware result.

