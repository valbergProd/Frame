# FRAME 0.6.8 Beta · release notes

Windows 10 2004+ / Windows 11, x64. Per-user setup; no administrator rights.

## Installation

Setup now downloads and verifies FFmpeg 8.1.2 recording tools automatically.
Users can start FRAME after setup without a separate FFmpeg installation.
The first installation needs internet access. Upgrades reuse an identical,
verified private copy. Failed downloads can be retried before FRAME files
are changed. Settings and recordings are preserved. Uninstall removes the
recording tools installed with FRAME, leaving other FFmpeg installations alone.

## Included improvements since 0.5.4

- Primary-display default with remembered display and startup preferences.
- Bounded shutdown and reliable Stop when the captured display is still.
- Replay audio join and trim timing fixes.
- Five visual modes and in-app update checks from valbergProd/Frame.
- Shadow Recording, manual pause/resume, Library, trimming and Crop for social.

## Known limits

This is a beta, tested on one NVIDIA Windows PC. Broader hardware, Windows 10,
long sessions and physical sleep/lock acceptance remain separate work. The
installer is not code-signed, so Windows SmartScreen may show a warning.
A published-release-to-newer-release upgrade still needs end-to-end acceptance.

See BETA_README.md and licenses/FFMPEG-NOTICE.txt for setup and third-party details.
