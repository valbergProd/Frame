# FRAME 0.3.3 — looping startup label

2026-10-12. Entire Loading capture engine… label types from empty, one character every 40ms, then starts over. It loops only during engine probing, stops immediately on readiness/failure, and suspends while hidden. No artificial readiness delay. Other 0.3.2 behavior and Advanced remain unchanged.

Verification: main application and Core dependency build succeeded with zero warnings/errors. All 36 native WPF checks passed, including full-label start, progressive typing, wraparound, ready/failure timer shutdown and existing UI regressions. Isolated settings and outputs; no live recording or physical audio. Evidence: validation-0.3.3/build-033.txt and ui-checks.json. Earlier Core/dialog counts belong to 0.3.2 and were not rerun for this UI-only adjustment.

Delivery: C:\dev\Frame\builds\FRAME-0.3.3\FRAME.exe; all seven companion files copied and hashes verified. Existing builds/processes/settings/media preserved. Quit the earlier tray instance before starting the new build.
