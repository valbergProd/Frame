# FRAME 0.3.6 validation (Phase 1/3 GUI refinement), 2026-10-12

## Changes
- `RecordingGlow`: edge-free halo of 32 abutting 1px rounded rings (frozen pens, quadratic fade inside the 32px spread) + ember undertone + four wandering interior pools; all geometry stays inside the surface (old radial pools were clipped into a rectangle). Noise-driven breathing/lean, irregular pulse swell, asymmetric rise/fade.
- `Drift` helper: three incommensurate slow sines (no RNG, no allocation) for non-repeating motion; `Gust` shared wind factor.
- `SandDriftLayer`: gusting speed, noisy lift, three grain alphas. `DuneFlowLayer`: ridges re-shape each tick (3 small frozen StreamGeometries), softer haze, gusting grains.
- Tooltips: 140ms ease-out fade; Region caption "Record part of a screen", Region tooltip "Drag to choose an area to record", window tooltip "Record only one window or game".
- Lifecycle: unchanged shared 12Hz timer, event-driven stop when hidden/minimized/tray/cloaked/Performance; new UI checks prove it.

## Results
- `dotnet build Frame.sln -c Release`: 0 warnings, 0 errors.
- `dotnet run --project tests/Frame.Tests -c Release`: 100 passed.
- `work/validate-036.ps1` (isolated --data, audio off, no capture): dark/light/minimum/hotkeys renders, 43/43 native UI checks, four hotkey-dialog checks. New checks: no updates while hidden 600ms; after repeated hide/show one loop (≤14 ticks/s); minimized stopped with no ticks.
- Visual: glow-high render and 3x corner crop (`work/validation-036/glow-corner-zoom.png`) show no clip edge or ring striation; sand/dune renders reviewed.

## Not verified
Live motion feel (still frames only), physical tray, live capture, CPU/GPU and game FPS while visible or hidden (hidden = timer stopped by code and tests, not profiled).
