# Unexpected-exit popup and Capture scroll-bar fix — validation

Date: 2026-11-27. Source version 1.0.0 (unchanged).

## Implemented
- `UnexpectedExitCard` (a Border inside the main window, top right below the title bar, SaveOverlay look; not a separate window) for the unexpected-exit note; Export diagnostics / Dismiss keep the previous semantics.
- In-page `UnexpectedExitNote` removed. Cinematic column scroll viewer reserves 10 DIP for the scroll bar (App.xaml scroll bar is 8 DIP) so notes that force scrolling no longer clip the column.

## Verified
- `dotnet build` Release: 0 warnings/errors. Core tests: 137 passed, 0 failed.
- Isolated off-screen renders (`--data work\scrollfix-20261127\data`, seeded `Logs\unexpected-exit.json`): Capture page shows no note, hotkey rows complete; popup visual rendered (`p-notice.png`).

## Not verified (needs a person)
- Real on-screen placement, clicking Export diagnostics (Save As dialog) and Dismiss, multi-monitor/DPI, `scripts\validate-diagnostics.ps1`.

## Build
`C:\dev\Frame\builds\FRAME-1.0.0-unexpected-exit-popup-20261127\FRAME.exe`

## Settings note spacing (same day)
Footer note top margin 24 -> 48; off-screen render of Settings with Advanced expanded checked. Build: `C:\dev\Frame\builds\FRAME-1.0.0-settings-note-spacing-20261127\FRAME.exe`

## Ground sand and Dark rename (same day)
CinematicDust (120 grains) under the Cinematic D stone; mode name Dark. Core tests 137/137; deterministic off-screen renders checked; CPU/GPU cost not measured; not seen live. Build: `C:\dev\Frame\builds\FRAME-1.0.0-ground-sand-dark-20261127\FRAME.exe`

Final build for this series: `C:\dev\Frame\builds\FRAME-1.0.0-window-popup-sand-20261127\FRAME.exe` (card locked inside the window; sand grains smaller and faster). Verified by off-screen renders in Original, Pink, Light(not re-read) and minimum size; Core tests 137/137. Not seen live.
