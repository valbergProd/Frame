# FRAME 0.5.4 · UI refinements (readable display cards, smart Apply, sidebar dot, compact recording controls) · validation (2026-10-15)

Build: `C:\dev\Frame\builds\FRAME-0.5.4-ui-refinements-20261015\FRAME.exe` (self-contained single file).
Status: implemented and compiled while the user played Fortnite (no tests then); **the deferred tests were run afterwards (18:20-18:36), results below.**
Final verified build: `C:\dev\Frame\builds\FRAME-0.5.4-ui-refinements-verified-20261015\FRAME.exe` (adds the Smart Apply UI checks; app code identical otherwise).
Not touched: recording/export/audio/buffer logic, hotkeys, obsidian stone, minute slider, desert animation, Advanced Capture.

## Implemented
1. **Readable display cards.** Core `DisplayLayout.ArrangeReadable(monitors, width)` (constants `CardWidth 76`, `CardHeight 44`,
   `NarrowCardWidth 46`, `NarrowCardHeight 58`, DIP before the 2 DIP inset): compact arrangement first (canvas ≤ 132 DIP); if any
   card would be below the readable size, a taller canvas (≤ 176 DIP); if displays still do not fit side by side, the narrow card
   (resolution split over two lines). Uses the same monotone per-axis mapping as `Arrange` (Windows arrangement kept, e.g. a 4K TV
   above the other displays; non-overlapping displays never overlap). `BuildDisplayCards` uses it; `CardLabel` is unchanged (it
   picks number + full resolution, adds "Primary" when it fits; the tooltip always has everything). Selection, orange ring and
   persistence unchanged. Expected: at the minimum window (~278 DIP map width) up to 3 displays side by side show number + full
   resolution on one line each; 4–5 side by side use the two-line resolution.
2. **Smart Apply** (`SettingsView.cs`): every Apply-only control is tracked (`Track`/`TrackToggle`/`TrackText`/`TrackIndex`) with
   its value as built from the saved settings: frame rate, output resolution, desktop/microphone device (by id), bitrate, replay
   duration, disk budget, cursor, combine, light appearance, reduce motion, compact controls, cues, start with Windows, start
   shadow on launch, save notifications, notification volume, FFmpeg path (trimmed), encoder (by id). Apply is enabled with an
   accent border + 22 % accent face only while a value differs; reverting disables it (grey: default face dimmed by the disabled
   template; tooltip "No changes to apply"). Unapplied values are kept in `settingsDraft` and restored when the page is rebuilt
   (navigation, probe completion, minute slider, hotkey and folder changes), so navigation and expanding/collapsing never change
   the dirty state; collapsed sections are tracked because their controls exist. Successful Apply clears the draft and rebuilds
   the page (disabled). Failure (validation/parse/busy) keeps the draft and the enabled button. Hotkeys, capture folder and
   "Minimize FRAME when a game starts" still save immediately and are not tracked. First run (not onboarded): Apply stays enabled
   because it also completes onboarding.
3. **Sidebar design C** (`MainWindow.xaml` `NavigationButton`): own template, no selected background/edge; active item = 5 px
   amber dot (Accent, .9) in the left padding (text/icon positions unchanged) + warmer text via new theme brush `NavActive`
   (dark `#FFE3C4`, light `#6B2E12`); inactive items use `Subtle`; hover = `Text` + 4 % wash; keyboard focus = 1 px Muted outline.
   Settings stays docked at the bottom.
4. **Compact recording controls:** "Start manual recording" 66 → 42 px high, 16 → 14 px text, sized to its text (MinWidth 168,
   no longer 1/3 of the row); save hint sized to its text (MaxWidth 340, Padding 14,6, MinHeight 42, wraps only when narrow) with
   "Change hotkey" beside it in a WrapPanel (moves down instead of clipping). States, texts, glow and click handlers unchanged.

## Verification done
- Self-contained Release publish (BelowNormal priority, one MSBuild node, no shared compiler): succeeded, 0 warnings/0 errors (warnings are errors), FRAME.exe 168,392,999 bytes, ProductVersion 0.5.4; Frame.Tests project compiled (0/0) into work/ui-refinements-testbuild but NOT run.
- Code review of the diff by the author. Nothing else.

