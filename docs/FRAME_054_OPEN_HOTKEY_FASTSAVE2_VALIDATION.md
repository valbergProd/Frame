# FRAME 0.5.4: Open hotkey and faster replay saving (validation)

- **Date:** 2026-10-14.
- **Build:** `C:\dev\Frame\builds\FRAME-0.5.4-open-hotkey-fastsave2-20261014\FRAME.exe`.
- **Machine:** i7-12700 (20 threads), RTX 4090, FFmpeg 8.1.
- **Load:** most measurements ran while the user was actively playing Fortnite (1440p, about 100–250 FPS). They are real-world measurements under gaming load, not idle benchmarks.
- **Isolation:** all FRAME test instances used isolated `--data` folders under `work/`.

## 1. Open FRAME hotkey (Ctrl+Alt+F)

**Implementation**
- New hotkey action `Open`, default Ctrl+Alt+F.
- Shown in Settings → Hotkeys as "Open FRAME". It can be changed with the existing inline editor and is persisted.
- Older settings files gain Ctrl+Alt+F automatically. If another action already uses Ctrl+Alt+F, Open is added as "Not set" (`Hotkey("Open", 0, 0)`; unset keys are never registered and never count as duplicates). Alt+Z and all other bindings are unchanged.
- `WM_HOTKEY` for Open is handled directly, not queued behind `Act()`, so it works during a save, a start or a stop.
- `BringToFront()`:
  - shows the window from the tray or from minimized, back to its previous state (a maximized window stays maximized);
  - an open window is only brought to the front;
  - it never hides, minimizes or starts a second instance.
- `Native.ForceForeground`: if Windows refuses `SetForegroundWindow` while a full-screen game holds the foreground, FRAME briefly joins that thread's input state (`AttachThreadInput` + `BringWindowToTop`). No injection.

**Verified**
- Core tests (106/106) cover the migration: default, keeps Alt+Z, conflict → not set, unset keys never duplicates, Ctrl+Alt+F accepted.
- Settings render shows the row (`work/render-open/hotkeys.png`).
- Live `--open-hotkey-test` (real `SendInput` → `RegisterHotKey`) during active gameplay, three runs:
  - **window shown:** tray, minimized→restored, maximized stays maximized and behind another window each showed the window in at least two of three runs; during an ongoing save, FRAME was visible after 27 ms while the save continued and completed;
  - **foreground:** 9 of 15 foreground checks passed; FRAME was frequently pulled back behind the game within 1.5 s.

  The user's simultaneous game input (clicks take the foreground back; held keys change the combination) makes this automatic check unreliable while playing.

**Needs a manual check:** press Ctrl+Alt+F in Fortnite with FRAME in the tray.

## 2. Faster replay saving: profile

Stage log per save (`replay-wait`, `replay-save … prepare/copy/check`). In-game run with the new build, 1440p60, 20 Mbps NVENC, real game audio:

| Save | Request → "Clip saved" | Segment-close wait | prepare | copy | check |
|---|---|---|---|---|---|
| A, 137 s | 15.2 s | 0.63 s | 4.79 s | 7.68 s | 1.03 s |
| B1/B2/B3 (202–234 s, back to back) | 16.2 / 15.7 / 16.2 s | 0.62–0.93 s | 1.0–1.5 s | 11.3–12.1 s | 1.0–1.2 s |
| C, 300 s | 28.2 s | 0.91 s | 3.75 s | 20.4 s | 1.43 s |

Remaining time is dominated by the concat copy. Opening and probing about 300 one-second segments is about 70 ms per segment under game load. Next comes the exact-start encode in "prepare" (decode plus NVENC session start, 1–4 s under load). It starts at the request and overlaps the segment-close wait.

## 3. Optimizations implemented

1. **Fast concat list.** Every entry gets `option analyzeduration 0`, `option probesize 65536` and explicit `inpoint`/`outpoint`, so there is no analysis decode per segment. The previously rejected fast probing placed some segments 37 ms late and caused non-monotonic DTS. With explicit in/out points, a 12-segment check showed 0 warnings, DTS = PTS and 0 gaps.
   - **Runtime guard:** any FFmpeg warning, or a duration off by more than 1.5 frames, rejects the fast form. The validated previous copy form then runs; if that also fails, the full re-encode runs.
