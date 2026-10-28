# FRAME 0.6.1 · five GUI modes, direct mode links · validation (2026-10-17)

## Build

| Item | Value |
|---|---|
| Folder | `C:\dev\Frame\builds\FRAME-0.6.1-beta-five-modes-20261017-122907\` |
| Executable | `FRAME.exe`, 182,024,201 bytes, SHA-256 `FD8B6A1989BD69F51500EBB312286685DCDC258F4A56F6D3B5A1082952E1D7CD` |
| Version | 0.6.1.0, product version 0.6.1-beta; self-contained single file, win-x64, .NET 8.0.32 (smoke run startup line: `FRAME 0.6.1 beta · … · .NET 8.0.32`) |
| Installer | none (not requested) |

## Changes (user spec "Simplify GUI Modes & Theme Navigation", deliberately small)

- Core `VisualModes.cs`: exactly five modes `Original, Dark, Pink, Light, Emo`; `VisualMode.Original` = the former Cinematic D design (default). The 0.5.5 design is no longer a mode. `Parse` maps both former keys (`"cinematic-d"` from 0.6.0, `"original"`) to Original GUI; at start a saved `"cinematic-d"` is rewritten as `"original"` (`ModeView.StartupNeedsMigration`). `Cycle`, `Next`, `ShowsOriginalShortcut` removed (`All` lists the five). Other saved choices (Pink/Emo background, effects, quote rotation) untouched.
- `ModeView.cs`: Original GUI uses `CinematicCaptureLayout` + its scene and the desert; every mode now has a layout, so the 0.5.5 HomePage is never shown. It stays in `MainWindow.xaml` hidden, because it is the state holder `CaptureSnapshot` reads and the harnesses use (removing it would mean rewriting UpdateState and the tests — not safe for this round). Navigation always uses `ModeNavigationButton`.
- Title bar: the cycle button and the separate "Original GUI" shortcut are replaced by five text links (`ModeLinks`, built by `BuildModeLinks`): Original GUI · Dark / No Bullshit · Pink mode · Light mode · Emo mode; one click activates the mode; the active link has the brighter Text colour (`Tag="Active"` in the `ModeLink` style, labels bound to the button Foreground so the app's implicit TextBlock style cannot hide the marking), others Muted; no dropdown, tooltip or confirmation. The "· <mode>" text after FRAME in the title bar is no longer shown (the links show the active mode).
- `ModePalettes.cs`: Original GUI gets the former Cinematic D palette; the 0.5.5 palette branch was removed (`DesertAppearance.Apply` still exists, only no mode uses it).
- Tests/harnesses: Core mode test rewritten (five modes, migration of both former keys); `--mode-switch-test` clicks the title-bar links; the UI-suite mode checks now cover the five links (one active, saves only the mode, links fit left of the window buttons at normal and minimum size).

## Checks (targeted, as requested)

| Check | Result |
|---|---|
| `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors |
| Core tests | 137/137 |
| Renders of all five modes (1280×800) + Original GUI at the minimum 930×690 | five links shown in every mode, no overlap with the window buttons; active link measured brighter/darker than the others (Original, Light, Pink) |
| `--mode-switch-test` (real capture of FRAME's test window) | 12/12: 10 switches with the links + Original GUI from Emo while recording, same FFmpeg processes, buffer kept growing, save holds pre-switch footage, settings unchanged, manual recording from the Dark layout, decoration stops in the tray in every mode and resumes without catch-up |
| Migration | settings with `VisualMode "cinematic-d"`, Pink background/effects → opens Original GUI, file rewritten to `"original"`, Pink choices kept |
| Published exe smoke run | renders, startup line 0.6.1 / .NET 8.0.32 |

Not run this round (user asked for a lightweight round): the full native UI suite and the real-capture regression suites. Note: the native UI suite's 0.5.5 Capture-page checks (`ValidateDesertUi`) measure HomePage controls, which are now hidden; they need to be rewritten for the Original GUI (Cinematic) layout before that suite is used again. Not verified by a person: real mouse use of the links.

Deferred by the user (do not work on them unless asked): Pink all-effects CPU cost and the pre-existing replay audio peak overshoot (see docs/FRAME_060_VALIDATION.md).
