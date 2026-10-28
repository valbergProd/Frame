# Project separation: FRAME and FRAME Advanced Capture, 2026-10-13

User request (autonomous, user away): make FRAME Advanced Capture a genuinely independent product with its own repository, solution, build, tests, docs and releases; remove it from FRAME only after the standalone app is verified.

## Safety
- Not a git repository, so there was no uncommitted-work state to preserve via git. Rollback point: `work/rollback/20261013-premigration-0.5.4/Frame-source-20261013-premigration-0.5.4.zip` (src, tests, assets, docs, Handover_files, root files, work/validate-*.ps1; 208 files; SHA-256 B70D83E2762C83008859FA7908C0523F32F2800332ACA856E7FE93DEF8EF8A95). Verified to contain all 14 files of `src/Frame.Advanced*` before deletion.
- Baseline before changes: `Frame.sln` Release build 0/0, Core 126/126.
- Order: new repository created and verified first; FRAME changed afterwards. No user data, settings, recordings or `builds/` packages touched (user `%LOCALAPPDATA%\FRAME Advanced\settings.json` still dated 2026-10-11).

## Dependency inventory (before)
Advanced = `src/Frame.Advanced` (WPF app) + `src/Frame.Advanced.Core`; ProjectReference to `Frame.Core`; linked engine source from `src/Frame.App` (Backend, CaptureCoordinator, AudioPipe, AudioDevices, MediaExport, Native, Screenshot) and appearance source (DesertAppearance, AtmosphereMotion); shared assets (Frame.ico, desert-drift.png); shared `Frame.sln`, `Directory.Build.props`, `Frame.Tests`, `Build.ps1`, `Publish.ps1`, `Run-Advanced.ps1`. Runtime: already separate data folder, mutex and capture folder; first-start read-only import of FRAME's settings file. Full table: `C:\dev\AdvancedCapture\docs\VALIDATION_0.2.1.md`.

## New repository `C:\dev\AdvancedCapture` (FRAME Advanced Capture 0.2.1)
Own solution and projects (`AdvancedCapture.Core`, `AdvancedCapture.App`, `AdvancedCapture.Tests`), own copies of the engine/appearance/needed Core files and assets, `Product.cs` identity, `Build.ps1`, `Validate.ps1` (render + real recording), `Package.ps1`, `Run.ps1`, `Recover-Session.ps1`, `CLAUDE.md`, `AGENTS.md`, `ADVANCED_CONTEXT.md`, `Handover_files/HANDOVER_Advanced.md` (+ verbatim history copy), `docs/CHANGELOG.md`, `docs/VALIDATION_0.2.1.md`.
V: build 0/0; tests 111/111; render pixel-identical to the old bundled build; real recording through FFmpeg/NVENC; isolated-copy build and full validation; local package validated from another folder.

## FRAME after removal
- `Frame.sln`: Frame.Core, Frame.App, Frame.Tests. `src/Frame.Advanced*` deleted. `Frame.Tests`: Advanced.Core reference and 32 Advanced tests removed; FRAME halves of two mixed tests kept → 96 tests.
- Scripts: `Publish.ps1` FRAME only; `Run-Advanced.ps1` removed; `Install-Project.ps1` without it (and now copies CLAUDE.md).
- Code: one comment in CaptureCoordinator; `UiValidation` focus checks report in their name when Windows refused foreground (test harness only).
- Docs: CLAUDE.md, AGENTS.md, README.md (note), README_START_HERE.md (historical note), HANDOVER_Frame.md + FRAME_CONTEXT.md (START HERE + separation section), HANDOVER_Frame_Advanced.md (ARCHIVED header, content kept), CHANGELOG.

## Results (FRAME)
| Check | Result |
|---|---|
| Release build (FRAME only) | 0 warnings, 0 errors; output only FRAME.dll/Frame.Core.dll |
| Core tests | 96/96 |
| Native UI + hotkey dialogs | 115/115 + 4/4 (`work/validation-sep-ui3`); "Opt-in atmosphere runs while visible" passed with its focus part NOT verifiable: Windows refused foreground to test windows while the user was away. Control: the pre-separation build (rebuilt from the rollback zip) failed the original focus assertion identically at that time, so the cause is the desktop state, not the separation. |
| Media/trim/share | 36/36 |
| Background start | hidden, tray, ready, 4 hotkeys, Run entry untouched |
| Real capture | 11/11 while Advanced recorded at the same time (`work/validation-sep-frame-capture`); 11/11 again from an isolated copy after the removal (`work/validation-sep-frame-capture-isolated`) |
| Publish | `dotnet publish src/Frame.App` → only FRAME.exe |
| Independence | copy built elsewhere with `-v:diag`: 0 mentions of `AdvancedCapture`, `Frame.Advanced` or `C:\dev\Frame\`; 96/96 there; scripts/projects scan: only a message text in Publish.ps1 names the other repository |

Not done: building each project while the other directory is renamed away — the agent's safety filter refused renaming `C:\dev\Frame`. Equivalent evidence above (isolated copies + diagnostic logs + static scan). Manual repeat if wanted: rename one folder temporarily, run the other's build and validation, rename back.

## Finding (pre-existing)
Stopping a capture while the source is completely unchanged can fail after 12 s ("Capture did not finalize"; segments retained) because ffmpeg/gfxcapture only reads the quit command when a frame arrives. Reproduced in Advanced (identical engine code); probably affects FRAME's manual/Shadow stop on a still screen too (not reproduced here). Needs an engine fix in each repository with real-capture tests.

## Remaining manual checks
Start both apps by hand from their new locations; Advanced: pick a window, Activate, real motion/stills/screenshots, settings persisted in `%LOCALAPPDATA%\FRAME Advanced`; FRAME: normal use unchanged. Decide whether FRAME's next package README/BETA_TESTING should mention Advanced as a separate download.