## Deferred tests (run after gaming; none of these were executed)
1. `dotnet run --project tests/Frame.Tests -c Release` — expect 119 (118 + new "Display map (readable)" test).
2. `dotnet build Frame.sln -c Release` (also compiles Frame.Tests and UiValidation).
3. Native UI suite: `powershell -ExecutionPolicy Bypass -File work/validate-display-settings.ps1 -exe <FRAME.exe> -validation <new folder under work>` (120 + 4 last time).
   UiValidation "Apply reads values from collapsed sections" must still pass with the Apply button now disabled until a change.
4. `--display-ui-test` with `--fake-displays "-1920,180,1920,1080;0,0,2560,1440,P;2560,-200,3840,2160" --fake-selected 1` (12 checks).
5. Renders (`--render <png> --render-source --fake-displays …`, normal and `--minimum`, dark and light): user's setup with the 4K TV
   above (e.g. `0,-2160,3840,2160;-1920,0,1920,1080;0,0,1920,1080,P;1920,0,2560,1440`), 3 side by side, ultrawide + portrait,
   5 side by side at minimum. Check: number + full resolution on every card, TV above, no overlap/clipping.
6. Renders of the Capture page (recording controls at normal/minimum size, "Stop manual recording" active state) and the sidebar
   (Capture / Library / Settings selected, hover, keyboard focus, light theme).
7. Manual Smart Apply check: change a value in a collapsed section → orange; revert → grey; navigate away and back → still
   orange with the value kept; Apply → grey; invalid bitrate (e.g. 1) → error, stays orange; Apply while recording → refused,
   stays orange; hotkey/folder changes save immediately and do not enable Apply.
8. Real capture regression `work/validate-capture.ps1 -Exe <FRAME.exe> -Data <new folder>` (11 checks) — engine unchanged, but the build is new.

## Deferred test results (2026-10-15, 18:20-18:36)
| # | Test | Result |
|---|---|---|
| 1 | Core tests | 119/119 |
| 2 | `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors |
| 3 | Native UI suite (dark/light/minimum/hotkeys + dialogs) | 125/125 + 4/4 (120 before + 5 new Smart Apply checks) |
| 4 | `--display-ui-test` | 12/12 |
| 5 | Display-card renders (`work/uiref/renders`) | TV above three displays at normal, minimum and light; 3 side by side; ultrawide + portrait; 5 side by side at minimum (two-line resolution); single display: number + full resolution on every card, arrangement kept, no overlap or clipping. "Primary" fits only on large cards (tooltip/automation name always have it) |
| 6 | Capture page / sidebar renders | recording row compact (manual 42 px, hint sized to text, Change hotkey beside it), active Shadow state, Capture/Library/Settings each show the amber dot only when active, light theme readable |
| 7 | Smart Apply (automated in UiValidation instead of by hand) | clean = disabled; change in collapsed Recording = enabled with accent border; revert = disabled; Settings rebuild via navigation + expand/collapse keep value and dirty state; failed Apply (bitrate 1) keeps value, button enabled, file unchanged; revert after failure = disabled; successful Apply = disabled |
| 8 | `--capture-test` (real gfxcapture) | 11/11 |

Not covered: real mouse hover/click and keyboard feel, Apply refused while recording (engine.Busy) and hotkey/folder changes not enabling Apply (by code: untracked), Narrator, real display hot-plug, mixed DPI.

## Follow-up: "Share" renamed to "Crop for social" (2026-10-15, 18:50-18:58)
Text-only change (Library button, trim editor button and post-save action, window title/eyebrow, tooltips, automation names, missing-engine message); internal names and settings keys unchanged. Solution build 0/0; Core 119/119; native UI suite 125/125 + dialogs 4/4; renders of the Library row, the Crop for social window and the Trim editor inspected. Build: `C:\dev\Frameuilds\FRAME-0.5.4-crop-for-social-20261015\FRAME.exe`.
