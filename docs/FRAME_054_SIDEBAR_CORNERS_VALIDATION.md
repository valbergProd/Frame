# FRAME 0.5.4 · sidebar corner frame · validation (2026-10-15)

Build: `C:\dev\Frame\builds\FRAME-0.5.4-sidebar-corners-20261015\FRAME.exe`.

| Check | Result |
|---|---|
| `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors |
| Core tests | 119/119 |
| Native UI suite (`work/validate-display-settings.ps1`) | 125/125 + 4/4 dialogs |
| `--nav-test` dark, dark minimum, light, light minimum (final source) | 18/18 each (an earlier batch had one intermittent Tab-focus miss per batch; reruns passed) |
| `--nav-test` on the published exe | 18/18 |
| Frame sizes (DIP) | Capture 103x35, Library 95x35, Settings 112x35; padding 10 px left/right, 7 px top/bottom; same at minimum size |

Renders inspected (`work/navframe/renders`, 2x sidebar crops): each item selected (dark, light, minimum), hover preview, keyboard focus (dotted underline), mouse-like focus.

Not automated / manual: real mouse click leaves no focus cue (WPF treats window activation as keyboard input, so the test cannot reproduce a click; by design the template has no focus trigger and FocusVisualStyle is keyboard-only), real hover appearance (preview only), Narrator.
