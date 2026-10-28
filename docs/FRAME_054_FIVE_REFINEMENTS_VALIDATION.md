# FRAME 0.5.4 · five UI refinements · validation (2026-10-15)

Build: `C:\dev\Frame\builds\FRAME-0.5.4-five-refinements-20261015\FRAME.exe`. Implemented 19:56–20:02 while the user gamed (no build/test/launch), tested 20:14–20:28 after permission.

| Check | Result |
|---|---|
| `dotnet build Frame.sln -c Release` | 0 warnings, 0 errors |
| Core tests | 119/119 |
| Native UI suite (`work/validate-display-settings.ps1`, work/five/ui3) | 129/129 + 4/4 dialogs (first run stopped at the failed-Apply check: Apply bug, fixed) |
| `--nav-test` dark / dark minimum / light / light minimum / published exe | 18/18 each (published exe: one intermittent Tab-focus harness miss, rerun 18/18) |
| Corner centring (ink padding, DIP) | Capture L 11.1 R 10.8 T 9.9 B 10.1; Library L 11.3 R 10.3 T 9.4 B 10.0; Settings L 11.5 R 11.7 T 8.2 B 9.0; shift Capture/Library (0, +3), Settings (+1, +2) |

Renders inspected (work/five/renders, work/five/ui3): sidebar each item, Settings top dark normal and light minimum, Recording expanded, Crop for social (TikTok) with FRAME title bar.

Bug found and fixed during testing: an inline comment had commented out Validate/Save in Apply settings (Apply did not save). Line-ending damage in MainWindow.xaml.cs (doubled line breaks) repaired before building.

Not automated: real hover over a collapsed expander (template sets the chevron only from IsChecked), real drag/resize/maximize of the Crop for social window, Narrator.
