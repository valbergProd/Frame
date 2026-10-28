# FRAME 0.6.2 · FFmpeg must not outlive FRAME · validation (2026-10-24)

## Build

| Item | Value |
|---|---|
| Folder | `C:\dev\Frame\builds\FRAME-0.6.2-beta-ffmpeg-orphan-fix-20261024\` |
| Executable | `FRAME.exe`, 182,028,297 bytes, SHA-256 `b7da234090001da9b84687cbab42eccbda6ee6758c97defae7dbd64824691e3e` |
| Version | 0.6.2.0, product version 0.6.2-beta; self-contained single file, win-x64 |
| Installer | none (not requested) |

## Problem (user report, 2026-10-24)

After quitting FRAME and leaving the PC on overnight, an `ffmpeg.exe` was still running and used 12 GB of RAM, as if it kept buffering video forever.

## Root cause

- A normal Quit (`MainWindow.Quit` → `CaptureCoordinator.DisposeAsync` → `CapturePipeline.StopAsync/DisposeAsync`) sends `q` and kills FFmpeg after the grace period, so that path was correct.
- Any other end of FRAME (crash, End task, a hang that was killed, a fatal exception) left the capture FFmpeg running. Closing stdin does not stop FFmpeg, and with FRAME gone nobody read its `-progress pipe:1` output, so gfxcapture frames kept being queued in memory. Reproduced with the 0.6.1 build (control run below).

## Changes (no change to recording pipeline, encoder settings, audio or timing)

- New `src/Frame.App/ProcessJob.cs`:
  - one Windows job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; `Backend.Start` assigns every FFmpeg/ffprobe process it starts to it. Windows ends them when FRAME's process ends, for any reason. The handle is never closed explicitly.
  - `KillOrphans(<data>\Recovery)`, run on a worker thread from `App.OnStartup` after the single-instance mutex: ends `ffmpeg` processes whose command line contains `gfxcapture`, `part-%08d.mkv` and this data folder's Recovery path, and whose parent process no longer exists (or started after the child, PID reuse). Parent id and command line are read from the target's PEB (x64). Everything else is left alone. A log line `orphan-ffmpeg` is written per ended process.
- Version 0.6.1 → 0.6.2 (`Frame.App.csproj`).

## Results

| Check | Result |
|---|---|
| `dotnet build` Release | 0 warnings, 0 errors |
| Core tests (`dotnet run --project tests/Frame.Tests -c Release`) | 137 / 137 |
| `work/validate-orphan.ps1` on a build-folder exe (isolated `--data`, real Shadow capture via StartShadowOnLaunch) | 6 / 6 |
| `work/validate-orphan.ps1` on the published exe (this build) | 6 / 6 |
| Control: same A scenario on the 0.6.1 build | capture FFmpeg still running after FRAME was force-killed (bug reproduced) |

Checks in the script: A1 FRAME starts a capture FFmpeg; A2 it is gone ~3 s after `Stop-Process -Force` on FRAME; B1 a fake orphan (command line with the markers, parent `cmd` exited) exists; B2 the next FRAME start ends it; B3 an unrelated FFmpeg survives; C no FFmpeg left over. (A first run showed C failing only because it looked 0 s after the kill; a 3 s wait was added.)

## Not verified / needs manual testing

- Windows logoff, shutdown, sleep or hibernate with Shadow on (expected to be covered by the job object; not measured).
- A real overnight run; FFmpeg memory use while FRAME is alive but hung.
- `Quit` still has no timeout when a stuck action never releases the action gate (the process then stays open; the job object only helps once it ends).
- An FFmpeg that is already running from 0.6.1 or older must be ended manually (Task Manager) or by starting this version.
- The manual-recording and export FFmpeg processes are also in the job (so an export is cut if FRAME is killed); normal Quit behaviour is unchanged and was not re-run through the long media/audio suites.

Test data: `work\validation-orphan-062*`, `work\validation-orphan-061ctl` (small; the agent cannot delete under `work`).
