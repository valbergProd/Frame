# FRAME 0.6.5 — replay audio quality and trim timing (2026-10-28)

## Scope and evidence

The user moved to release-readiness point 3 (audio quality and A/V synchronization) and deferred point 2's Windows restart/lifecycle checks. No restart, device-setting changes, installer, visual-mode changes or framework upgrade was performed. All diagnostics use synthetic PCM and owned test windows, isolated `--data` and output beneath `work/audio-065`.

The previous intermittent replay peak overshoot was reproduced in 0.6.4. A continuous 1 kHz sine at amplitude 0.95 (about -0.45 dBFS) reached **+2.402 dBFS** at the replay's encoded-head/copied-body join. The raw PCM was clean at that time. Float32 decoding, an independent AAC encoding of the same PCM, and exact sample-count-driven tones distinguish the new join transient from ordinary source-onset/codec transients. This was a real waveform defect, not a general gain change: integrated loudness stayed unchanged.

AAC transforms overlap adjacent frames. Joining independently encoded head AAC to the existing AAC stream breaks that transform history even when packet timestamps are continuous. Replay export now joins the **original AAC packets**, includes available preceding-segment pre-roll, and muxes that continuous audio alongside the existing video pieces. Normal moving replay audio remains bit-identical; the video body remains copied and only the existing partial head/still tail is encoded. A still tail needs padding, so its complete audio is encoded once from a joined original audio stream, avoiding independent AAC joins and repeated segment priming.

Copied audio pre-roll may begin before time zero and is hidden by the MP4 edit list. The file verifier explicitly decodes the first video frame if its short packet probe encounters only pre-roll. Duration and frame-count checks remain enforced. Export exceptions still fall back to the existing validated full export.

Trim/share re-encoding and the legacy manual/full-fallback exporter now preserve the filtered video time base (`-fps_mode:v passthrough -enc_time_base:v filter`). The default encoder time base rounded an exact cut to another frame grid while audio retained the precise cut. On the same recorded input, a 0.73-second trim previously added -8.645 ms of A/V offset; the corrected path differs by about +0.34 ms. The still-screen manual exporter keeps its explicit CFR frame-rate handling.

No gain, normalization, limiter, capture encoder setting, WASAPI source handling, live capture timing or mixing change was introduced. Very near full-scale material can still have normal lossy-codec transients; this is not a promise of zero overshoot for arbitrary AAC input. The existing 30-second pending PCM limit during a complete video stall remains.

## Validation

- Release build and self-contained publish succeeded. Core regression: 141/141 checks.
- Final executable, deterministic replay exports: **12/12**. These re-export exactly the same preserved source segments and ranges that reproduced the defect, covering two amplitudes, paced and exact digital sources, session-start and fractional-start replays. Every export used the stream-copy path.
- Independent final-output audio proof: **12/12**. SHA-256 hashes show every output's AAC packets are a contiguous sequence from the original recording, without missing/duplicated packet timestamps. Integrated loudness is unchanged within 0.1 LU. All 12 outputs have zero float samples above full scale. The exact high-level fractional-start cases now peak at **-0.44154 dBFS** (true peak -0.4 dBFS), matching the independent AAC control; the previous files measured +2.4 dBFS true peak with the same -0.4 LUFS integrated loudness.
- Video body preservation: **12/12**. Compressed H.264 image slices after the first 1.1 seconds match original recorded slices, including keyframes. Raw packet hashes initially differed only at keyframes because the concat bitstream filter inserts SPS/PPS headers; the corrected measurement compares image slices, retaining the keyframe checks. `fixtures-final/video-proof.json` and `verify-video.py`.
- Final manual/pause/trim synchronization: **14/14**. The 14.54-second recording stopped after four seconds of a still screen; all four flash/beep pairs were retained. The 93.18-second paused recording retained all 18 pairs and excluded the deliberately injected paused flash/beep. Offsets were -34.4 to +1.9 ms (short) and -18.0 to +42.6 ms (long); no accumulating drift. Matching audio/video durations and full-file decode passed before/after trim. Trim changed the short clip's offsets by at most 0.339 ms and the long clip's by less than measurement precision. An independent count confirms no extra non-terminal tones from the pause.
- Final five-minute replay/fallback/still/repeated-save tests: **6/6**. Moving replay: 59 flash/beep events, offsets -29 to +46 ms, trend -14 ms over the clip, 300.000 s video / 300.011 s audio. Forced full fallback: 57 events, -30 to +46 ms, trend -15 ms, 290.000 s video / 290.021 s audio. Both have zero video gaps and zero audio holes, overlaps or duplicates. The 6.24-second still tail and three subsequent saves also passed without faults. `sync-final-300/result.json`.
- Final media/trim/share regression: **36/36**, including 60/30/23.976 FPS, variable-frame-rate and ten-minute source cases.

