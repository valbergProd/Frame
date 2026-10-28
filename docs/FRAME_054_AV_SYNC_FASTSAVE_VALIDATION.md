# FRAME 0.5.4: A/V sync fix and fast replay saving (validation)

- **Date:** 2026-10-14.
- **Build:** `C:\dev\Frame\builds\FRAME-0.5.4-av-sync-fastsave-20261014\FRAME.exe`.
- **Machine:** i7-12700, RTX 4090, FFmpeg 8.1.
- **Isolation:** all tests used isolated `--data` folders under `work/`, with the capture folder inside the test folder.

## 1. Audio delay: root cause

The audio delay was a recording-time timestamp bug, not an export problem.

Audio and video started their timelines at different moments:
- **Audio:** time 0 was the moment FFmpeg opened the audio pipe. The raw `f32le` input is timed by sample count from its first byte, and `AudioPipe` began its wall-clock-paced timeline at the pipe connection.
- **Video:** time 0 was the first captured frame. `setpts=PTS-STARTPTS` sets it to 0, and gfxcapture's own timestamps already start at 0 on its first frame (verified with `showinfo`).

Windows Graphics Capture delivers the first frame clearly later than FFmpeg opens the pipe. Measured with the new `av-start` diagnostic:
- pipe opened +0.52 to +1.09 s after FFmpeg start;
- first frame +0.80 to +1.77 s.

Every sound was therefore stamped 0.28–0.57 s late (the difference between those two start delays) for the whole session. The offset is constant, not drift. It affected desktop audio and microphone equally (one mixed stream) and every segment and export path. The old build measured +612 / +622 ms on the reference flash+beep test (`--audio-test --sync-only`).

A smaller contributor: audio that waited in a WASAPI source's ring when it went live stayed as permanent latency. The same ring could also grow towards 1 s over hours if the device clock runs slightly faster than the system clock.

## 2. Fix (recording)

**Shared origin.** `CapturePipeline.TimelineOrigin` records one instant (QPC plus Unix µs) just before FFmpeg starts.
- **Audio:** `AudioPipe` runs its 20 ms timeline from that origin. Time before the pipe connection becomes timed silence.
- **Video:** `setpts='PTS-STARTPTS+if(eq(N,0),st(0,max(0,(RTCTIME-origin)/1e6/TB)),ld(0))'`. FFmpeg's wall clock is read once at the first frame and kept in register 0. gfxcapture's precise frame spacing is unchanged.
- `-fps_mode cfr` (or `fps=...:start_time=0` for combined layouts) holds the first frame from 0.

Both start times are measured, so nothing is a fixed offset. A session starts with about 0.3–0.8 s of held first frame (unchanged visually from before, apart from the held frame) and silence.

**Bounded source latency.** `IAudioSource.Limit`:
- a source that (re)starts keeps only what is due now;
- a running source keeps at most 50 ms beyond what is due.

**Diagnostics.** Each Shadow start logs `av-start` with the pipe-open and first-frame times.

Unchanged: the stall-recovery spool (30 s), still-screen handling (`-max_interleave_delta 0`), device recovery, and the encoder and FFmpeg timing settings.

## 3. Fast replay saving

`MediaExport.ExportReplay` replaces the full re-encode in `CaptureCoordinator.SaveReplay`. The full re-encode remains the fallback.

**Body.** Closed 1 s segments are stream-copied (`-c copy`), so H.264 and AAC stay bit-identical. The concat list carries:
- `duration` = end − start from `segments.csv`;
- for a session's first segment, `inpoint`/`outpoint` at its real first timestamp. The AAC priming packet (−21 ms) made Matroska shift that segment by +21 ms.

**Head.** When the window starts inside a segment, only that segment's part from the requested start (≤ 1 s) is encoded, with the recording's own encoder arguments (`Backend.VideoArgs`).

**Tail.** For a still screen, the open segment plus the freshly captured final image are encoded with the same arguments, holding the last frame up to the request.

**Joining encoded pieces.** Head and tail are joined by copy only if their codec parameters (codec, profile, size, pixel format, SHA256 of the SPS/PPS extradata) equal the recording's. Their AAC priming is skipped with `inpoint`. If the head cannot be joined, the copy starts at the keyframe; if the tail cannot, the full re-encode is used.

**Checks before the file gets its name.** Using the index only, with no decoding:
- video duration as planned (±0.25 s);
- audio length equal to video length (±0.15 s);
- frame count consistent with the CFR rate (no gaps or stretch).

Then a first-frame decode. Any failure falls back to the full re-encode. "Clip saved" is still shown only after the final file exists.

**Process priority.** All export ffmpeg processes run at BelowNormal priority.

**Fallback (full re-encode) also fixed.**
- Exact segment lengths.
- The segments are first joined by stream copy into one Matroska file, and only that file is decoded. Previously the AAC decoder lost its first frame at every concat file switch, leaving a 21 ms hole per second (measured: 49 holes in 50 s).
- The first-segment shift is applied.

This also applies to manual recordings (`Export`).

## 4. Measurements

**Test setup**
- **Synthetic audio, real capture:** `--audio-test --sync-test`. Real Windows Graphics Capture of a test window through FRAME's own Shadow pipeline. `SyntheticAudioSource` beeps, sample-exact, alternately on the desktop and microphone source, with a white flash at the same moment every 5 s. Offset = beep onset − flash onset, so it includes about one captured frame of display/capture latency. The test runs at 30 fps (33 ms per frame).
- **Real devices:** `--real-audio` used real WASAPI devices, opening only and playing nothing. It cannot measure sync without a real sound event; see the manual procedure in section 6.

