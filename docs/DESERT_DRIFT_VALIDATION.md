# Desert Drift implementation and verification

Date: 2026-10-12. Source: `C:\dev\Frame`. FRAME **0.3.0**, FRAME Advanced **0.2.0**. All five implementation phases in `FRAME_CODEX_IMPLEMENTATION_PROMPT.md` have been executed. This is an engineering build with the hardware acceptance limits below; existing releases and `dist` were not overwritten.

## Changes and boundaries

1. Inspected both handovers, relevant context/source and the authoritative reference. Saved a local baseline under `work/baseline-20261012`. Ran an actual baseline build: **75 passed, 0 failed**, both apps built without warnings/errors. No Git repository was present.
2. Implemented deterministic replay history/dial and source/interval game classification in `Frame.Core/ReplayHistory.cs`, Windows metadata discovery in `Frame.App/ReplayGameTracker.cs`, and native input in `HistoryDialControl.cs`. Coordinator, settings and Library use these through narrow changes; the encoder/export pipeline remains shared. Removed only the repetitive main-app tray reminder.
3. Rebuilt the native FRAME shell against the approved image; applied shared warm tokens/background to Advanced's separate layout. Fixed root-background/light-theme rendering, action font inheritance and minimum-size spacing after inspecting real WPF output.
4. Added persisted reduced motion (static by default), frozen image/brush resources and optional 1 Hz drift with explicit lifetime gates. Functional capture and Advanced sampling remain separate.
5. Built both apps, ran regression/native/synthetic checks, inspected render output and updated design/changelog/all three handover files with current evidence.

Main source groups: `Frame.App/MainWindow.xaml(.cs)`, `App.xaml`, `SettingsView.cs`, `LibraryView.cs`, `CaptureCoordinator.cs`, `MediaValidation.cs`; new `HistoryDialControl.cs`, `ReplayGameTracker.cs`, `DesertAppearance.cs`, `AtmosphereMotion.cs`, `UiValidation.cs`; `Frame.Core/ReplayHistory.cs` and `Models.cs`; Advanced window/styles/preview accents, `AdvancedSettings.cs`, both app projects; `tests/Frame.Tests/Program.cs`; `assets/desert-drift.png`. [Changed-file manifest](validation-2026-10-12/changed-files.txt) records exact files relative to the saved baseline.

## Executed verification

All runtime diagnostics were serial, used isolated settings **and** output paths under `work/validation-20261012`, exited normally and opened no physical audio endpoint. FFmpeg generated video and a synthetic sine stream; nothing was played through audio hardware. User media/settings/recovery were not test fixtures.

| Check | Result | Evidence / scope |
|---|---|---|
| `Build.ps1`, .NET SDK 8.0.402 | 0 warnings, 0 errors; **99 passed, 0 failed** | [Build and tests](validation-2026-10-12/build-and-tests.txt); both executables, existing 75 checks plus 24 new |
| Native FRAME checks | **21 passed** | [UI report](validation-2026-10-12/ui-validation.json); pointer capture/preview/release, keyboard/automation, persistence/rollback, navigation, theme, close-to-tray, actual motion run/stop, minimum layout |
| Hotkey dialogs | **4 passed** | [Dialog report](validation-2026-10-12/hotkey-dialog-tests.json); actual WPF dialogs with programmatic input, not physical keys or global registration |
| Synthetic media | **9 passed** | [Media report](validation-2026-10-12/media-validation.json); trim, pause/resume, empty/gap rejection, source preservation, game routing/fallback, encoder probes |
| Advanced native diagnostics | All four assertions true in each render | [Dark report](validation-2026-10-12/advanced-dark.png.json); inactive detection/recording, disabled Activate without selection, hidden decoration stopped |
| Native visual review | Both themes/apps and minimum sizes inspected | Images below; 150% is offscreen rasterization, not physical mixed-DPI testing |
| Idle process sample | 10.0009 seconds; **0.0391% CPU**, 148.64 MB working set, 86.39 MB private | [Idle report](validation-2026-10-12/idle-benchmark.json); CPU normalized by logical processors, two displays, no capture, static background, **zero registered hotkeys in this restricted diagnostic** |

The routed Fortnite fixture passed verification and full-frame decode, and Library discovery included it. A blocked CS destination preserved the verified root clip. Pause/resume duration was 1.9427 seconds for 1.9 seconds requested; fractional replay duration was 2.0213 seconds for 2 requested. Probes offered NVIDIA H.264/HEVC/AV1, Intel H.264/HEVC and CPU H.264. These are initialization checks, not capture-throughput qualification.

The native tray test verifies Hide leaves the coordinator available; it does **not** record a live game while hidden. The dial lock test exercises read-only behavior without starting capture. Existing state-machine/coverage tests and synthetic exports cover related logic separately.

