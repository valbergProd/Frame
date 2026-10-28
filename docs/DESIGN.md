# FRAME Desert Drift visual system

Implemented 2026-10-12 in FRAME 0.3.5 and FRAME Advanced 0.2.0. The sole approved main-app reference is [FRAME_DESERT_DRIFT_REFERENCE.png](FRAME_DESERT_DRIFT_REFERENCE.png). Earlier cyan/violet boards and screenshots are historical, not current design guidance.

Current 0.3.5: 72 upper grains plus subtle bottom dune flow; stronger 32px/3.6s recording glow; effects continue during capture and loss of focus, stopping hidden/minimized or in Performance mode. Performance is text-only with no border; tray uses the primary gold logo. See [current behavior](FRAME_035_VALIDATION.md).

Previous 0.3.4 override: main atmosphere defaults on in both themes; the subtle upper-right Performance button pauses motion and persists the choice. Loading uses Preparing capture… at 55ms/character with a short full-text hold. See [current behavior](FRAME_034_VALIDATION.md). Earlier descriptions below retain historical scope.

Previous main atmosphere: see [0.3.2 behavior and verification](FRAME_032_VALIDATION.md). Wider active glow and optional pulse/sand supersede older static-glow/main drift descriptions below. Startup loading reveals the entire label from empty at 40ms per character, looping until ready. Readiness/failure stops it immediately; hiding suspends it.

Both apps remain native .NET 8 WPF executables. `assets/desert-drift.png` is an original generated **background-only** abstract dune image, embedded in both apps through a frozen bitmap/brush. All text, controls, navigation, status and input are real WPF elements. No reference screenshot is used as a UI background; no browser, video wallpaper, shader is involved. FRAME optionally draws 18 sparse sand grains in a shallow native layer.

## Palette and geometry

`DesertAppearance.cs` owns the shared frozen brushes. Eight-digit values are ARGB.

| Token | Dark | Light |
|---|---|---|
| Background | #171310 | #F1E9DE |
| Surface | #B0211C18 | #F8F3EB |
| Raised | #A0302721 | #E8DFD2 |
| Boundary | #504238 | #B6A18C |
| Text | #F5EEE6 | #29211C |
| Secondary text | #C7B8AA | #655448 |
| Accent | #F27C39 | #AC431A |
| Shadow face | #70492218 | #DADBC1AA |

Segoe UI, light large action text, Consolas recording clock, tiny one-pixel button corners, restrained boundaries. The Shadow face is translucent with a thin ember outline; Shadow/manual text is centered in Segoe UI Light with no action dots. Active recording adds warm fill and two static translucent outer edges; paused manual recording removes that glow. There is no animation timer for this state; there is no expensive blur/glow effect. Hover, pressed, disabled and keyboard-focus states remain distinct. Dark is the visual target; light reduces background opacity and uses dark text, including on the primary action. Icon assets predate this work; the running shell and tray use warm accents.

## FRAME composition

Default 1280 x 800, minimum 930 x 690, 190-pixel left navigation. FRAME wordmark above Capture / Library / Settings; main content carries SHADOW RECORDING, a dominant 148-pixel-high rectangular Start/Stop action and a compact history dial to its right. A subtle Choose capture folder button sits directly under Shadow. The subordinate row contains manual recording and replay Save with a separate Change hotkey link. FRAME has no screenshot action. Accurate history status precedes a thin divider and compact source/quality/audio controls. Source details expand to existing display/window/region controls. Pause and the recording clock appear when appropriate. No slogan, Advanced navigation or persistent toast card.

Below 740 pixels tall, the home top margin contracts so minimum-size controls remain usable. Native WindowChrome keeps its 36-pixel caption, six-pixel resize border and SystemCommands. Caption buttons have accessible names and focus states. FRAME X/minimize hides to tray without the old reminder; Quit FRAME retains finalization. Advanced X still quits.

## History input and truthful Save state

`HistoryDialControl` draws a dark ring, short ember arc, one bright circular knob and two faint curved arrows outside its right side. Values are sorted 1/2/3/5/10/15/30/60-minute presets plus the valid persisted custom value (5–3600 seconds). New users start at five minutes. Settings remains available to choose another custom value. Subminute values show seconds; fractional minutes remain accurate.

The dial begins at twelve o'clock and advances clockwise through 300 degrees. Preset index `i` maps to `300 * (i / (count - 1))^2`, giving longer durations more arc and keeping five minutes visually compact. The final 60 degrees is an end stop, snapped to its nearer endpoint on initial pickup; grabbing the minimum knob just left of twelve o'clock stays at minimum. Dragging unwraps the zero crossing, captures the pointer and previews immediately; release commits atomically. Escape/lost capture cancels. Arrow keys, +/- and focused wheel snap by one step; Home/End select endpoints. UI Automation exposes a named range value in seconds and read-only state.

Changes are disabled during capture or pending operations: the tooltip instructs users to save clips, stop Shadow **and** manual capture, then adjust history; stopping clears unsaved history. There is no hidden restart. Failed settings writes restore dial and Save label. Save uses actual continuous closed-segment coverage, up to the requested duration. Warm-up, shortened history from the storage budget, and gaps are reported honestly; a gap disables Save until continuous coverage is available. Hotkey text comes from settings, not the reference's Alt+Z.

## Atmosphere lifetime

`ReduceMotion=true` is the default in each app. Optional motion is only a three-pixel background translation updated at 1 Hz. `AtmosphereMotion` stops its DispatcherTimer at the source on hide, minimize, deactivation, reduced motion or shutdown. Main also disables it while the capture engine is busy; Advanced disables it while monitoring is armed. Restoring a window reuses one subscription set. Unchanged history values do not invalidate the dial. Capture maintenance, hotkeys and Advanced's intentional 5 Hz detection/Preview remain separate. Motion stays disabled in light appearance.

## FRAME Advanced

Default 780 x 900, minimum 680 x 640, scrolling content. The primary 88-pixel Activate/Deactivate control is disabled without a selected window. Status, window picker/Refresh/Preview, motion settings, stills, destination Change/Open, screenshot actions and FFmpeg Browse/Check retain their own workflow. Motion fields share a compact row so normal-size actions fit. Preview uses warm change/image/video indicators; sampling logic is unchanged. Advanced has no replay dial, game routing UI or tray.

## Actual visual verification

Native WPF output was compared with the reference and corrected for transparent root rendering in light mode, inherited primary-button font size and minimum-size spacing. Hierarchy, warm dune atmosphere, translucent rectangular action and compact dial follow the reference; disabled Save, configured hotkey and audio-off state reflect real runtime values.

See [current refinement validation and images](FRAME_031_VALIDATION.md) and [earlier five-phase verification](DESERT_DRIFT_VALIDATION.md). Native renders cover both themes/apps, minimum sizes and 150% rasterization of FRAME. Physical mixed-monitor DPI, Snap, pointer feel and gameplay FPS still need interactive hardware acceptance.
