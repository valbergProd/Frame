# FRAME 0.5.1 Beta · release validation, 2026-10-13

## Release
- Folder: `C:\dev\Frame\builds\FRAME-0.5.1-Beta\` (new; older `builds\*` untouched).
- ZIP: `C:\dev\Frame\builds\FRAME-0.5.1-Beta-win-x64.zip` (134,127,018 bytes, SHA-256 `103e6805b5a259479fdfc7d8ede5cd475133472c04c6221c9d9dbf484f8c86d6`; same 11 files, exe hashes match the folder).
- Contents: `FRAME.exe` (FRAME, ProductVersion 0.5.1), `FRAME-Advanced.exe` (FRAME Advanced Capture, component version 0.2.0; renamed from `FRAME.Advanced.exe`), `README.md`, `BETA_TESTING.md`, `CHANGELOG.md`, `tools\README.txt`, `licenses\` (.NET 8.0.8 runtime + Windows Desktop runtime MIT licenses, runtime third-party notices, FFmpeg notice, notice index).
- Not published or uploaded anywhere.

## Build configuration
`dotnet publish src/<Frame.App|Frame.Advanced> -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none` (SDK 8.0.402, runtime packs 8.0.8). No trimming, no ReadyToRun/AOT, no single-file compression. Each exe ≈164 MB and needs no installed .NET. Staging: `work\publish-051beta\`.

## FFmpeg decision
Not bundled. The only local build is gyan.dev 8.1 "full" (`--enable-gpl --enable-version3`, GPLv3); redistributing it obliges us to provide corresponding source for FFmpeg and its GPL libraries, which this package cannot guarantee. Instead: `Backend.Resolve` order is Settings path → `tools\ffmpeg.exe` beside the exe → `%LOCALAPPDATA%\Microsoft\WinGet\Links\ffmpeg.exe` (new; works right after `winget install Gyan.FFmpeg` without a PATH refresh) → PATH. ffprobe is taken from the same folder. The missing-engine message now names winget, the tools folder and Settings › Advanced.

## Code changes (packaging-motivated)
- `Backend.cs`: WinGet links candidate; clearer missing-FFmpeg message (shared with Advanced).
- `DiagnosticLog.cs` (new) + wiring: startup line (version, OS, runtime, folder), engine detection result/failure, export and trim failures, unhandled UI exceptions, existing action errors. JSON lines, 2 MB rotation to `.previous`, local only. Diagnostic runs (`--data`) skip the startup line.
- No recording, settings-key or storage changes. Settings stay in `%LOCALAPPDATA%\FRAME` / `%LOCALAPPDATA%\FRAME Advanced`; recordings in `Videos\FRAME` (and `\Advanced`). Single-instance mutexes (`Local\FRAME-Desktop-0.1`, `Local\FRAME-Advanced-0.1`) are shared with older builds, so an old and the beta build cannot run at the same time.

## Results
- Release build of the solution: 0 warnings, 0 errors. Core tests: 116/116.
- Packaged binaries, run from a copy in another folder (`work\beta-fresh`, with test-only `tools\ffmpeg.exe`/`ffprobe.exe`):
  - `FRAME.exe` native UI suite (`work\validate-beta.ps1`): 83/83 incl. Library, trim editor, Share, save toast, settings, hidden-animation checks; hotkey dialog suite 4/4; dark/light/minimum/hotkeys renders exit 0.
  - `FRAME.exe --media-test` with **PATH stripped to System32 and no configured FFmpeg** (`work\validate-beta-media.ps1`): 32/32 (exports, presets, 13 trim-accuracy checks, cancellation, thumbnails). Render with empty FfmpegPath logged engine `...\beta-fresh\tools\ffmpeg.exe`, encoders h264/hevc/av1 NVENC, h264/hevc QSV, libx264.
  - `FRAME-Advanced.exe --render` with stripped PATH (`work\validate-beta-advanced.ps1`): exit 0, idle/hidden checks true, engine ready (tools\ FFmpeg).
- Embedded icons extracted from both packaged exes: gold (#E8AE52 at 32 px). No `C:\dev` string in either exe.

## Not verified
- A clean Windows machine/VM (all runs were on the development PC; the WinGet FFmpeg exists here, so the "no FFmpeg anywhere" first-run message was not observed in the packaged build).
- Live screen capture, real shadow saves, audio devices, game FPS impact, multi-monitor toast placement, physical taskbar/tray/Alt+Tab icons, SmartScreen behaviour, Windows 10.

## Before external beta
- Test the ZIP on a clean Windows 10 and Windows 11 PC (first run, SmartScreen, FFmpeg setup, recording).
- Consider rebuilding with the latest .NET 8 servicing SDK (bundled runtime is 8.0.8) and code signing.
- Decide on the FRAME license/terms for testers.
