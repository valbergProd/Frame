# FRAME 0.5.5 · beta refinements, recording reliability, cinematic desert · validation (2026-10-17)

## Build

| Item | Value |
|---|---|
| Folder | `C:\dev\Frame\builds\FRAME-0.5.5-beta-refinements-20261017-005444\` |
| Executable | `FRAME.exe`, 168,525,321 bytes, SHA-256 `858F7BF3ACAEACFFF2666F22246D3BFBDD0AF9CF6AA932BD02D4674E26C91308` |
| Version | 0.5.5.0, product version 0.5.5-beta (csproj Version 0.5.5) |
| Runtime | self-contained single file, win-x64, bundled .NET 8.0.32 (startup line of the smoke run: `FRAME 0.5.5 beta · Windows 11 Pro 25H2 (build 26200.9711) · x64 · .NET 8.0.32`) |
| Build | `dotnet build Frame.sln -c Release` 0 warnings, 0 errors (warnings as errors) |
| Installer | none (user request: a new installer after practical testing). The 0.5.4 Beta installer `builds\FRAME-0.5.4-Beta-20261016-162409` is untouched. |

## How the work was done

User spec "Beta Refinements, Recording Reliability & Cinematic Desert Atmosphere" (23 items). Parallel work as the spec suggested: a base snapshot (`work\r1-base`), subagent A (recording & reliability) in `work\r1-agentA`, subagent B (visuals & animation) in `work\r1-agentB`, the main agent in the repository (Library, Settings, sounds, audio level, Trim, Recovery, Capture layout). Both subagents were stopped by an account session limit (00:00) after finishing nearly all of their work; the main agent reviewed their copies (A: build clean, its own tests passed; B: build clean, three review videos), finished B's remaining item (replay-length slider) and tuning, and merged the three copies with `git merge-file` (3-way per file against the base; 40 files, 4 hand-resolved conflicts). Pre-merge backup of the main tree: `work\r1-main-premerge`.

## Implemented

| # | Item | Implementation (main files) |
|---|---|---|
| 1 | Primary display selection and recovery | Core `DisplaySelection` (unit-tested rules: by device id, never index 0; Windows' primary = MONITORINFOF_PRIMARY / MonitorFromPoint, else the display at 0,0); `DisplayRecovery.cs`: keeps remaining valid displays, else the primary; persisted; one status-line note + log, hint under Capture source; never under a running capture (deferred until idle / next start); the original selection returns when its display reconnects in the same session; WM_DISPLAYCHANGE re-enumeration debounced 1 s; `Targets()` re-enumerates and recovers at every start (auto-start case). |
| 2 | Window/game and region removed | Buttons, handlers, region overlay (`Screenshot.Select`/`SelectionOverlay`), `Native.Windows`/`Candidates` removed; the in-memory `custom` source is now `testSource`, set only by diagnostics harnesses (Backend's window path kept for them). Nothing was persisted, so no settings migration was needed. |
| 3 | Quality/FPS while Shadow Recording runs | `ShadowRestart.cs` `RestartShadowWith(next, reason)`: stop (segments finalized) → save → start through the same coordinator/`Act` gate; replay length kept everywhere; honest status ("replay length stays 15 minutes, earlier footage was cleared"); failure → previous profile restored in settings/file/combo and restarted once when safe, otherwise off with a clear note; combo disabled with a reason during manual recording, a transition or another action. Settings › Recording › Apply uses the same path while Shadow Recording runs. |
| 4 | Close exits | `OnClosing` → `Quit()` always; Quit waits for a running action, finalizes a manual recording, stops Shadow, ends FFmpeg, unregisters hotkeys, removes the tray icon; Minimize still hides to the tray; tooltip "Close FRAME (stops recording and exits; Minimize keeps it in the tray)". |
| 5 | No FRAME sounds | `Cue` and settings `Sound`/`Volume` removed (rows "Cues", "Notification volume"); Windows message boxes (which play system sounds) replaced by `QuietDialog` (FRAME chrome, silent): delete confirmation, finalize failure, unhandled-action note, "already running". Captured audio untouched. |
| 6 | Audio level | Investigated and measured (below); no processing change. |
| 7 | Hotkey labels | `HotkeyNames.cs` + Core `KeyLabels`: layout character via `MapVirtualKeyEx(MAPVK_VK_TO_CHAR)` (no keyboard-state side effects), dead keys show their accent, fixed names for digits/letters/numpad/F-keys; `FormatHotkey` is the single entry point. Stored keys and registration unchanged. |
| 8 | "Show FRAME window" | Settings › Hotkeys row title and automation name. |
| 9 | Library thumbnails | On-demand preview queue (`LibraryView.cs`): only while Library is shown and FRAME visible; rows in view first; ≤ 2 FFmpeg/ffprobe at BelowNormal priority, also while recording (root cause of missing thumbnails: nothing was generated while `engine.Busy`); placeholders "Preparing preview…"/"No preview"; cache validated (empty/undecodable entries deleted and regenerated; decoding from memory so a broken file is not kept open), atomic temp-file writes; paused when hidden, cancelled when leaving Library; failures logged once per file. The never-shown Capture-page recent list no longer builds rows. |
| 10 | Trim chrome | `FrameChrome.Wrap(this, Build(), "Trim")`. |
| 11 | Recovery tooltip | Small "i" next to Recovery (`InfoIcon`), `RichToolTip` style, keyboard focus opens it, automation name "What is Recovery?". |
| 12 | Light appearance / navigation | Root causes: Apply was refused while capture ran (the theme change "did not stick"), Apply navigated to Capture (`ShowPage(HomePage)`), the Settings page was built with `FindResource` snapshots (mixed brushes after a theme change). Now: immediate preview both ways from the Smart Apply draft (`ShownLightTheme`), every caller of `ApplyTheme` uses it; Settings text uses resource references; Apply stays on Settings at the same scroll offset; non-capture settings apply while recording. |
| 13 | Reduce visual motion removed | Checkbox, `ReduceMotion`, `AtmosphereVersion` removed (old files load; keys dropped at the next save); the atmosphere follows visibility and Windows' "Show animations" (SPI_GETCLIENTAREAANIMATION, live). |
| 14 | Stable Capture layout | Live "available / length" line removed (available footage is in the Save button tooltip); `ReplayInfo` is a fixed two-line slot (Hidden, not Collapsed) for real notes; Pause + timer moved into the record row; the manual-recording button width fits its widest label. |
| 15 | Status dot | `UpdateShadowDot`: grey off/failed, amber ring while starting/stopping, amber while recording; source of truth `engine.Replay`/transition state. |
| 16 | Manual recording quieter | Graphite edge and faint face; hover/focus lift to Muted; warm edge + glow only while recording. |
| 17 | Replay-length slider | Higher-contrast track, larger handle with a gold rim, gold active range, value 15 px SemiBold, "replay length" caption under it; same behaviour, persistence, accessibility (name "Replay length"). |
| 18 | Living desert | `CaptureAtmosphere.cs` + `DesertTerrain.cs` (B): `DesertWind` (prevailing wind, wandering mean, travelling gusts, curl-noise turbulence, wind streams), ~910 grains in four kinds (far dust, midground, foreground, surface grains with creep, saltation hops and crest spindrift), wisps, two tiled dust sheets seen through a light mask, dune-crest light streaming downwind on two parallax layers, size-compensated alpha buckets, preallocated arrays. |
| 19, 20 | Stone rotation and pulse | `ObsidianStone.cs`: inner `StoneArt` rotates about the measured visual centre, 40 s per turn, constant; active: OFF→ON crossfade, generated "Ember" fissure light and the halo pulse (~2.6 s, asymmetric breath, ≤ 0.7 % swell); `Stone` layout box, hover/press scale and hit area unchanged; slider distance uses the swept radius. |
| 21 | Lifecycle | One clock (`CompositionTarget.Rendering`), about 30 updates/s, dt ≤ 0.1 s (no catch-up); unsubscribed when hidden, minimized, in the tray, cloaked, session locked or display off, or with Windows animations off. |

## Results

| Suite | Check | Result |
|---|---|---|
| Build | `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors |
| Core | `Frame.Tests` (incl. 6 display-selection, 2 key-label, 2 settings tests new) | 133/133 |
| UI | native UI suite (dark, light, minimum, hotkeys), incl. 7 new checks: theme preview both ways, rapid toggling without mixed palettes, preview survives navigation, Apply stays on Settings at the same scroll position, revert to the saved theme, Recovery info icon, Trim title bar | 141/141 in the last three runs + hotkey dialogs 4/4. Two earlier runs failed once each on timing-dependent checks: the loading-label loop (made robust: waits for the wrap instead of one sample after 900 ms) and the pre-existing game-minimize check (passed on every rerun; the user was active at the PC) |
| Navigation | `--nav-test` | 18/18 |
| Displays | `--display-ui-test` with 3 fake displays (primary listed second), incl. recovery: disconnected selection → primary (not the first listed), persisted, one note, no repeat; return restores the original choice; partial loss keeps the remaining display; deferred while an action runs (no "0 displays"); empty enumeration keeps the saved selection; auto-start case; order change keeps a deliberate secondary choice | 20/20 |
| Hotkey labels | `--hotkey-names-test` (installed layouts read-only; Norwegian layout present) | 8/8 · Norwegian VK_OEM_6/3/7 → Å/Ø/Æ, Alt+Å, Ctrl+Shift+Ø, dead key ¨, US layout ] ` ' |
| Capture layout, dot, level, silence | `--capture-ui-test` (real Shadow Recording of a test window, synthetic digital audio) | 12/12 · all control positions identical (0 px) in idle, starting, active, saving, 15 min length, manual recording, audio-interruption note, stopping, stopped and at minimum size; dot off/ring/amber/ring/off |
| Library previews | `--library-test` (30 clips + broken clip + empty and corrupt cache entries, Shadow Recording running all the time) | 12/12 · no work while Library closed (also after a refresh); max 2 concurrent; rows in view first after a scroll; broken cache entries replaced; broken clip "No preview"; leaving Library stops; hidden pauses and showing resumes; a clip saved while Library is open gets its preview |
| Quality change | `--quality-change-test` (A) | 20/20 · 720p30 → 1080p60 (restart 1.0 s) → native/60 (1.6 s); replay length 15 min kept in settings, file, slider, labels and the running buffer; one FFmpeg per pipeline; saves before/after with the new size/fps; rapid changes do not race; failing profile → previous restored and Shadow continues; forced start failure → not recording, previous profile, nothing left running |
| Close | `close-test.ps1` (A): Shadow + manual recording running, title-bar Close | 7/7 · process exits, no FFmpeg left, manual recording finalized and playable, Recovery clean, quit logged, no unexpected-exit marker |
| Capture | `--capture-test` | 11/11 |
| Media | `--media-test` (export, trim, Crop for social) | 36/36 |
| Diagnostics | `scripts\validate-diagnostics.ps1` | crash (Critical, crash.json, marker), harness 14/14, clean exit, FFmpeg missing, unreadable settings: all pass |
| Audio | `--audio-test` | 16/16 |
| Hotkeys (real key presses) | `--open-hotkey-test`, two runs, on the published FRAME.exe | 14/14, 15/15 |
| Real user data | `%LOCALAPPDATA%\FRAME`, `Videos\FRAME`: files changed since the task started (23:25) | 0; HKCU Run "FRAME" unchanged; no test process left |

Order of the last changes: the real-capture suites (capture-ui, library, quality, close, capture, media, diagnostics, audio) ran on the integration build before two final changes that touch only the decorative clock (update cap ~30/s) and the resource harness (window kept behind others, not activated); Core, the UI suite (×2, 141/141), the resource measurement and the published build's smoke run came after them.

Real key presses (`--open-hotkey-test`, two runs): first held back because the user was actively using the PC; run after the user approved it (2026-10-17, ~01:05) on the **published** `FRAME.exe` of this build: **14/14 and 15/15** (Ctrl+Alt+F brings FRAME to the primary display from the tray, minimized or another display; the Shadow on/off hotkey starts and stops Shadow Recording while FRAME is hidden; restore state).

## Audio level (item 6)

Pipeline: WASAPI loopback/microphone (`WasapiSource`, mix format float32 or PCM16 → float, mono duplicated to both channels) → each source mixed with a fixed gain of **0.8 (−1.94 dB)** in `WasapiSource.Mix` (headroom so desktop + microphone do not clip when summed; applied to each source even when only one is enabled) → 48 kHz stereo float pipe → FFmpeg `aresample=async=1:first_pts=0` (timing only) → AAC 192 kb/s. Exports: replays stream-copy audio; trims/manual recordings re-encode AAC 192k; no `volume`, `amix`, `loudnorm` or normalization anywhere. Rate conversion for non-48 kHz endpoints is nearest-sample (no gain change; possible aliasing above 24 kHz on 96/192 kHz endpoints).

Measurement (`--capture-ui-test`, synthetic 1 kHz tone through FRAME's own mixer/pipe/FFmpeg/AAC and export paths, 2.0–4.4 s of each file, compared with the same tone rendered by FFmpeg):

| File | Input peak | Peak | RMS | Integrated | AAC reference (peak / RMS / LUFS) |
|---|---|---|---|---|---|
| Replay (stream copy + exact head) | −6.02 dBFS | −6.015 | −9.031 | −6.0 LUFS | −6.014 / −9.034 / −6.0 |
| Manual recording (re-encoded export) | −6.02 dBFS | −6.015 | −9.031 | −6.0 LUFS | same |
| Replay, near full scale | −0.45 dBFS | −0.444 | −3.457 | −0.4 LUFS | −0.442 / −3.456 / −0.4; flat factor 0 (no clipping) |

Conclusion: after the source stage FRAME changes the level by less than 0.01 dB and does not clip. The only deliberate attenuation is the −1.94 dB mix headroom on live device sources (not exercised by the synthetic source; read from code). The user's "quieter than expected" impression fits the playback chain (Windows volume 20–30 %, reduced in-game volume, high-gain amplifier): in-game/application volume is in the captured signal; whether the Windows master volume affects loopback depends on the device/driver and was not measured (no real device was opened, changed or played to). Audio processing was left unchanged, as the spec asked. A first run reported the replay peak 0.3 dB high: a measurement artefact (input seek `-ss` starts AAC decoding without pre-roll); the harness now decodes from the start and trims with a filter.

FRAME makes no sound: after the whole `--capture-ui-test` run (start, save, length change, manual recording, audio fault, stop, restart) the default playback device had **0 audio sessions** of the FRAME process.

## Resource use (item 21)

`--resource-test` via `work\measure-resources.ps1` (FRAME on the primary display behind other windows, not activated; Shadow Recording of a small test window at 1080p60 20 Mb/s, NVENC; GPU from the "GPU Engine" counters of the FRAME process; i7-12700 / 20 logical CPUs, RTX 4090, 2560×1440 at 175 %):

| Phase | FRAME CPU (one core) | FRAME CPU (whole PC) | FFmpeg CPU (PC) | FRAME GPU | Decorative updates/s |
|---|---|---|---|---|---|
| Visible, Shadow off | 15.6 % | 0.78 % | 0 | 2.7 % | 30.9 (clock running) |
| Visible, Shadow on (pulse) | 16.4 % | 0.82 % | 0.14 % | 2.9 % | 31.0 |
| Tray (minimized/hidden), Shadow on | 2.6 %* | 0.13 % | 0.08 % | 0.05 % | **0** (clock stopped) |
| Visible again, Shadow off | 13.5 % | 0.68 % | 0 | 2.6 % | 30.4 |

\* includes the harness's own animated test window (30 Hz) in the same process; FRAME's decorative work is zero (0 updates, clock unsubscribed).

Tuning: B's clock updated on every rendered frame (52–55/s here): 31 % of a core and 4.4 % GPU while visible. Capped at about 30 updates/s (25 ms minimum step = every second frame), the rate the review clips were rendered at: about half the cost, no visible difference in the clips. Hide/show cycles keep one loop (UI check).

## Animation review (items 18–20)

FRAME's own rendering, stepped deterministically (`--render-animation`, `work\render-animation-clips.ps1`), 10 s at 30 fps, 960×600: `docs\media\0.5.5-animation\dark-inactive.mp4`, `dark-activate.mp4` (Shadow presentation becomes active at 3 s), `light-active.mp4`. Reviewed frame by frame and with crops: grains are fine irregular specks and short streaks moving together with the wind, denser in streams; heavier grains hop and creep along the lit dune edges; crest light flows downwind; no seams, pops or flashes seen. Rotation: exactly 0.3° per frame (9°/s, 40 s per turn), constant. Pulse: ember light 0.10 → 0.90 over each ~2.6 s breath after a 0.7 s crossfade; peak vs trough clearly visible (crops in `work\r1-int\anim1\stone_states.png`), no blinking. Light theme: umber specks at lower alpha, readable UI. Observation: the brightest sky shows faint horizontal dust streaks (wind-blown sand against the light); at 2× zoom they read as a regular texture; at normal size they are subtle. Limit: the clips are FRAME's own render, not a screen recording of the live window; the live window runs the same clock at ~30 updates/s.

## Not verified / limitations

- A real display switched off/on in the NVIDIA Control Panel or unplugged: simulated with fake displays (enumeration, recovery and deferral); the real WM_DISPLAYCHANGE path (debounced re-enumeration) was not exercised with a physical change. Displays changing during an active recording are handled by the existing fault path (not re-tested with a real display).
- Quality change during real gameplay and with several displays; Settings › Apply restart path tested through the shared restart code (quality test) but not separately with a real Apply click while recording.
- Animation cost on weaker GPUs/CPUs (measured only on i7-12700 + RTX 4090); WPF composes at ~52–55 Hz on this PC.
- Windows reduced-motion setting followed live (code) — not toggled in a test (it is a Windows setting).
- "Recording controls" (General): it is the compact floating toolbar for manual recording, not a sound setting, so it was kept and renamed "On-screen controls" (the request assumed it was a sound cue); the user can decide whether it should go.
- −1.94 dB mix headroom on live sources and nearest-sample rate conversion: by design, unchanged (documented above).
- The never-shown `RecentList` element is still in MainWindow.xaml (no rows are built).
