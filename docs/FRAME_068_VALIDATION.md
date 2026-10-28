# FRAME 0.6.8 validation — recording tools in Setup

Date: 2026-10-28. Requested: prepare FFmpeg during FRAME installation so users do not install it separately afterward. README now describes FRAME only, as requested.

## Implementation

`packaging/FRAME.iss` prepares dependencies before copying app files. A download page fetches the pinned Gyan FFmpeg 8.1.2 full x64 ZIP directly from the publisher. Inno verifies the archive SHA-256; the extracted helper verifies it again, extracts only ffmpeg.exe, ffprobe.exe, LICENSE and README, checks their hashes and performs bounded capture-filter/probe checks. Inno installs the staged files normally under `{app}\tools`, including uninstall tracking.

An identical installed set is reused without network access. Missing/changed components trigger preparation again. Invalid/missing archives and preparation errors block installation before app-file changes, with retry/cancel instructions. `/FFMPEGARCHIVE=<absolute zip path>` allows a checksum-verified offline source. No winget, PATH changes or system-wide FFmpeg installation; existing custom FRAME paths/settings are preserved. Fresh FRAME installs discover private tools through the existing Backend resolver.

The archive manifest is based on the [Microsoft WinGet manifest](https://github.com/microsoft/winget-pkgs/blob/master/manifests/g/Gyan/FFmpeg/8.1.2/Gyan.FFmpeg.installer.yaml) and [Gyan release](https://github.com/GyanD/codexffmpeg/releases/tag/8.1.2). Downloaded archive: 266,085,149 bytes, SHA-256 `B8CDEFAB5F50590A076C27C2B56B0294A0E6154FADED28BA1BA05EBC4F801F57`. FFmpeg binaries are not embedded in Setup or added to the FRAME repository. The publisher's license/build README are retained; third-party notices link the publisher, source and [FFmpeg licensing information](https://ffmpeg.org/legal.html).

## Verification

- Release solution build: zero warnings/errors; Core regression: 159/159.
- Self-contained win-x64 publish: success; payload smoke reports .NET 8.0.32.
- Real package staging with Windows PowerShell 5.1: archive/component hashes, gfxcapture `hmonitor` and ffprobe version checks passed.
- Negative component-hash fixture: rejected before executable probes.
- Production Inno Setup 6.4.3 compilation: success, no warnings. Separate validation build: same prerequisite/install code, distinct AppId, no production shortcuts/launch/user-data cleanup, no compression; harmless unused-variable hint in that build only.
- Complete installer integration: 13/13 passed (real online download/install, missing/corrupt archive rejection, payload/hash identity, offline reuse, failed repair preserving existing app files, successful offline repair, installed-app render, private-tool media execution, uninstall and preserved user state).
- Installed-app media suite: 36/36 passed with automatic private-tool resolution and no `--ffmpeg` override, including replay, trim, share, thumbnails and encoder probes.

Evidence: `work/setup-068` and `work/installer-smoke-*`. The integration script is `scripts/Test-SetupFfmpeg.ps1`; all data/media fixtures are isolated under work. The production installer was compiled, while destructive install/upgrade/uninstall scenarios use the isolated validation variant to preserve the existing user installation.

The initial integration wrapper's 180-second media timeout expired, although the media process subsequently completed 36/36 checks. Only that test installation was uninstalled after the process completed. The wrapper now allows 600 seconds, persists the pre-test user-state fingerprint, and the successful complete run uses the fresh `integration-final` directory. Both test installations were removed; the real user's settings, startup entry and production install location were verified unchanged in the complete run.

## Artifacts

Directory: `C:\dev\Frame\builds\FRAME-0.6.8-Beta-20261028-213554`.

- Setup: `FRAME-0.6.8-Beta-Setup.exe`, 66,119,537 bytes, SHA-256 `8318A51CE46AF9D5EFDD82B31CFF34B3DDE926894560F445FE659D464AEF2D99`.
- Payload: `app\FRAME.exe`, 182,146,569 bytes, SHA-256 `EEE0236D25BE524BF478BEFD810705FBF0018ADD187A41B34E5EC317FBCACFD6`.
- App file version 0.6.8.0; product version `0.6.8-beta+4502923644e6240b7a1af43d6a5c774136e7c7c9`. The suffix is the pre-change checkout HEAD; the package was produced before committing this task.
- `build-info.json`, installer checksum, release notes and notices accompany the build. Prior builds are retained. The standalone payload still needs FFmpeg; run Setup for automatic preparation.

## Limits

No GitHub installer release/tag published by this task. The installer is unsigned. This PC already has another FFmpeg installation; the test verifies private-tool resolution and hash identity but is not a clean-VM or Windows 10 qualification. First online install requires about 270 MB downloaded and temporary space. Proxy restrictions, loss of connection mid-download, visible wizard cancellation and the full published-release updater sequence need separate manual acceptance. Existing capture/audio/encoder implementation and user settings are unchanged; new installations receive the pinned FFmpeg 8.1.2 tools.