Baseline 0.6.4's 300-second synchronization run passed 5/6 checks: replay offsets were -29 to +44 ms; one full-fallback event measured +52 ms against a 50 ms limit. Neither path showed accumulating drift or packet gaps. This baseline failure is retained, not reclassified as a pass. Intermediate diagnostic failures (JSON lifetime on final report save, and a conflicting `-r`/passthrough option in the still manual path) were corrected before the final executable was published.

Total: **233/233** Core/native/media/independent packet checks passed. No C# changes followed the final publish. The final executable hash was rechecked after validation. These checks are not a substitute for the deferred lifecycle and hardware qualification.

Export-cost observation on the small test window: the first 300-second replay took 8.49 s including segment wait (2.11 s FFmpeg CPU), versus 5.28 s / 1.19 s CPU in the baseline. Later final-build stream-copy exports logged 2.38–3.52 s excluding wait and analysis; the still-tail export took 4.50 s. This is single-run evidence, not a general throughput benchmark. The new audio-only remux/probe adds work and storage during export; the unchanged video body was verified separately.

Evidence and reproducible runners: `work/audio-065/run.ps1`, `media.ps1`, `measure.py`, `verify-audio.py`; baseline `quality-baseline` and `sync-300`; final `fixtures-final/result.json`, `levels.json`, `audio-proof.json`, `manual-sync-final/result.json`, and media/long-run output folders. `--audio-quality-test` requires isolated output; optional `--quality-fixtures`, `--quality-peaks-only`, and `--quality-sync-only` select its cases.

## Build identity and limits

`C:\dev\Frame\builds\FRAME-0.6.5-beta-audio-quality-20261028\FRAME.exe` — self-contained single-file win-x64, product version **0.6.5-beta**, **182,106,633 bytes**, SHA-256 **7189CB7B997698C3784C84FEA407242BFAAD3DB9D4411B445618D75D513956FA**. Startup logs confirm .NET **8.0.32**. Published with cached packages and `NuGetAudit=false --ignore-failed-sources`; dependencies unchanged. Earlier builds are preserved.

Measured on one NVIDIA/Windows development PC. Physical microphones, speaker loopback, listening tests, AMD/Intel-only systems, hour-long recordings, third-party player compatibility with AAC pre-roll, and Windows restart/sleep/lock qualification are not covered by these tests. The new audio-only remux/probe adds export work; recording performance is unchanged by design, not newly benchmarked across hardware.

Technical reference: [Apple's AAC encoding background](https://developer.apple.com/documentation/quicktime-file-format/background_aac_encoding) describes the overlapping transform and decoder priming. Peak/loudness measurements use [FFmpeg ebur128](https://ffmpeg.org/ffmpeg-filters.html#ebur128) and unclipped float32 decode.

## Subsequent user acceptance (2026-10-28)

After restarting Windows, the user confirmed that Primary display, recording settings and startup settings were retained ("All good"). This qualifies the reported startup-preference issue by user observation; it is not a sleep/lock/device-change qualification, and the exact running executable was not independently inspected during the reboot.
