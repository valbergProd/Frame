# FRAME 0.3.1 refinements — 2026-10-12

User-requested changes to FRAME.exe only. Advanced remains 0.2.0 with its own screenshot and monitoring workflows. The shared screenshot source remains necessary for Advanced and FRAME's video-region selector; FRAME exposes no screenshot capture command, tray action, hotkey or annotation action. Existing PNG library items remain accessible through the default Windows viewer; the filter is named Images.

## Implemented

- Small Choose capture folder button immediately below the Shadow action. Native folder dialog; a temporary delete-on-close write probe validates the folder. Atomic settings save precedes the runtime switch; cancel/error leaves the previous destination. Tooltip shows the current path. Changes are disabled during capture; no mid-session rerouting. Removed the capture-folder editor from Settings.
- Centered Shadow text in Segoe UI Light, matching the FRAME wordmark's font/weight. Removed the large button dot and manual red dot. Manual labels are Start manual recording / Stop manual recording.
- Shared active treatment for both recording actions: warmer fill, stronger ember edge and two static translucent outer edges. No blur shader, animation or extra timer. Shadow indicates actual engine.Replay; manual glows only while Recording, not Paused. Stop manual recording remains available when paused.
- Save now says Press <configured hotkey> to save last <requested duration>, with correct minute/minutes/seconds labels. Existing mouse/tray/hotkey Save remains functional and actual coverage still controls availability. Separate Change hotkey opens Settings, scrolls to Hotkeys and focuses the Replay binding; usable even when Save is disabled.
- Window renamed Choose window / game with an explanatory tooltip. It selects an HWND as the capture source, rather than an entire display. Region selects a fixed rectangle within one display; it does not follow a moving window. Both apply to shadow and manual video and remain pending the user's keep/remove decision. GPU/window capture compatibility is unchanged and needs hardware testing.
- Removed screenshot capture handlers, home/tray actions and hotkey dispatch from FRAME. Four default bindings remain; SettingsStore.Load drops the legacy Screenshot binding while preserving other user shortcuts and destination. No user settings were changed during verification.

## Verification and delivery

The standard Build.ps1 attempt encountered a DLL locked by the user's running FRAME. No process was stopped. Building the complete solution with a separate --artifacts-path succeeded with **0 warnings, 0 errors**; [100 Core checks passed](validation-0.3.1/tests-031.txt), including legacy shortcut migration. [Build log](validation-0.3.1/build-031.txt).

[29 native WPF checks passed](validation-0.3.1/ui-checks.json), including folder persistence/rollback, Settings navigation, removed settings controls, active/paused presentation and existing history/lifetime regressions. The deliberately locked settings-file test exposed UnauthorizedAccessException rather than IOException; the test now accepts either expected failure type. Product code retained the previous destination. [Four real WPF hotkey-dialog checks passed](validation-0.3.1/hotkey-dialog-tests.json), with programmatic input.

Visual review: [dark](validation-0.3.1/dark.png), [light](validation-0.3.1/light.png), [minimum](validation-0.3.1/minimum.png), [Hotkeys](validation-0.3.1/hotkeys.png), [active dark](validation-0.3.1/ui-checks.active.png), [active light](validation-0.3.1/ui-checks.active-light.png). Active images explicitly preview presentation only; no recording was started. Main diagnostics with explicit isolated --data and --render/--hotkey-ui-test use a separate data-scoped diagnostic mutex so they can coexist with the user's running app. Normal production single-instance behavior is unchanged. Settings and outputs stayed inside work/validation-031, with audio off.

Fresh runnable copy: `C:\dev\Frame\builds\FRAME-0.3.1\FRAME.exe` (keep adjacent files). Quit the old FRAME through its tray menu before opening this build. Existing release directories were not overwritten. The ordinary src/.../bin path may remain stale/partially copied after the locked build attempt; use the versioned copy.

No live capture, physical folder-dialog interaction, physical global shortcuts, mixed DPI, FPS or audio hardware acceptance was performed in this iteration. Prior nine synthetic media results belong to 0.3.0 and are not relabelled as a new run. CaptureCoordinator, Backend, MediaExport, audio code, Screenshot shared source, and Advanced UI/detection were not edited by this refinement.
