# FRAME

> **Current state (2026-10-13):** FRAME 0.5.4 Beta source (Shadow Recording app only). **FRAME Advanced Capture is now a separate product in its own repository, `C:\dev\AdvancedCapture`** (own solution, build, tests, packaging and docs); this repository no longer contains or builds it. The text below is an older snapshot (0.3.5) kept for history; for current status and commands see `CLAUDE.md` and `Handover_files/HANDOVER_Frame.md`.

Native Windows capture applications — FRAME 0.3.5 / FRAME Advanced 0.2.0, 12 October 2026 (historical).

Latest: [0.3.5 visible recording atmosphere](docs/FRAME_035_VALIDATION.md). Previous: [0.3.2 atmosphere and startup feedback](docs/FRAME_032_VALIDATION.md), with 100 Core, 36 native UI and four dialog checks passing. Runnable copy: `builds/FRAME-0.3.5/FRAME.exe`.

The five-phase Desert Drift update is implemented and locally built. See [current verification and hardware acceptance](docs/DESERT_DRIFT_VALIDATION.md): 99 Core checks, 23 native UI checks, four hotkey-dialog checks and nine synthetic-media checks pass. Previous release packages remain unchanged.

**This is executable application code, with verified builds and synthetic media tests. It is not yet a production-qualified release or a complete implementation of the master brief.** Live capture and audio require validation outside the restricted execution environment used to build it. See [validation](docs/VALIDATION.md) and [known limits](docs/LIMITATIONS.md) before relying on an important recording.

![FRAME Desert Drift native interface](docs/validation-0.3.1/dark.png)

Latest home refinements remove FRAME screenshot capture and move folder selection beneath Shadow. [0.3.1 verification and launch details](docs/FRAME_031_VALIDATION.md).

## Run

1. For the fresh build, open `builds\FRAME-0.3.1\FRAME.exe` or `src\Frame.Advanced\bin\Release\net8.0-windows\FRAME.Advanced.exe`. Keep adjacent files. `dist`, `Run.ps1` and `Run-Advanced.ps1` may use an older packaged version.
2. This package requires the **.NET 8 Windows Desktop Runtime, x64**. It is installed on the development machine.
3. Install/use **FFmpeg 8.1+**, with `gfxcapture`, the desired encoder, and `ffprobe.exe` beside `ffmpeg.exe`. FFmpeg is already installed on the development machine, but it is not redistributed in this package. Select its real executable in Settings if PATH detection cannot resolve a package-manager link. Alternatively place both executables in `dist\tools`.
4. Choose the output folder, display selection, audio sources and quality. Desktop/microphone audio and sound cues initially stay off. Replay never starts automatically. Audio endpoints are read only when you press Refresh audio devices in Settings.
5. Choose **5 min** (or another history length) beside **Start shadow recording**, then start. Use **Save last 5 min** to keep a clip. **Start manual recording** is the separate manual action. Choose the capture folder below the Shadow button; the change saves immediately. `Saved` appears only after export verification. Closing the main window keeps FRAME in the tray; **Quit FRAME** finalizes and exits.

No elevation is required. Windows capture and device permissions still apply. Protected content is not bypassed.

| Shortcut | Action |
|---|---|
| Ctrl+Shift+R | Start/stop recording |
| Ctrl+Shift+P | Pause/resume |
| Ctrl+Shift+S | Save replay |
| Ctrl+Shift+M | Open monitor selection |

Change hotkeys in Settings: click a binding and press the new combination; it saves and activates immediately. Failed registration or persistence leaves the previous binding in place. Conflicts/restrictions are reported with the Windows error code. Global shortcuts are not guaranteed in exclusive-input or restricted environments.

## Implemented

- Native WPF Desert Drift dark/light shell, functional history dial, optional reduced-motion background, conservative game-folder replay routing, monitor layout, selected-display persistence and display-change notifications.
- Windows Graphics Capture through FFmpeg's `gfxcapture` filter, explicit monitor/window handles, selected window and single-display region capture.
- Independent display files; combined physical-layout composition. Native, 720p, 1080p, 1440p and 2160p settings; 30/60/120 FPS and bitrate configuration.
- Real initialization probes for NVIDIA NVENC, AMD AMF and Intel Quick Sync. H.264/HEVC/AV1 offered only for successful probes; explicit CPU H.264 fallback.
- Native WASAPI desktop/microphone capture, endpoint selection, bounded audio rings, stereo mixing and named-pipe transport. This implementation is **not yet hardware audio-qualified**.
- Shared encoding for replay and ordinary recording. Disk-backed keyframe segments, duration/byte retention, pause ranges, export verification, low-disk watchdog and recovery journals.
- Global shortcuts, tray operations, compact draggable controls, optional sound cues and 1 Hz measured CPU/RAM diagnostics.
- Background library enumeration, search, newest-first ordering, image/video filtering, incremental rows, thumbnails and metadata cache, open, reveal, rename, recycle, copy and drag files.
- Advanced capture is no longer part of FRAME: smart (UI Automation) selection, smart screenshots and opt-in window motion recording live in the separate app **FRAME Advanced Capture** (`FRAME.Advanced.exe`, built and published together with FRAME). Motion detection is experimental and does not classify video semantics.

