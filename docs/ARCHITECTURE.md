# FRAME architecture

FRAME 0.2.0 is a native WPF application targeting .NET 8 on Windows, with a separate FFmpeg media process. It is an engineering build, not a production-qualified implementation of every item in the product brief.

## Decisions

- **WPF shell:** uses the installed Windows Desktop SDK without a Windows App SDK bootstrapper or an embedded browser. Vector controls, event-driven redraws and a 1 Hz status timer keep the UI separate from capture delivery. The application is per-monitor V2 DPI aware; mixed-DPI interactions still require hardware acceptance.
- **Windows Graphics Capture:** FFmpeg 8.1's `gfxcapture` supplies D3D11 frames using an explicit HMONITOR or HWND. NVIDIA/AMD paths advertise GPU frames; Intel and CPU paths download to NV12. Independent mode runs one media process per selected target. Combined mode downloads and composes with `xstack`, so it has a material CPU/memory-copy cost.
- **Encoder discovery:** short synthetic frames are encoded through each backend. Only successful NVIDIA/AMD/Intel codec probes are offered, with a CPU H.264 fallback. These probes prove initialization, not zero-copy interoperability, gaming impact or sustained throughput.
- **Audio:** native WASAPI shared-mode capture. Loopback and microphone sources feed bounded, approximately one-second stereo float rings into one 48 kHz mixed pipe per pipeline. Neither enumeration nor capture changes the Windows default device, mute, volume or driver. Startup does not enumerate audio devices; the user explicitly refreshes the source list. Capture, microphone and cues default off. Audio has not passed physical-device acceptance.
- **Replay:** approximately one-second encoded Matroska segments on disk, with a byte budget and duration limit. No raw frame history is retained. Recording bookmarks the same pipeline used by replay. Pinned recording segments survive replay eviction; the quota is a retention target rather than a hard cap during export/recording.
- **Export:** select complete segment coverage, reject missing footage, trim each recorded interval with the chosen encoder, concatenate into MP4, probe the container and decode its first video frame, then move into the reserved final filename. Exact trimming currently costs a full re-encode. Integration fixtures additionally decode every frame. The exporter preserves originals on failure.
- **Recovery:** one session directory contains per-target segments, FFmpeg arguments, bounded engine logs and a recording interval journal. Failed sessions survive for recovery. Low free disk space stops capture; stalled progress is detected by the coordinator. The recovery script remuxes available segments without deleting them, but does not reconstruct pause bookmarks automatically.
- **Screenshots:** one-shot GDI capture plus a physical-pixel WinForms selection surface. A magnifier and keyboard refinement assist precise selection. WPF InkCanvas supplies annotation. It is not a continuous screen polling capture engine.
- **Advanced selection (since 2026-10-11 part of the separate app `FRAME.Advanced`, not FRAME; logic in `Frame.Advanced.Core`):** native window bounds and UI Automation image/ancestor geometry ranked by confidence. The separate opt-in motion mode compares 160×90 grayscale samples at 5 Hz with sustain, inactivity, hysteresis and cooldown. It does not classify video semantics.

## Ownership and concurrency

`Frame.Core` holds deterministic settings, state transitions, target grouping, replay coverage/retention, candidate ranking and motion rules. `Frame.App` adapts those rules to native Windows APIs and process/file I/O. No external NuGet library is required.

The UI command semaphore suppresses overlapping user actions. The coordinator semaphore serializes lifecycle changes and exports; periodic maintenance skips while it is held. Each capture process has independently drained stdout/stderr. A per-process low-space check continues while an export holds the coordinator. Audio uses bounded rings and a bounded pipe; overflow, discontinuity and stalled consumers report failure rather than grow indefinitely.

The current export gate can delay a Stop command while saving a replay. Exports and active capture can compete for the same encoder. These are documented engineering limitations, not claims of ShadowPlay-equivalent latency.

## Files and trust boundaries

Settings are validated and atomically replaced in `%LOCALAPPDATA%\FRAME\settings.json`. Thumbnail/metadata caches and rotating diagnostic logs use the same application directory. User-selected output directories hold final captures. The media engine receives structured arguments through `ProcessStartInfo.ArgumentList`; source windows and file names are not evaluated as commands.

Recovery deletion checks that the session is beneath the application's recovery root. A finalized capture is not deleted after a cleanup error. The library recycles files only after a confirmation. Protected windows are subject to Windows restrictions; FRAME does not bypass DRM or capture access checks. Screenshots and recovery segments can contain private content.

## Primary technical references

- [Microsoft: Screen capture with Windows Graphics Capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
- [FFmpeg: gfxcapture filter](https://ffmpeg.org/ffmpeg-filters.html#gfxcapture)
- [Microsoft: WASAPI](https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi)
- [Microsoft: Loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
- [Microsoft: IMMDeviceCollection](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdevicecollection)
- [Microsoft: SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)

Capture exclusion is applied to the floating toolbar/selection surface where supported; it is not a general-purpose privacy filter for other applications.
