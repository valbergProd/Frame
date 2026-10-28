# FRAME 0.6.0 · visual modes (Cinematic D, Original GUI, Dark / No Bullshit, Pink, Light, Emo) · validation (2026-10-17)

## Build

| Item | Value |
|---|---|
| Folder | `C:\dev\Frame\builds\FRAME-0.6.0-beta-visual-modes-20261017-112255\` |
| Executable | `FRAME.exe`, 182,024,201 bytes, SHA-256 `8FD6FB7510A12602CA4E35185A0CA8B3712DA4187868CFE249A01B0826E1D03C` |
| Version | 0.6.0.0, product version 0.6.0-beta (csproj Version 0.6.0) |
| Runtime | self-contained single file, win-x64, bundled .NET 8.0.32 (startup line of the smoke run: `FRAME 0.6.0 beta · Windows 11 Pro 25H2 (build 26200.9711) · x64 · .NET 8.0.32`; all six modes rendered from the published exe) |
| Build | `dotnet build Frame.sln -c Release` 0 warnings, 0 errors (warnings as errors) |
| Installer | none (not requested). 0.5.5 build and the 0.5.4 Beta installer untouched. |

## How the work was done

User spec "Cinematic D, Multi-Theme System & Interactive Visuals" plus the Alternative D reference image (`work\r2-int\cinematic-d-reference.png`). Rollback snapshot of the 0.5.5 source: `work\r2-base`. Phase 0 (main agent, in the repository): mode model in Core, host side of the layout/scene contract, palettes, title-bar mode buttons, removal of the floating manual-recording bar and the Light appearance setting, the plain layout used by Dark / Light / Pink / Emo, harnesses. Merge base for the agents: `work\r2-phase0`. Four agents in isolated copies with file ownership: A Cinematic D (`work\r2-agentA`), B Pink world (`r2-agentB`), C Pink dogs (`r2-agentC`), D Emo (`r2-agentD`). All four stopped twice at the account session limit (05:00 and ~10:00); their copies built and their own evidence was complete except final reports, so the main agent took over: reviewed the evidence, merged the owned files (no conflicts: shared-file changes were only the registration lines in `ModeView.cs` and one additive `PinkWorld.Gust` member) and re-verified everything on the integrated build.

## Implemented

| Area | Implementation (main files) |
|---|---|
| Mode model | Core `VisualModes.cs`: `VisualMode` (CinematicD, Original, Dark, Pink, Light, Emo), cycle, names, stable keys, `Decorative`, `ShowsOriginalShortcut`; `PinkOptions`, `EmoOptions` (keys, first-entry defaults, toggles); `EmoQuotes` (the exact ten quotes, 10-minute interval, shuffle bag). Settings: `VisualMode` (null/unknown = Cinematic D, so new and 0.5.5 installations start in Cinematic D), `PinkBackground`, `PinkEffects` (null = never entered), `EmoBackground`, `EmoEffects`, `EmoQuoteLast`, `EmoQuoteBag`; `LightTheme` and `Toolbar` removed (old files load, keys dropped at the next save). Not capture settings (`CaptureSettings.Differ` unchanged). |
| Host / contract | `CaptureLayouts.cs`: `CaptureView` (snapshot of everything a layout shows), `ICaptureCommands` (the same paths as the Original controls: RequestShadowToggle, Act(ToggleRecord), PauseResume, RequestSaveReplay, CommitHistory, the quality combo path, audio, hotkey editor, capture folder), `ICaptureLayout`, `IDecorativeClient`, `IModeScene`, `ModeContext` (settings update, seed, UiRects). `ModeView.cs`: `ApplyMode` (palette, scene, layout, desert on/off, sidebar style, title bar), persistence, `CaptureSnapshot` (read from the Original controls UpdateState keeps current: one source of truth), re-parenting of the shared status elements (FailurePanel, EnginePanel, UnexpectedExitNote, ReplayInfo, display cards) into the active layout. `ModePalettes.cs`: complete brush set per mode (+ SidebarBackground, PageScrim, Panel, Recording, CloseGlyph, HeadingFont). |
| Mode switch | Title bar (`MainWindow.xaml`): "FRAME · <active mode>" on the left; right: "Original GUI" shortcut + cycle button naming the next mode (`ModeLink` style, no tooltip, keyboard focus underline). Instant, saved at once, open page and scroll position kept. The shortcut is hidden in Original GUI and in Cinematic D (whose cycle button already reads "Original GUI": two identical buttons side by side avoided — a deliberate deviation from the literal spec). |
| Decorative clock | `CaptureAtmosphere.cs`: `Desert` flag (sand, dune light and waves only in Original GUI and Cinematic D), clients (`Attach`/`Detach`, ~30 updates/s or `FullRate` every composition frame), Original's stone advanced only while shown, `IsOnScreen`/`OnScreenChanged`. `DecorativeVisibility.cs`: NEW — another input desktop (secure screen saver, lock, UAC; `EVENT_SYSTEM_DESKTOPSWITCH` + `OpenInputDesktop` name) counts as obscured (found during this task: the 0.5.5 clock kept running behind the user's DigitalRainSaver). |
| Cinematic D (A) | `CinematicCaptureLayout.xaml(.cs)`, `CinematicStone.cs`, `CinematicReplaySlider.cs`, `CinematicScene.cs`. Large stone left-centre (own high-resolution decode of the existing art), title/subtitle right, horizontal replay slider with every real choice (1, 2, 3, 5, 10, 15, 30, 60 min + custom), quiet ▷ manual-recording text control with timer/pause slots reserved, "Save last …" with history icon, hotkey chips + Change hotkey, bottom panel (display popup with the shared cards, quality menu with preset name, mic icon + audio checkboxes), fixed notes slot. One light state (activation crossfade × ~2.6 s breath, rotation-dependent fissure flicker) drives fissure ember, rim/contact light around the whole silhouette, halo/spill, ground light pool and faint reflection, and warm highlights on title and slider. 40 s per turn. Desert of 0.5.5 reused unchanged; `CinematicScene` adds a generated rocky ground plane in perspective under the stone (no new image files), revealed by the stone's light pool. |
| Original GUI | The 0.5.5 HomePage, stone, vertical minute slider and desert, unchanged (files `ObsidianStone.cs`, `HistorySliderControl.cs`, App.xaml `ObsidianAction` untouched except the 0.6.0 `Shown` property on ObsidianStone). Only additions: title-bar mode name/buttons. |
| Dark / No Bullshit, Light | `SimpleCaptureLayout.cs` with the neutral palettes: plain button, horizontal slider (WPF Slider over the same discrete values), text-only manual recording, shortcut rows, notes, bottom bar (display popup, quality, audio; audio stacks at narrow widths). No scene, no desert, no clock. |
| Pink mode (B, C) | `PinkScene.cs` (world, shared wind field with gusts, background, layers in z-order, readability wash, pointer, keep-out from `UiRects`, Tools: background picker with thumbnails + seven effect toggles), `PinkEffects.cs` (hearts, flowers with occasional red, sakura, sparkles, butterflies, ribbons/bows), `PinkArt.cs`, `PinkSecrets.cs`, `PinkDogs.cs` + `PinkDogRig.cs` (three articulated vector dogs). Pink uses `SimpleCaptureLayout` with the pink palette. |
| Emo mode (D) | `EmoScene.cs`, `EmoArt.cs` (four procedural illustrated backgrounds, cached bitmaps; side-swept-fringe figure motif), `EmoEffects.cs` (dark rose petals, rain, bats, fog), `EmoQuote.cs` (hero quote: reserved height for the longest quote, fade ~0.45 s, rotation on entry and every 10 min of on-screen time, one-shot timer paused while not on screen). Uses `SimpleCaptureLayout` (quote in HeroHost) with the Emo palette (Palatino Linotype headings). |
| Removed | Floating manual-recording toolbar ("On-screen controls") + setting/row/tooltips/wiring (`ShowToolbar`, `toolbar`, `GameMinimize` exclusion); "Light appearance" row and preview code (`ShownLightTheme`, `PreviewTheme`, `ApplyTheme`). Manual recording unchanged (Capture page, hotkeys, tray, finalized on stop/exit). |
| Diagnostics | `--mode <key>` (show a mode without saving it), `--seed <n>`, `--mode-switch-test <json>` (`ModeHarness.cs`), `--cinematic-states <json>`, `--pink-effects all|none|list`, `--pink-background`, `--pink-picker-open`, `--pink-report <json>`, `--pink-secret`, `--pink-bench`, `--emo-quote-seconds <n>`, `--emo-quote-test <json>`, `--emo-perf <json>`. Scripts: `work\r2-int\render-modes.ps1`, `run-harness.ps1`, `clip.ps1`, `validate-ui.ps1`, `run-real-tests.ps1`, `measure-all.ps1`. |

## Cinematic D vs Original GUI

Cinematic D: compact two-column composition (large stone left, title/slider/actions right, one bottom panel), horizontal labelled slider, manual recording as a text control, hotkey chips, accent-bar navigation and a tinted sidebar, unified stone lighting with ground light. Original GUI: the 0.5.5 layout (stone beside the title, vertical minute slider left of it, folder buttons under the title, manual/save buttons, separator line, expandable display map and quality combo), corner-frame navigation, 0.5.5 stone halo.

## Assets

Used unchanged: `assets/1.png`, `2.png` (stone), `desert-drift.png`, `Sakura_garden.png`, `pink_mode(1..4).png` (embedded as `Assets/pink/*.png`, 1672×941, UniformToFill). New artwork: none as image files — dogs, butterflies, hearts, flowers, petals, ribbons, sparkles, secrets, the four Emo backgrounds and Emo effects are vector/procedural drawings in code. Note: `pink_mode(1).png` shows a character resembling Jinx (Arcane / League of Legends, Riot Games); used as supplied, flagged as a copyright/trademark risk before public distribution.

## Pink effects and interactions (implemented)

Shared wind field (gusts) moves sakura, flowers, hearts and ribbons together. Butterflies flee from dogs and land on flowers when Flowers is on. Dogs chase butterflies (leap when low), jump for low drifting hearts (`heart-caught` → the heart pops, sparkle burst when Sparkles is on), snap at petals, play together (chase, play-bow), look at and approach the pointer near the ground line, sit/lie down/sleep after calm (Z's) and wake on pointer/hearts/treat. Flowers + hearts occasionally form a heart-shaped swirl (`heart-swirl`). Secrets (no tooltip, only in free areas, hit-testable spots only): a tiny heart in the empty sidebar middle (heart fountain; butterflies come), a heart-shaped cookie on the ground line right of the content (dogs run to it, eat it: `secret-treat`/`treat-eaten`), a faint star in the free sky (shooting star and star shower). Effects pause on Library/Settings (the scene stays as a still background under the scrim).

## Results

| Suite | Check | Result |
|---|---|---|
| Build | `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors |
| Core | `Frame.Tests` (4 new: default/cycle/names/keys, persistence without capture changes, Pink/Emo defaults and toggles, exact quotes and shuffle bag; 2 updated for the removed settings) | 137/137 |
| UI | native UI suite (`work\r2-int\validate-ui.ps1`: the 0.5.5 checks against Original GUI plus 3 mode checks — six distinct complete palettes with the page kept and the title bar naming mode and next mode; no clock/desert/scene in Dark/Light; the cycle saves only the mode and the Original GUI shortcut restores the 0.5.5 palette; the 7 Light-appearance checks were replaced) | 139/139 + hotkey dialogs 4/4 |
| Navigation | `--nav-test` (Original GUI) | 18/18 |
| Displays | `--display-ui-test` with 3 fake displays (primary listed second) and `--fake-selected 1` | 20/20 |
| Hotkey labels | `--hotkey-names-test` | 8/8 |
| Mode switching | `--mode-switch-test` (`ModeHarness.cs`; real capture of FRAME's test window, integrated build) | 12/12 · 12 switches + Original GUI shortcut while recording: same FFmpeg processes, buffer kept growing (frames 288 → 511), a save afterwards holds the footage from before the first switch; capture settings, replay length and hotkeys unchanged; manual recording from the Dark layout finalized and playable; Dark/Light run no clock; every mode stops all decorative updates when minimized to the tray and resumes without catch-up (largest step 0.046–0.05 s) |
| Cinematic D states | `--cinematic-states` (11 states: idle, starting, active, saving, 15 min, custom 45 s, manual, manual paused, stopping, quality/audio/source change, engine check) | 0 DIP movement of every element at 1280×800 (integrated build) and at 930×690 (agent A's copy, identical layout code; the integrated minimum run ended before its probe wrote) |
| Pink | `--pink-report` (integrated, all effects) | 3 secrets placed, hit-testing reaches each, none over FRAME UI; 7 effect toggles, no tooltips; Sakura garden decoded at 1472 px for a 1280 DIP window |
| Emo quotes | `--emo-quote-seconds 2 --emo-quote-test` (integrated) | entry shows a new quote; interval changes; hidden: timer stopped, 0 changes; resume: exactly one change after the kept remainder; 30 re-entries without immediate repeats, full rounds; one reserved height for all ten quotes |
| Capture layout, dot, level (Original GUI) | `--capture-ui-test` | layout, dot and silence checks pass in every run; the two replay **peak** level checks fail intermittently: new build 12/12 in 3 of 6 runs, **0.5.5 control 12/12 in 2 of 4 runs** (same failure). Replay peaks −5.09 … −6.02 dBFS for a −6.02 input (0.5.5: −5.15 … −6.02, once −4.99 in a manual recording); near full scale once +0.13 dBFS (new) / −0.25 (0.5.5); RMS and LUFS identical to the reference in every run. Pre-existing intermittent export peak overshoot, not a regression (Backend, CaptureCoordinator, MediaExport, AudioPipe are byte-identical to 0.5.5) |
| Library previews | `--library-test` | 12/12 |
| Quality change | `--quality-change-test` | 20/20 |
| Close | `close-test.ps1` | 7/7 |
| Capture | `--capture-test` | 11/11 |
| Media | `--media-test` | 36/36 |
| Diagnostics | `scripts\validate-diagnostics.ps1` | crash, harness 14/14, clean exit, FFmpeg missing, unreadable settings: all pass |
| Audio | `--audio-test` | 16/16 |
| Hotkeys (real key presses, user's go-ahead) | `--open-hotkey-test`, two runs (default mode Cinematic D) | 14/14, 15/15 |
| Real user data | `%LOCALAPPDATA%\FRAME` (900 files), `Videos` (21 files): changed since the task started (01:42) | 0; HKCU Run "FRAME" unchanged; no test process left |

The real-capture suites ran on the integrated source before the version number changed to 0.6.0 (no code change afterwards); UI, hotkey, mode-switch and resource runs used the 0.6.0 build.

Screen saver finding: real-capture runs at night failed (manual recording "too short", no frames after ~1 s) while the user's DigitalRainSaver (secure desktop "Screen-saver") was active; 0.5.5 failed the UI suite the same way. Windows Graphics Capture receives no new frames while another desktop has the input. The 0.5.5 quality-change test still passes then (it does not check that frames flow). All results above are from runs with the user's desktop active.

## Resource use

`work\r2-int\measure-all.ps1` → `measure-resources-mode.ps1` per mode (`--resource-test --mode <key>`, 15 s phases; FRAME on the primary display behind the other windows, not activated; Shadow Recording of a small test window at 1080p60 20 Mb/s, NVENC; GPU = "GPU Engine" counters of the FRAME process; i7-12700 (20 logical CPUs), RTX 4090, primary 2560×1440). CPU = FRAME process in % of one core (includes the harness's own 30 Hz test window); updates = decorative clock steps per second.

| Mode | Visible, Shadow off | Visible, Shadow on | Tray (minimized), Shadow on | Visible again |
|---|---|---|---|---|
| Cinematic D | 17.7 % · GPU 3.15 % · 31/s | 19.7 % · 3.31 % · 30/s | 4.1 % · 0.05 % · **0** | 32.9 %* · 3.04 % · 30/s |
| Original GUI | 21.2 % · 2.75 % · 31/s | 23.1 % · 2.89 % · 31/s | 3.9 % · 0.04 % · **0** | 20.3 % · 2.65 % · 31/s |
| Dark / No Bullshit | 2.0 % · 0.04 % · **0** | 3.6 % · 0.05 % · **0** | 4.4 % · 0.05 % · **0** | 2.1 % · 0.04 % · **0** |
| Light | 1.6 % · 0.04 % · **0** | 3.3 % · 0.05 % · **0** | 2.1 % · 0.05 % · **0** | 1.6 % · 0.04 % · **0** |
| Pink (Sakura garden + sakura, first-entry default) | 14.9 % · 1.81 % · 31/s | 16.4 % · 1.94 % · 31/s | 2.6 % · 0.05 % · **0** | 14.1 % · 1.90 % · 30/s |
| Pink, all 7 effects | 40.5 % · 4.75 % · 31/s | 37.9 % · 4.68 % · 31/s | 3.0 % · 0.04 % · **0** | 45.3 % · 4.92 % · 31/s |
| Pink, six effects without dogs | 20.7 % · 2.28 % | 20.1 % · 2.24 % | 3.1 % · 0.04 % | 23.1 % · 2.39 % |
| Pink, dogs only | 20.5 % · 2.34 % | 23.2 % · 2.54 % | 3.3 % · 0.05 % | 20.9 % · 2.34 % |
| Emo (Dead rose garden + petals) | 6.8 % · 0.86 % · 32/s | 8.1 % · 0.90 % · 31/s | 3.0 % · 0.04 % · **0** | 8.8 % · 0.82 % · 32/s |
| Emo, all 4 effects | 6.0 % · 1.08 % · 31/s | 10.2 % · 1.04 % · 31/s | 2.7 % · 0.05 % · **0** | 8.1 % · 1.05 % · 30/s |

\* includes stopping Shadow Recording within the phase (single sample; Original GUI 20.3 % in the same phase).
Tray: 0 decorative updates in every mode (clock unsubscribed). Dark / No Bullshit and Light: no decorative clock while visible either. Pink with every effect costs about twice Original GUI while visible (≈2 % of the whole 20-thread CPU): the dogs are advanced at full composition rate (~60/s) for smooth motion, so WPF composes the whole window twice as often as with the 30/s particles alone; six effects without dogs and dogs alone cost about 20 % each. Effects pause on Library/Settings, and everything stops in the tray, when FRAME hides itself for a game and behind a screen saver.

## Visual review

Screenshots and clips: `docs\media\0.6.0\` — `mode-*.png` (all six modes, 1280×800), `mode-pink-all-effects.png`, `cinematic-d-vs-reference.png` (reference / inactive / active), `cinematic-d-activation-sheet.png`, `cinematic-d-activate.mp4` (activation at 3 s), `pink-all.mp4` (all effects, 60 fps, seeded), `pink-dogs-sheet.png`, `pink-dog-sleep-wake-sheet.png`, `emo-all.mp4` (Rainy neon alley, all effects). More agent evidence (iterations, per-effect clips, states JSON): `work\r2-agentA\out`, `r2-agentB\out`, `r2-agentC\out`, `r2-agentD\out`.
Cinematic D vs reference: same composition, scale and hierarchy (stone left-centre and dominant, large title right, slider below, actions, hotkey rows, bottom panel); differences by design: real slider values instead of 5/15/30/60, text-only manual recording, real hotkey labels, the 0.5.5 desert artwork instead of the reference's sun/ground photo-like scene (a generated rocky ground plane is added under the stone), FRAME's title bar with mode buttons.

## Not verified / limitations

- Pink mode with all effects: ~38–45 % of one core while visible on an i7-12700 (≈2× Original GUI); not measured on weaker hardware or AMD/Intel GPUs. Possible follow-up: a lower full-rate step for the dogs when many effects run, or smaller particle budgets (`PinkScene.Rebudget`).
- Pre-existing, not a regression: intermittent replay/export audio peak overshoot (up to ~+1 dB over the input peak, once above 0 dBFS near full scale) with identical RMS/LUFS, in 0.5.5 and 0.6.0; audio/export code was not changed (spec). Needs its own investigation (likely the join between copied segments / AAC priming).
- Not verified by a person on screen: real mouse hover/press on the new controls, the Pink picker and effect icons, clicking the three secrets with the mouse (hit-testing verified by harness), the dogs' pointer curiosity with a real mouse, Emo's 10-minute interval in real time (tested with a 2-second diagnostic interval), Cinematic D live at the user's 175 % display (rendered at 100 % and 150 %), Narrator.
- The new screen-saver/secure-desktop detection (DecorativeVisibility) was checked only through the desktop name reported while the user's screen saver ran; the clock stopping and resuming on a real screen-saver start/stop was not exercised end to end.
- Physical display off/on, gameplay in each mode, weaker hardware (as after 0.5.5).
- Deviation by design: the "Original GUI" shortcut is hidden in Cinematic D because the cycle button there already reads "Original GUI".
- `pink_mode(1).png` resembles Jinx (Arcane / League of Legends, Riot Games): copyright/trademark risk before public distribution; used as supplied.
- Agents A–D were stopped twice by the account session limit and did not deliver final reports; the main agent reviewed their code and evidence and re-verified everything on the integrated build.
- There is no visual-mode selector in Settings (title bar only, as specified).
