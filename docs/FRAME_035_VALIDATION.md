# FRAME 0.3.5 — visible recording atmosphere

2026-10-12. Main-only visual refinement; capture engine and Advanced unchanged.

- Upper sand increased from 18 to 72 fine grains. Added a bottom 135px layer with three low-opacity flowing ribbons and 32 slipping grains, suggesting dune movement without distorting the background artwork or controls.
- Recording glow extends 32px outside each active button, with brighter golden-orange edges and a 3.6-second pulse spanning 30–100% intensity. Manual pause retains its previous inactive-glow semantics.
- User overrides previous capture/focus suspension: sand, dune flow and active pulse continue during recording and while visible without focus. Hidden/tray/minimized and Performance mode stop the shared 12Hz timer. Loading behavior is unchanged. No video, blur shader or continuous rendering callback.
- Performance has a dedicated text-only template with no border in normal, hover, pressed or keyboard-focus states. Hover/focus use text color; keyboard access remains.
- Tray icon now uses the primary wordmark symbol's #E5AC52 golden color and four-corner geometry on a transparent background.

Diagnostics use isolated settings/output and audio off. Active renders simulate presentation; they do not prove physical recording or game FPS. More visible animation can consume more CPU/GPU while the window is shown; no FPS measurement was performed.

Delivery: C:\dev\Frame\builds\FRAME-0.3.5\FRAME.exe with companion files. Quit the previous tray instance first. Existing processes, releases and user media/settings are preserved.

Verified: full solution build zero warnings/errors; 41 native WPF checks and four native hotkey-dialog checks passed. Checks compare actual intensity changes on both glow surfaces, retain sand/dunes with active recording presentation, continue without focus, and stop callbacks hidden/minimized/Performance. Native high/low glow, sand, light and minimum layouts inspected. Fixed ScrollViewer viewport clipping by placing the glow gutter inside the viewport. Seven delivered files hash-verified. Core/media tests were not rerun for these presentation-only changes; prior results retain their previous scope. Tray icon source matches main logo; actual Windows tray visual appearance is not physically verified.
