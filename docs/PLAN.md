# FRAME implementation plan

1. Inspect Windows, SDK and capture dependencies; research official APIs.
2. Build WPF shell with reusable tokens and native vector controls.
3. Implement Windows Graphics Capture through FFmpeg 8.1, verified hardware encoders, WASAPI audio, independent/combined targets, screenshots and hotkeys.
4. Share segmented encoding between recording and disk-backed replay. Serialize transitions, bound retention, verify exports, retain recovery files.
5. Add tray, compact toolbar, settings, library, region selection and annotation.
6. Add opt-in accessibility-assisted Smart Capture and motion analysis with honest confidence labels.
7. Build, exercise deterministic tests and local hardware integration, record actual measurements, package and document remaining gaps.

Workspace: `C:\dev\Frame` (moved there on 2026-10-11; earlier work was staged in the chat's outputs/Frame).
