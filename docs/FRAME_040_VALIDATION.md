# FRAME 0.4.0 validation (Phase 2/3 Smart Sharing & Export), 2026-10-12

Files: `src/Frame.Core/ShareExport.cs` (new: presets, planner, destinations), `src/Frame.Core/Models.cs` (share settings + validation), `src/Frame.App/ShareExportJob.cs` (new: probe, preview still, export lifecycle), `src/Frame.App/ShareWindow.cs` (new: modal UI), `src/Frame.App/LibraryView.cs` (Share button, `OpenShare`, `ShareEncoder`), `src/Frame.App/MainWindow.xaml.cs` (Share tooltip), `src/Frame.App/MediaValidation.cs` + `UiValidation.cs` (tests), `tests/Frame.Tests/Program.cs` (7 tests), `Frame.App.csproj` (0.4.0).

Results (2026-10-12):
- `dotnet build Frame.sln -c Release`: 0 warnings, 0 errors.
- `dotnet run --project tests/Frame.Tests -c Release`: 107 passed.
- `work/validate-040.ps1` (isolated data, audio off, no capture): 48/48 native UI checks, four hotkey-dialog checks, renders incl. `work/validation-040/ui-checks.share-{shorts,tiktok,youtube}.png`.
- `FRAME.exe --media-test work/validation-040-media --ffmpeg <ffmpeg 8.1>`: all checks pass (see media-validation.json): copy presets, Shorts blur / TikTok crop 1080×1920 via h264_nvenc, Custom 1:1 720 via libx264, 2.5 s range export, A/V drift ≤ 0.016 s, full decode, source hash unchanged, cancel cleanup, missing/unwritable rejection, no overwrite.

Not verified: real browser upload pages, Explorer selection, Discord app launch and clipboard paste by hand; QSV/AMF exports; long recordings; actual low disk; live Shadow/Advanced capture (code untouched).
