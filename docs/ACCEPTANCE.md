# Interactive hardware acceptance

This is a pending test procedure, not a record of passed tests. Run outside the restricted build process, using disposable captures. Audio-device testing should be agreed separately following the E50 incident.

| Test | Pass condition |
|---|---|
| First launch | UI opens, correct displays shown, replay off, desktop/mic/cues off. No audio endpoint enumeration until Refresh audio devices is pressed. |
| Silent video | Record 15 seconds on one monitor with all audio off. Stop produces a playable MP4, a correct duration, a matching selected display and an entry in the library. |
| Pause/resume | Record a visible timer, pause for five seconds, resume and stop. Paused content is absent; beginning/end are within the documented timing granularity. |
| Source isolation | Test each monitor, a visible window and a small region individually. No extra screen areas appear in the result. |
| Two displays | Independent mode produces both files. Combined mode matches physical layout. Check mixed resolution, rotation, negative coordinates and DPI. Measure timing offset. |
| Replay sharing | Enable replay, save twice, record while replay remains active, pause/resume/stop, then disable replay. No duplicate pipeline per target; bounded unpinned retention; every file decodes. |
| Replay tail | Use a visible running clock to quantify the lag between Save and the latest included frame. |
| Audio, separate session | With explicit agreement, enable desktop audio on the chosen device, then microphone, then both. Check Windows playback before/after each test, clipping, stereo, and sync at 1/10/60 minutes. Stop on device errors. |
| Hotkeys/tray | Each shortcut works when minimized; held keys do not repeat; conflicts are reported. Quit ends capture and tray presence. |
| Screenshot | Check exact bounds and annotation at 100%, 150% and 200% scaling; keyboard movement/resize; negative-origin displays; clipboard contention. |
| Failure handling | Disconnect a test display/audio source, stop the media child, deny access to a disposable output directory, and exercise low-space handling on a bounded test volume. No false Saved status; existing files survive; recovery is understandable. |
| Smart selection | Accessible image, nested image, browser zoom, a player without accessibility metadata and an overlapped region. Manual correction wins; no claim of semantic video classification. |
| Motion | Brief blink does not trigger; sustained motion does; inactivity stops; cooldown avoids oscillation. Observe CPU for a small and large region. |
| Long session | Measure process/private memory, temporary disk use, A/V drift and game frame pacing over at least one hour. Compare capture/replay off and on with the same workload. |

Proposed acceptance budgets (targets, not results): idle normalized CPU below 0.5% over five minutes; no upward private-memory trend after warm-up in a one-hour run; no unreported missing source/file; decodable final exports; A/V drift within 50 ms at ten minutes after synchronization improvements. Game FPS impact needs a reproducible workload and a baseline before setting a credible hardware-specific budget.

Record Windows/GPU/driver versions, display topology/scaling, FFmpeg version, chosen encoder, audio format, quality settings, test duration and observed errors for each hardware run. Keep original failure segments until recovery has been reviewed.
