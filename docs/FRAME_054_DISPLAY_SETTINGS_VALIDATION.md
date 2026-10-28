# FRAME 0.5.4 · display selector, capture-mode buttons, Settings navigation and collapsible sections · validation (2026-10-15)

Build: `C:\dev\Frame\builds\FRAME-0.5.4-display-settings-20261015\FRAME.exe` (self-contained single file, ProductVersion 0.5.4).
Scope: Capture-page display selection, capture-mode buttons, "Switch to display capture", sidebar Settings position, Settings sections.
Not touched: recording/export engines, A/V sync, Shadow buffer, hotkeys, obsidian Start/Stop, minute slider, desert animation, Advanced Capture.

## Root cause of the overlapping display map
`DrawMonitors` drew each display as a Border on a fixed 88 px Canvas, scaled by `min((width-40)/union.Width, 64/union.Height)`.
With a 4K TV beside 1080p/1440p monitors the union is tall, so a 1080p box became ~30 px high, while its content stayed fixed
(15 px name + 8 px gap + 10 px resolution). The text was not clipped and spilled over neighbouring boxes; adjacent boxes also
shared edges exactly (no gap), so outlines merged. Nothing enforced a minimum size or adapted the labels.

## What changed (implemented)
- **Geometry (Core `DisplayLayout.Arrange`, pure, unit-tested):** one uniform scale that fits the width and a 132 DIP max height;
  canvas height follows the arrangement (88–132 DIP). Each axis is mapped monotonically edge by edge, so displays that do not
  overlap on the desktop cannot overlap on the canvas, also after a too-small display is stretched to the minimum card size
  (48 x 34 DIP + inset); every card is inset 2 DIP (4 DIP gap). Any origin, negative coordinates, any primary position.
  Bounds are physical desktop pixels, so mixed DPI keeps the real arrangement proportions.
- **Clickable cards (`DisplayMap.cs`):** the checkbox row is gone; each display is a `ToggleButton` card. Selected = 2 px Accent
  ring (separate layer, label never moves), unselected = 1 px Line border, hover = 5 % wash + Muted border (enabled only),
  disabled while capture is busy or an action is pending (opacity .45, tooltip "Stop capture before changing source.").
  Keyboard: Tab focus (dashed outline, keyboard only), Space/Enter toggle; focus stays on the rebuilt card. Automation name e.g.
  "Display 2, 3840 × 2160, primary, selected". Theme brushes are dynamic (light/dark). No timers, glow or animation.
- **Labels:** measured per card; first fitting form of: number + resolution + "Primary" → number + resolution → resolution on two
  lines (portrait) → one line → number only. Tooltip/automation name always carry the full information.
- **Selection rules:** persistence path unchanged (`selected` → `settings.SelectedDisplays` → `SaveSettings()`). Multiple
  displays allowed. The last selected display cannot be deselected (quiet no-op, explained in its tooltip) — previously an empty
  selection could be saved and Start failed with "Select at least one display or window". Clicking a card while a window/region is
  the source switches back to displays with that display only. Ids of disconnected displays are dropped on the next card click
  (they could not be deselected anywhere and made `Targets()` throw "A selected display is disconnected").
- **Updates:** cards rebuild on WM_DISPLAYCHANGE (`RefreshDisplays`), canvas width change, selection change, theme change and
  capture-lock change only.
- **Capture-mode buttons:** "Choose window / game" and "Region" in a two-column `UniformGrid` (identical width/height, padding
  10,9, default button style, wrapping centred text at small sizes). The lone "Record part of a screen" caption under Region moved
  into its tooltip, so both buttons align.
- **"Reset to displays" → "Switch to display capture":** inspected first — `ResetSourceClick` clears the window/region source
  (`custom = []`) and refreshes the display list; it never resets settings. Behaviour unchanged; renamed, placed directly under the
  capture-mode buttons, quiet style, visible only while a window or region is the source.
- **Sidebar:** the navigation column is a `DockPanel`; Capture and Library stay at the top, Settings is docked to the bottom
  (26 px from the bottom edge, same style, icon, hover and selected states, same column width).
