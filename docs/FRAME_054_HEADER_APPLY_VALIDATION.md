# FRAME 0.5.4 · Apply in the Settings heading + Start-up merge · validation (2026-10-15)

Build: `C:\dev\Frame\builds\FRAME-0.5.4-header-apply-20261015\FRAME.exe`. Implemented while the user gamed (no build/test), tested after permission.

| Check | Result |
|---|---|
| `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors (first build: test-code name clash, fixed) |
| Core tests | 119/119 |
| Native UI suite (work/applyhdr/ui2, final source) | 131/131 + 4/4 dialogs |
| `--nav-test` | 18/18 |

New UI checks (all pass): Start-up after Hotkeys with three toggles in order and no Games section; Apply hidden when clean, shown with accent on a change in a collapsed section, hidden on revert; fixed while scrolling (outside the ScrollViewer, same position, on the heading line); hidden on other pages and back on Settings; navigation/expand keep pending state; failed Apply keeps value and button; successful Apply hides it; game minimizing last in Start-up.

Renders (work/applyhdr/renders): clean, pending, pending scrolled, scrolled to the end, light minimum pending/scrolled, Library light minimum, published exe. Fixed from renders: scrolled content ran under the heading and the status line on Settings/Library (PageScroll -32 margin); now clipped at the heading row and above the status row, content start unchanged.

Not automated: real mouse clicks on the header button, Narrator.
