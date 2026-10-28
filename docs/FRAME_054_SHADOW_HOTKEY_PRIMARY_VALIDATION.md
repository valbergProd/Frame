# FRAME 0.5.4 · primary-display Open hotkey, Shadow on/off hotkey, Apply far right · validation (2026-10-15)

Build: `C:\dev\Frame\builds\FRAME-0.5.4-shadow-hotkey-primary-20261015\FRAME.exe`.

| Check | Result |
|---|---|
| Solution build | 0 warnings, 0 errors |
| Core tests | 122/122 (+3 new; roundtrip test updated to 6 hotkeys) |
| Native UI suite (work/hk3/ui3, final source) | 133/133 + 4/4 dialogs |
| `--nav-test` | 18/18 |
| `--open-hotkey-test` (real keys, 3 displays incl. 4K TV at 300 % above) | 15/15 runs 3, 4, 5 (5 = restart: Shadow hotkey restored) and published exe rerun; run 1 and first published run: one foreground-only miss each (shown at once, Windows kept another window in front); run 2: pre-scaling bug, fixed |

Primary display log (published build): window on the TV 3840x2400 at scale 3 → primary 1280x800 at (640, 296), scale 1 (centred, same DIP size).

Renders (work/hk3/renders): Capture shortcut rows (dark normal: Not set / Ctrl+Shift+S; light minimum: Ctrl+Alt+F9 / Alt+Z), Settings with Apply at the far right (dark scrolled, light minimum).

Not covered: the side display as source (test uses the first non-primary display, the TV), a real full-screen game holding the foreground, Narrator.
