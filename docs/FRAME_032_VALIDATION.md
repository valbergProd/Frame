# FRAME 0.3.2 — atmosphere and startup feedback

2026-10-12. Main app only; Advanced stays at 0.2.0. Existing capture behavior and user settings/media are preserved.

## Implemented

- Active Shadow and manual buttons have a wider, 20px outward warm glow drawn with ten translucent native edges. Optional six-second pulse; paused manual recording has no active glow.
- Optional 18-grain sand drift in a shallow 180px background band. One 12Hz background-priority timer drives sand and glow; no video, blur shader or full-background animation. Sand pauses during capture. All atmosphere callbacks stop when hidden, minimized, unfocused or motion-disabled. Static active glow remains.
- Static default is preserved. In dark theme, uncheck **Reduce visual motion** in Settings and apply to enable sand/pulse. Advanced retains its existing atmosphere implementation.
- Shadow displays **Loading** immediately during capability probing, then reveals ** capture engine…** at 40ms per character. The finite reveal takes approximately 0.64 seconds, then its timer stops. Readiness hides the label immediately; failure shows **Capture engine unavailable · check Settings**. Hidden windows stop the reveal timer. It never artificially delays engine readiness.

## Verification

Full isolated solution build: zero warnings/errors. 100 Core checks passed. 36 native WPF checks passed, including immediate/progressive/completed startup text, ready/failure states, active glow without sand, atmosphere suspension, and existing home/settings/history checks. Four native hotkey-dialog checks passed. Evidence: `validation-0.3.2/`.

Native loading, minimum layout, light active-state and dark active-state renders reviewed. Active-state images are appearance fixtures, not real recordings. Diagnostics used isolated data/capture paths and audio off. No user app was stopped, no live capture/audio or game FPS benchmark was run. The bounded animation implementation is not evidence of zero FPS impact; real CPU/GPU cost depends on hardware and window visibility. Previous idle measurements are not measurements of these effects.

Fresh runnable build: `C:\dev\Frame\builds\FRAME-0.3.2\FRAME.exe`, with adjacent files. Quit the previous instance through the tray before launching. Existing releases and locked running binaries are preserved.