**A/V offset (beep − flash)**

| Build / path | Offsets | Drift (trend) |
|---|---|---|
| Old build (aligned-20261014), `--sync-only` | **+612, +622 ms** | — |
| New, `--sync-only` (published exe) | +6, −17 ms | — |
| New, 60 s, stream copy | 11 events, −15 to +19 ms, mean +4 | +8 ms |
| New, 60 s, full re-encode fallback | 9 events, −10 to +18 ms | −1 ms |
| New, 5 min, stream copy | 59 events, −28 to +26 ms, mean +4 | −6 ms |
| New, 5 min, full re-encode fallback | 56 events, −28 to +26 ms | −5 ms |
| New, 5 min, still-screen tail save | all events in the clip, −28 to +26 ms | — |

- Beginning, middle and end are all within ±30 ms.
- Desktop and microphone events behave identically.
- An earlier 5-minute run had one event at +51 ms. It was identical in both export paths, so it came from frame quantisation of the flash, not from an export.

**Continuity and duration**

| Clip | Video | Audio | Gaps |
|---|---|---|---|
| 60 s copy | 60.000 s | 60.010 s | 0 video steps > 1.5 frames; audio packets contiguous (max step 21.3 ms) |
| 300 s copy | 300.000 s | 300.010 s | 0 |
| 300 s still-screen | 300.004 s | 300.032 s | 0 video gaps; join may split one audio packet, no hole |

Before the fix:
- the old export path produced a 61–67 ms frame gap every second and about 3 % timeline stretch (synthetic: 123.62 s for 120 s);
- the first version of the copy path had one 54 ms gap at the session's first boundary (fixed with `inpoint`).

**Export speed: FRAME's own code, 120 s at 2560x1440, 60 fps, NVENC 20 Mbps (`--export-perf`, synthetic content)**

| Path | Wall | ffmpeg CPU | GPU encoder | Result |
|---|---|---|---|---|
| Stream copy | **8.5 s** | **6.0 s** | none | 119.999 s, 7200 frames, 0 gaps |
| Stream copy, start mid-segment (encoded head) | 10.7 s | 7.1 s | ≤ 1 s | 109.520 s requested → 109.520 s |
| Full re-encode (previous method) | 34.4 s | 57.7 s | 120 s of NVENC | 120.000 s |

- Small test window, real capture: a 300 s replay takes 2.3 s wall and 0.9 CPU-s.
- A still-screen save of 300 s takes 7.1 s, including the final image capture.
- Copy cost at 1440p is dominated by opening and probing each 1 s segment, about 60 ms each. The faster per-file probe options are not used: they produced non-monotonic DTS warnings and are not yet validated.

**Regression tests**
- Audio reliability suite (`--audio-test`): **16/16**. A run during heavy load (a game in the foreground) gave 14/16: B1 did not reach the eviction cap, and B2's Δ offset was exactly at the 60 ms limit. Unloaded, B1 evicted 204 chunks and B2's offset was −2 / −2 ms.
- Real devices (`--real-audio`): 3/3. Desktop loopback and microphone live, 20.00 / 20.01 s, no ring drops.
- Capture reliability (`--capture-test`): **11/11**. Still save 8 s ending on the final image; motion save; "Saving replay…" then "Clip saved"; long still; killed-engine recovery; source loss.
- Media suite (`--media-test`): **36/36**. Includes Export and ExportLive.
- Sync and fast save (`--sync-test`): 6/6 at 60 s and 6/6 at 300 s. Covers:
  - stream copy;
  - fallback;
  - still-screen tail;
  - three saves in a row while recording.

## 5. Limitations and risks

- **Real sound sources not measured.** Real-device sync was not measured with an actual sound event, because no audio was played on the user's PC. Real WASAPI loopback adds the device period (about 10 ms) and is bounded by the 50 ms cap. Manual check: section 6.
- **Combined multi-display layouts** (`fps=...:start_time=0` path) were not tested; only one display or a window was available for the test.
- **Session start:** the first ~0.3–0.8 s of each Shadow session is the held first frame with silence.
- **Encoded head/tail joins** depend on the encoder producing identical SPS/PPS. This was verified for NVENC (window capture and synthetic 1440p). QSV, AMF and libx264 were not tested. A mismatch falls back safely: keyframe start for the head, full re-encode for the tail.
- **Copy cost scales with resolution** because each segment is probed. A faster probing variant needs separate validation.
- **Combined path not run:** the full Core and UI suites (UiValidation etc.) were not run.
- **Test clip in the user's folder:** the first baseline run saved one test clip (104 KB) to `C:\Users\tvalb\Videos\FRAME\FRAME 2026-10-14 19-11-01 Replay Capture-1.mp4`, because an empty `--data` folder uses the default capture folder. Deleting it was not permitted to the agent; the user can delete it. The audio test now redirects any non-isolated capture folder into the test folder.

## 6. Manual real-world check

1. Start the build and enable Shadow Recording with desktop audio.
2. Play an "audio video sync test" video (white flash plus beep) full screen for at least 1 minute.
3. Save a replay.
4. Measure the clip:
   ```
   powershell -ExecutionPolicy Bypass -File C:\dev\Frame\work\measure-av-sync.ps1 -Clip "<clip.mp4>"
   ```
   It reports every flash/beep pair, the offset in ms (positive = audio late), min/max and drift. The test video's own sync and the player's output latency are part of the result. Compare against a clip made by an older build (expected about +300 to +600 ms there).