The final native input audit also verified minimum-knob pickup across zero and Escape cancellation; an endpoint jump was fixed. Unchanged history ticks no longer invalidate the dial. [Integrity audit](validation-2026-10-12/integrity-audit.json) confirms 25 XAML handler references resolve, the approved image is unchanged, and 11 critical engine/audio/export/screenshot/detection source files match the baseline byte for byte.

## Native images

These are application renders, not generated UI mockups. Only the independent abstract background asset was generated.

| Surface | Image |
|---|---|
| FRAME dark | [1280 x 800](validation-2026-10-12/main-dark-final.png) |
| FRAME light | [1280 x 800](validation-2026-10-12/main-light.png) |
| FRAME minimum | [930 x 690](validation-2026-10-12/ui-validation.minimum.png) |
| FRAME minimum, 150% rasterization | [1395 x 1035](validation-2026-10-12/main-minimum-150.png) |
| Library with root and routed synthetic clips | [Library](validation-2026-10-12/library-final.png) |
| Advanced dark / light | [Dark](validation-2026-10-12/advanced-dark.png), [light](validation-2026-10-12/advanced-light.png) |
| Advanced minimum | [680 x 640](validation-2026-10-12/advanced-minimum.png) |

## Routing contract and deliberate limits

Exact known executable names only: `FortniteClient-Win64-Shipping.exe` -> `Fortnite`, `cs2.exe` -> `CS`, plus Dota 2, Rocket League and Apex Legends. Window capture requires exact HWND; display/region attribution requires the known foreground game window to cover at least 95% of the selected area. Combined sources must agree. The existing 1 Hz maintenance tick stores bounded metadata (at most 4000 samples / about an hour); a foreground-change epoch detects even short alt-tabs between samples. No OCR, frame analysis, cloud calls or process-list polling is added.

Every sample bracketing the **saved interval** must agree on game and epoch; missing/stale samples, desktop/launcher/browser, multiple games or source ambiguity save to root. This is intentionally conservative. Switching to FRAME to click Save may cause root fallback; use the configured global Save hotkey after enough uninterrupted game history for practical attribution testing. A missed maintenance sample during export can likewise cause later root fallback until that gap ages out.

Only verified Shadow Replay files route. Root export is retained while a destination copy is flushed and atomically committed; failures keep the root file. Filenames retain source labels, collisions preserve both clips. Only canonical safe direct folders are allowed. Existing owned folders are reused; empty folders can be marked. A nonempty unmarked folder is not adopted or overwritten: the clip stays in root. Ownership uses `.frame-replays` with exact signature `FRAME replay folder v1`; symlink/reparse folders are skipped. Library includes root media and owned direct game-folder MP4s, including renamed clips after restart; it excludes `Advanced`, unrelated folders and deeper descendants. No migration of old recordings was performed.

Manual recordings/screenshots and all Advanced outputs keep existing destinations. Advanced retains separate settings, mutex, recovery, audio-off policy, monitoring and Quit behavior.

## Launch fresh binaries and hardware acceptance

Use these builds, keeping adjacent runtime files in place; `Run.ps1` / `Run-Advanced.ps1` can launch an older `dist` copy:

- `C:\dev\Frame\src\Frame.App\bin\Release\net8.0-windows\FRAME.exe`
- `C:\dev\Frame\src\Frame.Advanced\bin\Release\net8.0-windows\FRAME.Advanced.exe`

Before interactive launch, quit an older FRAME through its tray menu; X only hides it. No processes were force-stopped by this work.

1. With audio off, select a short history while stopped. Check drag, arrow keys, Save label and restart persistence. Start shadow: adjustment locks with an explanation; Save reports actual available continuous history. Check manual Start/Pause/Resume/Stop and screenshot/annotation normally.
2. Select the display/window showing Fortnite, then CS2. Play uninterrupted until the selected interval is entirely game footage; save through the configured hotkey. Expect `Fortnite` / `CS` and a playable clip in Library. Alt-tab or span games: expect root fallback. Restart, search/filter, rename/open/copy/recycle a disposable test clip in Library.
3. Check tray restore/Quit while capturing, real global hotkeys/conflicts, caption hover/focus, drag/Snap/maximize and physical 100/125/150% DPI across monitors. Programmatic diagnostics do not substitute for these checks.
4. Run Advanced separately: selection/Refresh, Activate arms without recording, motion/stills into its destination, Preview, Deactivate, screenshot modes and close/finalization. GPU windows can still return blank `PrintWindow` frames; this pre-existing limitation is unchanged.
5. Compare the same repeatable game scene with FRAME off, shadow on and FRAME in tray; record average FPS, 1% lows and frame times. Compare static/default versus optional motion while visible. The ten-second idle sample is **not** a game-FPS guarantee or long-run leak test. Long sessions, HDR, mixed-display sync, 120 FPS, sleep/device loss and physical audio remain unqualified. Physical audio testing requires separate explicit agreement.
