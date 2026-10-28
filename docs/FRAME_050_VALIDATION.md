# FRAME 0.5.0 validation (Phase 3/3 Advanced Library & Clip Editor), 2026-10-12

## Implemented
- Library (`LibraryView.cs`, `MainWindow.xaml`): larger thumbnail cards with hover surface, name, resolution · duration · size · date (cached ffprobe), game-folder tag for replays routed by `ReplayFolders`; video actions Trim / Share / ••• (Play, Show in folder, Copy file path, Rename, Copy file, Move to Recycle Bin), images keep Open / •••; double-click plays; right-click opens the same menu; sort Newest/Oldest/Name/Longest/Largest (Longest uses cached probe results only, unknown last); clearer empty states; missing files give a message and refresh. Paging stays 50 rows per page with the existing 2-wide thumbnail/metadata gates.
- Clip editor (`TrimWindow.cs`, `TrimTimeline.cs`, Core `TrimSelection.cs`): MediaElement preview, Play/Pause, ⇤ Start, Play selection, Loop, position/duration, 14-still timeline with dimmed outside, accent selection, draggable handles, playhead, click/drag scrubbing (seeks throttled to 60 ms), exact Start/End fields (`TimeText`), Set start · I / Set end · O, ←/→ one frame, Shift ±1 s, Home/End, Space, Esc. Save clip (beside the recording; Library refreshes), Save to… (folder), Share (opens the Phase 2 Share window with the selected range). After saving: Open clip, Show in folder, Share clip.
- Trimming: one pipeline. `ExportRequest.Range` → `ExportPlanner` → `ShareExportJob.Run`. Trimmed ranges always re-encode with input seek `-ss` + `-t` (copy is kept for whole files). Measured reason: copied ranges snapped/overran by 1–2 frames and a keyframe-to-end copy drifted ~0.1 s A/V.
- Thumbnails: `ShareExportJob.Thumbnails` — keyframe-seek decode of 14 samples, batched 5 per ffmpeg process, BelowNormal, cancellable (process tree killed), cache keyed by path+size+mtime, newest 1200 kept when over 1500 stills. Not started while capture is active.
- Lifecycle: editor created only on Trim; hidden/minimized pauses playback, stops the clock and cancels thumbnails (resume from cache when shown); close stops/clears the player, timers, workers and handlers. Re-encoding is refused while recording (Phase 2 rule kept).
- Polish: themed global Slider style (thin track, accent fill, round knob).

## Results
- `dotnet build Frame.sln -c Release`: 0 warnings, 0 errors.
- Core `tests/Frame.Tests`: 113/113 (6 new: selection clamping, 5000 random handle moves, exact entry/very short/invalid, 4-hour range, time text, trimmed ranges always re-encode).
- `FRAME.exe --media-test work/validation-050-media`: 32/32 (Phase 2 checks plus 13 trim checks). Each trim compares decoded output frame count with the source's own frames in the interval, video length, A/V start/length, first-frame PSNR vs the source at the start (~55 dB) and 0.5 s later (~18–24 dB), full decode and source SHA-256:
  - 60 FPS (2 s GOP, B-frames): 0→1.5 (90/90), 10.37→end (97/97), 3.37→7.81 (266/266), 6 frames (6/6), 4→end (480/480)
  - 30 FPS 2.2→4.9 (81/81); 23.976 FPS 1.1→3.3 (52 of 53, within one frame; A/V 0.072 s); VFR 1→3 (72/72); 10-minute file 312.4→332.4 (600/600)
  - Shorts preset with range 1080×1920 (97/97); trim cancellation leaves no files; thumbnails cold 3.6 s / warm 0.005 s for 14 stills of a 10-minute file, cancellation leaves no workers, changed source gets new stills.
- `work/validate-050.ps1`: native UI 67/67 (19 new: video/image row actions, name sort, empty state, 400-file folder realizes 50 rows, editor opens and decodes, 14 thumbnails, I/O/frame keys, clamped rapid drags, Space play, hide pauses + stops clock, editor Share passes range, Save clip + Library refresh, close releases player/timers/workers, 4× open/close leaves no workers, recording-busy editor makes no thumbnails and refuses re-encode). Renders: `ui-checks.library.png`, `ui-checks.trim.png`, `ui-checks.trim-share.png`.
- Hotkey dialog suite (code unchanged): 4/4 in the final run and 5 standalone runs; it failed intermittently at "Alt+Z selection" in 2 of 6 sequential runs during this session. Cause not established (possibly global Alt+Z use by other software); not caused by Phase 3 files.

## Delivery
builds/FRAME-0.5.0/FRAME.exe (copy of the Release build, ProductVersion 0.5.0; offscreen render smoke test exit 0). Note: the 0.4.0 version bump had not been applied (failed command), so earlier 0.4.0-era binaries reported 0.3.6.

## Not verified
Mouse feel of dragging/scrubbing and playback smoothness by hand; MediaElement on HEVC/AV1 recordings (Windows codec dependent; the editor shows a preview-unavailable note and still trims); 100%/150% DPI editor layout by hand; real game recordings over 1 hour; live Shadow/Advanced capture while editing (capture code untouched); QSV/AMF exports.