2. **`-moov_size` instead of `+faststart`.** The index is written into reserved space at the start, so there is no second pass over the file. The previous form keeps `+faststart` as the fallback.
3. **Written in the capture folder.** The output is written as `<name>.partial.mp4` and renamed in place, never copied from the Recovery folder (which can be on another drive).
4. **Fewer FFmpeg/ffprobe launches.** Each launch costs hundreds of ms on a loaded PC.
   - One ffprobe for the final checks (duration, A/V length, frame count, and decoding the first 0.2 s).
   - One ffprobe per encoded piece (parameter hash, start, size).
   - Reference parameters are probed once per recording session.
   - The start-time probe runs only for a session's first segment.
5. **Exact-start piece prepared at the request** (`MediaExport.PrepareHead`), during the segment-close wait.
6. **Still-screen tail:** the fresh final image is captured while the open segment is copied.
7. **The window ends at the request.** It no longer ends at the close of the following segment, up to 1 s later. The last segment is cut there with `outpoint`. Core test added.
8. **Exact-start piece is seamless.**
   - Video: the cut is rounded to the segment's frame grid, including a session-first segment's +21 ms shift, and trimmed with `trim`; `-t` lost the last frame.
   - Audio: the AAC frames are laid out so the piece ends exactly at the next segment's first audio packet.
   - Result: 0 gaps; at most one sub-frame overlap at the start join (seen once: 4 ms step 0.11 s into a 60 s clip).
9. **Tail audio keeps its real start time.** It continues the last copied packet.

There is no extra GPU encoding (NVENC stayed at recording level during saves) and no new monitoring inside FRAME.

## 4. Before/after on identical material

Real gameplay: 302 one-second segments recorded during the in-game test, 742 MB, 1440p60 20 Mbps. Exported with `--export-perf` by the previous build and the new build, sequentially, while the game was running.

| Export | Previous build (av-sync-fastsave) | New build | Change |
|---|---|---|---|
| Whole window, 302 s | 48.4 s wall, 32.5 CPU-s | **13.1 s, 10.1 CPU-s** | **−73 % time, −69 % CPU** |
| Exact start, ~291–297 s | 50.9 s, 34.5 CPU-s | **16.0 s, 12.1 CPU-s** | **−69 % time** |
| Full re-encode (pre-copy method) | 125.4 s, 297 CPU-s + NVENC | — | — |

All outputs had 18,120 frames (or matched their duration × 60), 0 video gaps, longest video step 17 ms and longest audio step 21.3–22 ms.

These are wall-clock times of the export function only. In the app the user path adds the segment-close wait (about 0.6–0.9 s) and the toasts.

Synthetic, small window, under game load:
- 60 s copy: 2.1–3.7 s;
- 300 s copy: 4.1–6.3 s.

## 5. Real-world gaming stress test (`--game-test`, `work/game-test1`)

- **Setup:** primary display captured (2560x1440 @ 60, 20 Mbps NVENC), real desktop (game) audio plus a synthetic tone in the microphone slot (the real microphone is never opened). Saves through `RequestSaveReplay` (the Alt+Z path, with toasts).
- **Performance log:** Windows counters every ~1.4 s (`work/perf-monitor.ps1`).

Phase averages (% of the whole machine):

| Phase | CPU total | FRAME + ffmpeg CPU | NVENC | GPU 3D (game) | Disk |
|---|---|---|---|---|---|
| FRAME idle, Shadow off | 63.7 | 0.1 | 0 | 52.0 | 9.6 MB/s |
| Shadow on, steady | 61–66 | 0.8–0.9 | 19.3–19.5 | 47–54 | 19 MB/s |
| Save, 137 s | 68.2 | 3.2 | 19.1 (no extra) | 42.4 | 56 MB/s |
| 3 saves in a row | 68.9 | 4.0 | 19.4 | 48.2 | 80 MB/s |
| Save, 300 s | 64.8 | 3.3 | 19.6 | 46.4 | 62 MB/s |
| Shadow off again | 62.9 | 0.1 | 0 | 54.3 | — |