## Architecture

`Frame.Core` contains deterministic state, configuration, geometry, replay and motion logic. `Frame.App` owns the WPF views, native Windows adapters and capture coordinator. The media subprocess owns frame acquisition, GPU encoding and muxing; UI rendering never drives frame delivery. WASAPI feeds bounded stereo float buffers through a local pipe. One encoder pipeline runs per independent target, or one composed pipeline for combined mode. Ordinary recording bookmarks the active replay pipeline instead of launching a second encoder for that target.

Segments close at approximately one-second keyframe boundaries. Final export trims recorded intervals, re-encodes using the selected encoder and joins them into MP4. It is verified with ffprobe and a decode of the first video frame before the final name is committed. Integration tests additionally decode every frame of their fixture exports.

See [architecture decisions and sources](docs/ARCHITECTURE.md).

## Project structure

```text
Frame.sln                 Solution
src/Frame.Core/           State, configuration, geometry, replay and motion logic
src/Frame.App/            Native desktop app, capture/audio/export services and views
tests/Frame.Tests/        Dependency-free deterministic test runner
assets/                  Generated brand direction, vector mark and application icon
docs/                    Original brief, design, architecture, results and limitations
dist/                    Built application (after publishing)
Build.ps1                Build and core tests
Publish.ps1              Distributable package
Run.ps1                  Launch the application
Install-Project.ps1      Copy source into an empty destination, default C:\dev\Frame
Recover-Session.ps1      Remux retained recovery segments without deleting originals
```

The project lives in `C:\dev\Frame` (moved there on 2026-10-11). All scripts use their own folder (`$PSScriptRoot`), so the project can be built and run from any location. `Install-Project.ps1 -Destination <dir>` copies the source to another empty folder and refuses to overwrite a nonempty directory.

The home screen prioritizes Shadow Recording: choose a history length beside **Start shadow recording** (5 minutes by default), then use **Save last 5 min** to keep a clip. Manual recording and screenshots remain separate actions.

## Build and distribute

Requires the .NET 8 SDK on Windows. There are **no external NuGet dependencies**. WinUI was evaluated; WPF avoids an additional Windows App SDK runtime and builds with the installed SDK.

```powershell
.\Build.ps1
.\Publish.ps1
.\dist\FRAME.exe
```

Keep all files in `dist` together. To build a self-contained x64 executable on a machine that can restore the runtime pack:

```powershell
.\Publish.ps1 -SelfContained
```

The self-contained path is provided but was not validated here because the package feed was unreachable. The delivered executable is framework-dependent. No installer, signing certificate, update service or public distribution has been configured.

## Validation and next work

- Release build: succeeds with zero warnings/errors.
- 51 core tests pass.
- Seven synthetic media checks pass, including replay trimming, paused-interval removal, full fixture decode, missing-footage rejection and preservation after a save failure.
- Successful initialization probes: NVIDIA H.264/HEVC/AV1 and Intel H.264/HEVC. This does not establish live capture throughput.
- Short idle sample: approximately 0.016% normalized CPU, 132.6 MB working set and 81.1 MB private bytes. No active-recording performance claim is made.
- Live WGC capture returned access denied (`0x80070005`). Global hotkeys did not register in the restricted test process. The published executable passed the idle UI smoke test; actual screen and sound capture remain untested.

Next priorities: interactive hardware acceptance; QPC-based cross-stream synchronization and drift measurement; GPU composition and faster export without full re-encoding; long-session/device-loss testing; stronger Smart Capture geometry/temporal classification; signing and packaging. Full details are in [LIMITATIONS.md](docs/LIMITATIONS.md).

Configuration and diagnostics live in `%LOCALAPPDATA%\FRAME`. Recovery content can contain private screen/audio data; only failed sessions are retained for manual review. Recording/export-pinned segments can exceed the replay retention budget. The low-space watchdog stops capture at 512 MB free space.
