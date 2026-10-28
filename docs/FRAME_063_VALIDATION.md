# FRAME 0.6.3 — primary display default and persistence (2026-10-28)

## Problem and changes

The default primary choice was saved as a concrete Windows display ID. A later boot/primary change could leave that ID pointing to a secondary display. Automatic recovery also saved temporary fallback IDs over user preferences, so a restart lost the original choice.

- `Settings.FollowPrimaryDisplay`: nullable only for migration; true follows Windows' current primary, false uses deliberate `SelectedDisplays` preferences. New/empty preferences become primary when displays first become available. Selecting only primary saves true and an empty ID list.
- `DisplaySelection` resolves saved intent separately from the effective capture selection. `DisplayRecovery` never saves a temporary fallback over the preference and restores available preferences even across restarts. Capture/action locks and the fresh enumeration in `Targets()` are preserved.
- Legacy empty/current-primary selections migrate to the primary policy; legacy secondary/multi-display selections remain explicit. Old files do not record whether a secondary ID was deliberate or a previous automatic fallback: select only primary once to opt into following primary in that case.
- Other settings, recording pipeline, encoder settings, audio and timing are unchanged.
- The display diagnostic opens the active layout's display popup (the legacy expander is hidden since 0.6.1). The background diagnostic now reports selected IDs and Windows' primary ID.

## Validation

- Release solution build: 0 warnings, 0 errors (isolated `OutDir` under `work/primary-default-063`).
- Core tests: 141/141, including four new primary-policy/persistence/migration cases.
- Display UI and recovery diagnostic: 28/28 on the development build in Dark mode; other preferences preserved (120 FPS, 1440p, bitrate, replay duration, audio, mode, startup and notification choices).
- The first display run exposed an obsolete harness assumption (canvas in the legacy expander); fixed to open the actual display popup and rerun successfully. Failed evidence retained in `validation-dev`; passing evidence in `validation-dev2`.
- Published executable in Original GUI: 28/28 display UI/recovery checks; other preferences preserved (`validation-published2`).
- Real background auto-start on the published executable: 4/4 (`validation-autostart-native`): hidden tray launch, real Shadow capture active, only Windows' current primary selected, real HKCU startup registration unchanged. Audio off; isolated data only. Report: primary/selection `\\.\DISPLAY1`, `replay=true`, `trayState=Active`, one activation notice.
- Sandbox limitations were encountered and resolved for validation: the first single-file launch failed before FRAME started (0x8000809F, bundle extraction); a test-only `DOTNET_BUNDLE_EXTRACT_BASE_DIR` under `work` allowed it to run. Sandboxed Windows Graphics Capture returned access denied (0x80070005); the same isolated auto-start script passed outside the sandbox. Neither workaround changes the executable or user settings.

## Build

- Target: `C:\dev\Frame\builds\FRAME-0.6.3-beta-primary-display-20261028\FRAME.exe` (self-contained single file, win-x64, .NET 8.0.32; no installer).
- Published successfully; file/product version `0.6.3.0` / `0.6.3-beta`; 182,032,905 bytes; SHA-256 `158EC7AEF3573F0250DC535286A278C4C880FAC99ED234E7357BF2289DC6C75D`.
- Initial restore could not contact NuGet's vulnerability service (NU1900); publish succeeded using cached packages with `NuGetAudit=false` and `--ignore-failed-sources`. No dependency versions changed; online vulnerability auditing was unavailable.

## Limits

No physical Windows reboot or real display reconfiguration was performed. Primary changes, display loss/return, empty enumeration and reload were simulated through isolated diagnostic displays. A running capture keeps its sources until idle; display changes never switch an active recording. Long media/audio suites were not rerun for this selection-only change.

Evidence/scripts: `work/validate-primary-default-063.ps1`, `work/validate-primary-autostart-063.ps1`, `work/primary-default-063/` (all test data isolated; real user settings/recordings untouched).
