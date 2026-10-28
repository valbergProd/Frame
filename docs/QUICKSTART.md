# FRAME quickstart

Current release: 1.0.0. See [setup validation](FRAME_068_VALIDATION.md) and the [changelog](CHANGELOG.md) for current status. Older reports describe historical builds.

1. Start `FRAME.exe`. A self-contained build includes the .NET runtime; a framework-dependent build requires the .NET 8 Windows Desktop Runtime, x64.
2. FRAME Setup prepares FFmpeg automatically (internet required on first install). For standalone developer builds only, install FFmpeg 8.1+ with `gfxcapture` and a matching `ffprobe.exe`; choose it in Settings if it is not detected.
3. Choose the capture folder and displays. Primary display is the default; deliberate secondary/multiple-display choices are remembered. Desktop audio defaults on, microphone is opt-in.
4. Choose replay length and start Shadow Recording. Save the last moments with the Capture button or your configured replay hotkey. Only buffered history can be saved. Save wanted clips before stopping Shadow.
5. Use manual recording for a selected interval, with pause/resume and Stop. Closing FRAME exits and finalizes work; minimizing keeps it in the tray.
6. Use Library to open or trim saved clips. Settings > Hotkeys shows your actual saved shortcuts.
7. Settings > General provides **Check for updates**. Startup checks can be disabled. Downloads and installation require your choice; stop recording and close editors before installing.

Settings and logs live under `%LOCALAPPDATA%\FRAME`. The installer preserves these and your recordings when upgrading. A standalone build uses the same settings; avoid running two normal copies at once.

Download the installer from [GitHub Releases](https://github.com/valbergProd/Frame/releases/tag/v1.0.0). See [the release workflow](UPDATES.md) for publishing future updates. Physical-device and broader hardware acceptance remain separate from automated tests.
