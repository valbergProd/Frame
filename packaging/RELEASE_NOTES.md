# FRAME 0.5.4 Beta · release notes

First public beta with a Windows installer. Version 0.5.4 (file version 0.5.4.0), Windows 10 2004+ / Windows 11, 64-bit.

## Main features

- **Shadow Recording:** keeps the last 1–60 minutes of a display, window or region and saves them with a hotkey (default Ctrl+Shift+S), the *Save last …* button or the tray menu. The length can be changed while recording.
- **Manual recording** with pause and resume.
- **Library** with thumbnails, search, sorting, a **trim editor** and **Crop for social** (local, formatted copies for vertical, square and other formats; nothing is uploaded).
- **Display selection** with clickable monitor cards that follow your Windows arrangement, plus window/game and region capture.
- **Audio:** desktop audio (on by default) and microphone (opt-in), following your Windows default devices; recording continues if a device disappears.
- **Hotkeys** for saving, Shadow Recording on/off (set your own), manual recording, pause, opening the capture source and **opening FRAME on your primary display** (Ctrl+Alt+F).
- Runs in the **system tray**, optional **start with Windows** (with Shadow Recording starting automatically), and optional hiding when a game starts.

## New in this beta

- **Windows installer** (per-user, no administrator rights), Start menu and optional desktop shortcut, clean upgrade and uninstall.
- **Diagnostics:** FRAME keeps local logs (`%LOCALAPPDATA%\FRAME\Logs`) and, from Settings › Advanced, **Export diagnostics** creates a ZIP with redacted logs and system details for a bug report. Nothing is uploaded; no telemetry.
- **Crash handling:** fatal errors are logged before FRAME closes, and the next start offers *Export diagnostics*.
- **FFmpeg assistant:** if FFmpeg is missing or too old, the Capture page explains it and can start the winget installation for you (visible, only when you click).

## Recent improvements (0.5.4 development)

- Audio/video sync fixed (sound was about half a second late in replays).
- Replays save 3–4 times faster (stream copy instead of re-encoding), with exact start and end.
- Audio interruptions (a still screen, a removed headset) no longer stop Shadow Recording.
- Redesigned Capture page (obsidian stone, minute slider, compact controls), multi-monitor map fixes, collapsible Settings sections, Settings › Start-up near the top, Apply settings shown only when needed.

## Known issues and limitations

- Verified on one NVIDIA PC (Windows 11). AMD, Intel-only and Windows 10 PCs are untested; Intel Quick Sync and AMD encoders are offered when detected but unverified.
- FFmpeg is not included (license); FRAME guides the installation.
- Exclusive-fullscreen games can hide the save note; the clip is still saved.
- Stopping while the captured screen is completely unchanged can take a few seconds.
- Combined multi-display recording is less tested than single displays.
- Not code-signed: SmartScreen warnings are expected.

See BETA_README.md for installation, usage, troubleshooting and how to report bugs.
