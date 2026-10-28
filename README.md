# FRAME

FRAME is a Windows screen recorder with disk-backed Shadow Recording, manual recording, a clip library and trimming. Save the last moments of your selected displays without starting a separate recording beforehand. [Download setup](https://github.com/valbergProd/Frame/releases/download/v1.0.0/FRAME-1.0.0-Setup.exe).

**Current release: 1.0.0.** Windows x64.

## Features

- Shadow replay and manual recording with pause/resume.
- Primary-display default, remembered display choices and startup preferences.
- Desktop and microphone audio; hardware encoder selection when supported.
- Clip library, trimming and export framing.
- Five visual modes: Original GUI, Dark, Pink, Light and Emo.
- Update checks and user-initiated download/installation from this repository's Releases.

## Requirements and build

Use Windows x64 and the .NET 8 SDK to build. FRAME Setup downloads and installs verified FFmpeg recording tools automatically; internet access is required for the first installation. FFmpeg binaries are downloaded directly from their publisher and are not embedded in Setup or stored in this repository. Standalone developer builds still need FFmpeg 8.1+ with `gfxcapture` and a matching `ffprobe.exe`. A self-contained FRAME build includes the .NET runtime.

```powershell
.\Build.ps1
.\Publish.ps1 -SelfContained -OutputDirectory builds\my-new-build
.\builds\my-new-build\FRAME.exe
```

Always use a new output folder. Settings and diagnostic logs live in `%LOCALAPPDATA%\FRAME`; recordings use the folder chosen in the app. Closing FRAME exits; minimizing keeps it in the tray.

## Releases and updates

Installer releases are available on the [Releases page](https://github.com/valbergProd/Frame/releases). In the app, open **Settings > General > Check for updates**. Downloads and installation require your choice; recording must be stopped before installing.

[Update and release workflow](docs/UPDATES.md) explains how a reviewed code change becomes an installable update. A Git push alone never distributes a new executable.

## Project layout

- `src/Frame.Core`: settings, policies, replay rules and update validation.
- `src/Frame.App`: WPF application, Windows capture/audio integration and exports.
- `tests/Frame.Tests`: deterministic regression tests, run by `Build.ps1` and GitHub Actions.
- `assets`: application images and icons required by the build.
- `scripts` and `packaging`: installer, validation and release tooling.
- `docs`: validation reports, design history and release notes.

See the [changelog](docs/CHANGELOG.md), [setup validation](docs/FRAME_068_VALIDATION.md), [updater validation](docs/FRAME_066_VALIDATION.md), [audio validation](docs/FRAME_065_VALIDATION.md) and [stop/shutdown validation](docs/FRAME_064_VALIDATION.md). Tests run in isolated folders under `work`; never use a user's settings or recordings as fixtures. Desktop capture, hardware and installer acceptance still require local Windows testing.

Older documentation is retained as dated history, including the [previous README](docs/HISTORICAL_README_035.md). Local handover notes, builds, installed test copies and generated recordings are not versioned.
