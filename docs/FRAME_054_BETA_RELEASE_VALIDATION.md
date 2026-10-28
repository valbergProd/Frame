# FRAME 0.5.4 Beta · public beta release (installer, diagnostics) · validation (2026-10-16)

## Release (final: bundled .NET 8.0.32, built 2026-10-16 16:24)

| Item | Value |
|---|---|
| Release folder | `C:\dev\Frame\builds\FRAME-0.5.4-Beta-20261016-162409\` |
| Installer | `FRAME-0.5.4-Beta-Setup.exe`, 53,416,479 bytes (50.9 MB) |
| SHA-256 | `8B0805E5277912B0C982B2B4728034DE26FDA41D6FBB2BF022C8D4E9C38B0159` (also in `FRAME-0.5.4-Beta-Setup.exe.sha256.txt`) |
| Payload (installed as FRAME.exe) | `app\FRAME.exe`, 168,344,585 bytes, SHA-256 `3D3B90862352674890FEAC2A3BF7937B95607DF679A59B473381F4FAA7689369`, file version 0.5.4.0, product version 0.5.4-beta |
| Runtime | self-contained single file, win-x64, bundled **.NET 8.0.32** (Microsoft.NETCore.App and Microsoft.WindowsDesktop.App runtime packs and the app host 8.0.32 from nuget.org, Microsoft-signed; built with SDK 8.0.402) |
| Installer tool | Inno Setup 6.4.3, `packaging\FRAME.iss`, built by `scripts\Build-BetaInstaller.ps1` |
| Install type | per-user (no administrator rights), `%LOCALAPPDATA%\Programs\FRAME`, AppId `{0998192B-07BE-4AF5-B687-50024D3B1493}` |
| Code signing | none (no certificate available); SmartScreen warnings expected |
| Also in the folder | `BETA_README.md`, `RELEASE_NOTES.md`, `licenses\` (FRAME notice, .NET 8.0.32 MIT licenses and third-party notices copied from the runtime packs, FFmpeg notice, Inno Setup note), `build-info.json` |

Superseded / not for distribution (kept, not overwritten): `builds\FRAME-0.5.4-Beta-20261016-004355` (first release: same FRAME code, bundled .NET 8.0.8; Setup SHA-256 DB47AC01…F87D, payload 901390C9…; replaced only to update the runtime), `builds\FRAME-0.5.4-Beta-20261016-003140-INCOMPLETE-do-not-use` (build script stopped before checksums), `builds\FRAME-0.5.4-Beta-20261016-003245-SUPERSEDED-log-flush-race` (contained the log flush race fixed below). Release candidates and the 0.5.3 upgrade-test installer are under `work\rc`, `work\rc-old`.

Reproducibility: a second `dotnet publish` of the same source with the pinned runtime produced a byte-identical payload (`3D3B9086…`). For the first release a later run of the same script also produced a byte-identical payload (`901390C9…`); only Setup.exe differs (timestamps).

## Test environment and limits

No clean VM is available on this PC (no Hyper-V, no Windows Sandbox; enabling them needs a reboot and system changes). All tests ran on the development PC (Windows 11 Pro 25H2 build 26200, i7-12700, RTX 4090 + UHD 770, primary 3840×2160 at 175 %), with every FRAME run on isolated `--data` folders under `work\`. Consequences:
- "No developer dependencies" was shown by running the payload with `PATH` = System32/Windows only and an invalid `DOTNET_ROOT`: it started and rendered with its bundled runtime (.NET 8.0.32 for the final release; 8.0.8 for the first). A PC without the .NET SDK/runtime was not used.
- FFmpeg-missing was simulated with `--ffmpeg-only-settings` (search only the Settings path), because this PC has FFmpeg installed through winget.
- Shortcuts were verified (target, working folder) but not double-clicked: a normal launch would use the real `%LOCALAPPDATA%\FRAME` data, which the project rules forbid touching. The installed FRAME.exe was started directly with `--data`.

## .NET runtime update to 8.0.32 (user request, 2026-10-16)

The SDK on this PC (8.0.402) publishes self-contained apps with its own 8.0.8 runtime packs. The user approved bundling the newest 8.0.x servicing runtime and downloading the packs from NuGet.
- Latest version (nuget.org flat-container index, 2026-10-16): **8.0.32** for `microsoft.netcore.app.runtime.win-x64`, `microsoft.windowsdesktop.app.runtime.win-x64` and `microsoft.netcore.app.host.win-x64`.
- Pin: `src\Frame.App\Frame.App.csproj` `BundledRuntimeVersion` = 8.0.32, applied through `KnownFrameworkReference Update` (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App, .WPF, .WindowsForms: `LatestRuntimeFrameworkVersion`) and `KnownAppHostPack Update` (`AppHostPackVersion`), only when `SelfContained` is true. Framework-dependent development and test builds are unchanged. No FRAME source code changed.
- Signatures: `dotnet nuget verify --all` exit 0 for all three packages: author signature CN=Microsoft Corporation (certificate SHA-256 9A1B131B…B630, valid 2026-09-08 to 2027-12-04) and repository signature "NuGet.org Repository by Microsoft" (1F4B311D…482D). Package SHA-256: runtime 4B7AE168…, windowsdesktop 9DC00EC0…, host 32D48BDB….
- Proof in the payload: the startup log line of the release payload reports `.NET 8.0.32` (build smoke test, every test run below and the no-.NET run). `Build-BetaInstaller.ps1` now stops if this line does not report the pinned version and records `bundledRuntime` in build-info.json.
- Licenses: `packaging\licenses\dotnet-runtime-LICENSE.txt`, `dotnet-runtime-THIRD-PARTY-NOTICES.txt` and `windowsdesktop-runtime-LICENSE.txt` replaced with the copies inside the 8.0.32 packs (release copies byte-identical); `licenses\README.txt` names 8.0.32.
- Size: payload 168,344,585 bytes (8.0.8: 168,524,583); Setup 53,416,479 bytes (8.0.8: 53,351,147).

### Final verification of the 8.0.32 release (`C:\dev\Frame\builds\FRAME-0.5.4-Beta-20261016-162409`)

| Area | Check | Result |
|---|---|---|
| Build | `dotnet build Frame.sln -c Release` (warnings as errors; framework-dependent, so the pin does not apply) | 0 warnings, 0 errors |
| Core | `Frame.Tests` | 125/125 |
| UI | native UI suite (dark, light, minimum, hotkeys) + hotkey dialogs | 133/133 + 4/4 |
| Sidebar / display map | `--nav-test`, `--display-ui-test` | 18/18, 12/12 |
| Real capture | `--capture-test` (Shadow start, saves after still and motion, stop) | 11/11 |
| Media | `--media-test` (export, trim, Crop for social) | 36/36 |
| Hotkeys | `--open-hotkey-test` with real key presses, two runs | 14/14, 15/15 |
| Audio | `--audio-test` (synthetic audio, still periods, device loss/recovery, A/V) | 16/16 (5 `audio-interrupted` warnings logged by the device-loss steps, as intended) |
| Diagnostics | `scripts\validate-diagnostics.ps1` | crash (exit 0xE0434352, Critical line, `crash.json`, marker kept), harness 14/14, clean exit removes the marker, FFmpeg missing logged, unreadable settings kept and logged |
| Privacy | scan of the exported ZIP | no user/computer name, no profile path, no media files; summary.txt reports .NET 8.0.32 |
| Installer | `scripts\validate-installer.ps1`: 0.5.3 test installer → this Setup (upgrade) → uninstall | 15/15 |
| Wizard | `scripts\validate-wizard.ps1` | 6 pages, installed, "Start FRAME" unticked, no FRAME started by the wizard, uninstalled, nothing left running or registered |
| No .NET on PATH | payload with `PATH` = System32/Windows and an invalid `DOTNET_ROOT`, isolated `--data` (arguments checked before the start) | exit 0, rendered, startup line `.NET 8.0.32` |
| Reproducibility | second `dotnet publish` | byte-identical FRAME.exe |
| Real user data | fingerprint of `%LOCALAPPDATA%\FRAME` + `Videos\FRAME` (863 files) and HKCU Run "FRAME" before and after the whole run | unchanged; no FRAME or Setup left running |

All FRAME runs used isolated `--data` folders under `work\final3` (the no-.NET helper now builds a plain argument string and refuses to start unless it begins with `--data`). Wizard screenshots: `work\final3\wizard\wizard-*.png`.

## Results on the first release `…-004355` (.NET 8.0.8; FRAME code identical to the final release)

| Area | Check | Result |
|---|---|---|
| Build | `dotnet build Frame.sln -c Release` (warnings as errors) | 0 warnings, 0 errors |
| Core | `Frame.Tests` (incl. 3 new: log retention, redaction, session marker) | 125/125 |
| UI | native UI suite (dark, light, minimum, hotkeys) + hotkey dialogs | 133/133 + 4/4 |
| Sidebar / display map | `--nav-test`, `--display-ui-test` | 18/18, 12/12 |
| Real capture | `--capture-test` (Shadow start, saves after still and motion, stop) | 11/11 |
| Media | `--media-test` (export, trim, Crop for social) | 36/36 |
| Hotkeys | `--open-hotkey-test` with real key presses, two runs (Ctrl+Alt+F to primary display, Shadow hotkey start/stop while hidden, restore at start) | 14/14, 15/15 |
| Audio | `--audio-test` (synthetic audio, still periods, device loss/recovery, A/V) | 16/16 |
| Diagnostics | `scripts\validate-diagnostics.ps1`: real crash (`--crash-test`), harness 14 checks, FFmpeg missing, unreadable settings | all pass (details below) |
| Installer | `scripts\validate-installer.ps1`: 0.5.3 test installer → 0.5.4 upgrade → uninstall | 15/15 |
| Wizard | `scripts\validate-wizard.ps1`: interactive pages with screenshots, install, "Start FRAME" unticked, uninstall | completed; 6 pages (Welcome, Information, Destination, Tasks, Ready, Finish) |
| Privacy | scan of the exported ZIP | no user/computer name, no profile path, no media files |

### Installer, upgrade, uninstall (15/15)
Fresh install of the 0.5.3 test installer (payload = the earlier verified shadow-hotkey-primary build) with desktop shortcut: files, per-user Installed Apps entry (name, version, location, icon, uninstall string), Start-menu and desktop shortcuts → installed FRAME.exe; the installed FRAME starts. A running FRAME (main instance mutex) stops Setup with the "FRAME is running … Quit FRAME" message (log: EAbort), files unchanged. Upgrade without `/DIR` finds the same folder, replaces FRAME.exe with the 0.5.4 payload (hash), updates the Installed Apps entry, keeps both shortcuts. The test data folder (settings with Alt+Z, Start with Windows, capture folder; a recording file) is byte-identical after the upgrade, and the upgraded FRAME reads it (Shadow hotkey added as "not set"). Logs go to `<data>\Logs`, nothing is written to the install folder; no developer files (.pdb, .json, scripts, media) are installed. Uninstall removes program files, the folder, both shortcuts and the Installed Apps entry; the data folder and recording stay (the remove-data question defaults to No; with /SUPPRESSMSGBOXES it is answered No). The user's real `%LOCALAPPDATA%\FRAME`, `Videos\FRAME` and HKCU Run "FRAME" value (pointing to a portable build) were fingerprinted before and after: unchanged.

Wizard: modern style with FRAME welcome/finish artwork and the mark in the header; Information page shows `packaging\INSTALL_NOTES.txt`; Tasks page offers "Create a desktop shortcut" (unchecked); Finish page explains the FFmpeg requirement and offers "Start FRAME". During the test a FRAME with its own data held the instance mutex while Finish was pressed, so a started FRAME could not touch real data; the option was unticked and no FRAME was started.

### Diagnostics
- Crash: `--crash-test` throws on a background thread; the process ends (exit 0xE0434352), a Critical line with the stack is in `Logs\FRAME-*.log`, `Logs\crash.json` is written and the session marker remains. The next start logs "previous session ended without a normal exit", shows the note on the Capture page and the export contains `crash/crash.json` and `crash/unexpected-exit.json`. A clean exit removes the session marker.
- Harness (14/14): JSON line with UTC time, level, version, component, exception type and stack; start-up line; hotkey held by another window → Warning; encoder FFmpeg rejects → start fails, Error with FFmpeg's "Unknown encoder" text; capture folder that becomes unusable while recording → save fails, Error, Shadow Recording keeps running; crash policy; export contents (summary, README, logs, settings, FFmpeg error log), no media, redaction (profile path, user, computer, e-mail, token/password values), no custom share destination, summary with version/Windows/CPU/GPU/displays/FFmpeg/encoders/recent errors, export logged.
- FFmpeg missing: Error logged, Capture page panel (Install FFmpeg (winget) / Choose ffmpeg.exe… / Check again), status "FFmpeg needed".
- Unreadable settings (Fps 45): defaults used, the file kept as `settings.json.unreadable-<time>`, Error logged.
- Log volume (performance): 20–40 lines per multi-minute capture/audio/hotkey test run (startup, start/stop, saves, faults; nothing per frame or packet); writes happen on one below-normal background thread. Capture, audio and A/V sync suites pass unchanged.

### Fixed during this validation
- Log flush race: a `Flush()` running while the writer thread was mid-write returned early, so an export could miss the newest lines (found by the harness on the first final build). `Flush` now holds the file lock across take-and-write; 3 consecutive harness runs 14/14.
- UI checks that assumed 100 % display scaling (tray icon size, toast margin) failed at 175 %; the checks now use the display's scale (the code was correct).
- Build script: wizard images (GDI+ cannot read the PNG icon frame → WPF decoder), Setup version check (padded fields), unique folder per second, build-info serialization (PowerShell metadata, incl. a temp path) and UTF-8 read. The final release's `build-info.json` was rewritten after the fix (Setup.exe and FRAME.exe unchanged).
- Startup line said "Windows 10" on Windows 11 (registry ProductName); now derived from the build number.

## Incident during validation (2026-10-16 00:59): FRAME started once with the real user data

A test helper meant to start the release payload with `--data` and a stripped PATH used `ProcessStartInfo.ArgumentList`, which Windows PowerShell 5.1 does not have; the argument calls failed but `Process.Start` still ran, so `app\FRAME.exe` started **without arguments**, i.e. as a normal FRAME on the real `%LOCALAPPDATA%\FRAME` (found at 01:07, ~8 minutes later). Effects: it tried "Start shadow recording when FRAME starts" (the user's setting) and failed with "A selected display is disconnected" (the user's saved display selection no longer matches the current displays; the user's own auto-start would fail the same way), so nothing was recorded; `settings.json`, Recovery and Videos\FRAME were unchanged; it rewrote the HKCU Run "FRAME" value to its own path (restored to the previous value `"C:\dev\Frame\builds\FRAME-0.5.4-shadow-hotkey-primary-20261015\FRAME.exe" --background`); it created `Logs\FRAME-20261016.log` (kept) and `Logs\session.json` (removed, so no false "closed unexpectedly" note); it **overwrote `recording-status.json`** with that start failure (previous content not recoverable; FRAME will show "Last session: Shadow Recording stopped unexpectedly" once, dismissible). The process was stopped. Lesson: test helpers that start FRAME must abort on any argument-building error and always pass `--data`.

## Not verified
- A clean PC/VM without .NET, FFmpeg or developer tools; Windows 10; AMD and Intel-only GPUs (QSV/AMF are offered when detected but unverified).
- The real winget FFmpeg installation from the panel (not run: FFmpeg is already installed here, and it would change the PC); the command and the detection of the WinGet Links path are the same as before.
- Double-clicking the Start-menu/desktop shortcuts and a real sign-in with Start with Windows (both would use the real user data); the uninstaller's removal of a Run entry pointing into the install folder (the user's existing Run entry points elsewhere and was correctly left alone).
- The uninstaller's "remove settings and logs" Yes path (it would delete the real `%LOCALAPPDATA%\FRAME`).
- SmartScreen behaviour of the unsigned installer on another PC.