**FPS** was read from the game's own FPS counter, visible in the recording every 2 s (no injection):
- steady gameplay 100–250 FPS, strongly scene-dependent;
- around save A: about 120–160 FPS, similar to the surrounding 120–150;
- during the three consecutive saves: mean about 153 FPS, against about 160 before and about 172 after.

No systematic FPS drop can be separated from scene variation; a drop of a few percent during back-to-back saves cannot be ruled out. FPS with Shadow off could not be read, because it is not recorded. Frametimes were not measurable: no admin ETW and no PresentMon.

**Stability:** no fault, no audio interruption, all 5 saves "Clip saved".

**Clip continuity:** 137.283 / 202.283 / 218.483 / 234.116 / 300.016 s, frames equal to duration × 60, 0 video gaps, A/V length within 3–27 ms.

**In-game A/V offset: not measurable in this run.** Fortnite (fullscreen) covered the reference flash window, so it is absent from the capture; the tone was recorded but there was no visual reference. Sync is covered by the controlled tests below.

## 6. Regression and sync tests (new build, under game load)

- **`--sync-test` 300 s:** 6/6.
  - 59 events, desktop and microphone, −25 to +30 ms, trend −10 ms.
  - 300.033 s, 0 video gaps, contiguous audio.
  - Still-screen tail 300.000 s.
  - Three saves while recording.
- **`--sync-test` 60 s:** 5/6 before the T2 rule change. The single deviation was a 4.3 ms audio step 0.11 s into the clip (encoded start overlapping the first copied packet, no hole). T2 now checks for holes (longest step ≤ 22 ms) and allows one join overlap. Sync −15 to +18 ms.
- **Audio reliability suite:** 16/16. Test A now saves 1 s earlier, because clips end at the request and the first flash otherwise falls just outside its 30 s window. A4 +11/+23 ms; B2 +29 → −14 ms across a 45 s still.
- **`--real-audio`:** 3/3.
- **`--capture-test`:** 11/11.
- **`--media-test`:** 36/36.
- **Core:** 106/106.
- **Published exe:** `--sync-only` ran three times: +42/−23, −1/+33, +15/−16 ms. The first run missed the test's relative 50 ms rule at 30 fps under game load; the two repeats passed.

## 7. Limitations

- **Copy cost per segment** (about 35–70 ms) remains the main save time at long windows. Fewer, longer segments would require changing the recording engine, which was not done.
- **Exact-start encode** costs 1–4 s under game load; it overlaps the segment-close wait only partly.
- **Torn open segment:** when the open segment has no readable header yet, the still tail cannot be encoded and the full re-encode fallback runs (seen once in the audio suite; the save succeeded).
- **Not measured:** FPS with Shadow off, frametimes, in-game A/V sync.
- **Open hotkey foreground** is only partly verified under active gameplay.
- **Not tested:** QSV/AMF/libx264 head and tail joins (they fall back safely), and multi-display layouts.

## 8. Follow-up: stricter clip rules, final build re-run (22:23–22:29)

- `Frame.Core.MediaContinuity` (Core tests, 5 new, 111/111): a clean ms-rounded AAC stream passes; the known start-join overlap passes; one missing AAC frame mid-clip or at the start, an overlap later in the clip, two overlaps, a duplicated packet are rejected; a linear 60 ms drift is detected, frame jitter is not.
- `--sync-test` uses these rules: T2/T4 allow at most one partial overlap within the first 1.05 s; T3 (full re-encode, no encoded start) allows none; T1 drift by trend (< 30 ms) and every offset within ±50 ms.
- Final build `--sync-test --sync-seconds 60`: **6/6**. Offsets −33…+1 ms (11 events, desktop + microphone), trend +4 ms; copy clip 60.000 s, 0 video gaps, 0 audio holes/overlaps/duplicates; fallback, still tail and 3 consecutive saves pass.
- Rules on real clips: earlier clip with the 4.33 ms step at 0.112 s → accepted; previous full export (49 × 43 ms holes) → rejected; final 60 s clip → accepted (no irregularity).