- **Settings sections:** Hotkeys and Storage unchanged. Recording, General and Advanced settings are sibling `QuietExpander`
  sections (same chevron ring, typography and hairline separators as the former Advanced expander), collapsed by default; their
  descriptions became the expander captions ("Quality, history and sound", "Appearance, sounds and on-screen controls", "Capture
  engine, encoder and diagnostics"); Recording's "Apply below; capture must be stopped." is the first line inside it. Rows and
  order unchanged; controls are only hidden, so Apply reads collapsed sections. Open/closed state is remembered for the app
  session (`openSettingsSections`, not persisted). On the first-run onboarding page Recording starts open (its text says "Choose
  your capture preferences below"). Apply button and note stay below all sections.
- **Diagnostics:** `--fake-displays "x,y,w,h[,P];…"` + `--fake-selected "1,3"` (only with `--render`), `--render-source`,
  `--display-ui-test <json>`, `--render-settings`, `--render-settings-expanded`.
- **UiValidation maintenance:** stale checks fixed (Settings heading order; Shadow-button glow that no longer exists since the
  obsidian button — crashed the run; slider width threshold 150 → 100 for the 112 px slider). New checks: sections collapsible and
  collapsed, rows complete, state memory, Apply reads collapsed sections, Settings anchored at the bottom at minimum size,
  capture-mode buttons equal at minimum size.

## Verification (this build / integrated source)
| Check | Result |
|---|---|
| `dotnet build Frame.sln -c Release` (warnings = errors) | 0 warnings, 0 errors |
| Core tests (`Frame.Tests`) | 118/118 (111 + 7 display-map tests: 1/2/3/4 displays, 4K TV beside/above/below 1080p, ultrawide + portrait, negative coordinates, tiny beside 8K, random arrangements never overlap / stay inside / keep minimum size, labels and fake spec) |
| Native UI checks (`work/validate-display-settings.ps1`: dark with `--ui-test`, light, minimum, hotkeys, hotkey dialogs) | 120/120 + 4/4 dialogs, all cases PASS |
| `--display-ui-test` (3 fake displays, isolated data) | 12/12: toggle via UI Automation persists to settings.json, toggle off persists, last display cannot be removed, Enter/Space toggle, focus kept, region source → no card selected + switch button visible → card click returns to displays, cards disabled while an action is pending and enabled after |
| Renders inspected (work/integrated/renders, work/par-display/renders, work/par-settings/run-settings) | 1 display; 1440p + 1080p; 1080p (negative X) + 1440p primary + 4K TV, normal and minimum size; 4K above/below 1080p; ultrawide + portrait; four displays incl. 1280x1024 at minimum; light theme; multiple selected; Settings collapsed/expanded, minimum, light; sidebar at normal/minimum size. No overlapping cards or labels; orange outline only on selected cards; buttons equal |
| Published exe render (3 fake displays incl. 4K TV) | correct |
| Real capture regression `--capture-test` (`work/validate-capture.ps1`, integrated build) | 11/11 |

All runs used isolated `--data` folders under `work/`; no Windows display setting was changed.

## Capture regression
`work/validate-capture.ps1 -Exe work/integrated/bin/FRAME.exe -Data work/integrated/capture`: 11/11 (Shadow start on real gfxcapture, saves after a still and during motion, 8 s windows, stop). Engine code unchanged; this confirms the display selection still feeds `Targets()` correctly.

## Not verified (manual testing needed)
- Real mouse hover/click feel, tooltips appearing, Narrator wording (the Settings expander toggles are announced by their caption).
- Real WM_DISPLAYCHANGE (connect/disconnect/rearrange) and real mixed-DPI or 4K-TV hardware; only simulated arrangements were rendered.
- Card lock while `engine.Busy` during real capture (the action-gate lock was tested).
- Live window resizing by hand; the Expander fade (0.16 s) when a section opens.
- Pre-existing, unchanged: page content scrolls under the status line at the bottom (`PageScroll Margin=-32`).
