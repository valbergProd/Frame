# FRAME quickstart

This is an executable engineering build (0.2.3), not a production-qualified release. See [validation](VALIDATION.md) and [limitations](LIMITATIONS.md).

1. Extract the entire ZIP and open **FRAME.exe**. Keep its files together.
2. Requires **.NET 8 Windows Desktop Runtime, x64**, already installed on the development machine.
3. Requires **FFmpeg 8.1+** with `gfxcapture`, and `ffprobe.exe` in the same directory. Both are installed on the development machine. If discovery fails, select the actual `ffmpeg.exe` in Settings. Other PCs need FFmpeg separately; it is not bundled.
4. Choose the output directory and display. Start with a short silent test. The earlier direct-capture test returned access denied (`0x80070005`); its cause remains unresolved. Successful capture still needs confirmation in a normal Windows session.
5. Choose the duration beside **Start shadow recording** (default **5 min**), then start. Status shows whether background capture is active and how much history is available. **Save last 5 min** or **Ctrl+Shift+S** saves a clip. Until the buffer fills, only available history is saved. An insufficient disk budget can shorten history; the home screen warns about this.
6. **Stop shadow recording** stops background capture and removes unsaved buffer clips. Save wanted clips first. Change history length while capture is off.
7. **Start recording / Stop recording** under Manual Recording controls ordinary recording. Pause/Resume appears during recording. **Take screenshot** captures a still. Desktop audio, microphone, and sound cues default off.

Audio devices are enumerated only after **Refresh audio devices** in Settings. The app does not change the default device, Windows volume, mute, or drivers. Physical audio capture remains unvalidated. The E50 playback incident during development was resolved by power-cycling the device; its cause is unknown.

## Hotkeys

| Key | Action |
|---|---|
| Ctrl+Shift+R | Start/stop recording |
| Ctrl+Shift+P | Pause/resume |
| Ctrl+Shift+S | Save replay |
| Ctrl+Shift+C | Select screenshot region |
| Ctrl+Shift+M | Open display selection |

For region selection: drag, adjust with arrow keys (Shift resizes), then press Enter. Esc cancels. Hotkeys are configurable in Settings; conflicts are reported.

Closing the main window sends FRAME to the system tray. Active recording/replay continues. Choose **Quit FRAME** in the tray to finalize recording and exit.

Settings, logs, and recovery files live in `%LOCALAPPDATA%\FRAME`. Final captures use your selected output directory. Failed sessions are retained for recovery and may contain private screen content.

Administrator installation is not required. The package is unsigned. The source package includes build commands, architecture documentation, and a recovery script.

Version 0.2 changes the old 30-second default to five minutes; other existing history lengths remain. Capture never starts automatically.

The window has a custom title bar. Drag its top area to move; double-click to maximize. Top-right controls minimize, maximize/restore, and close to tray. Drag an edge/corner to resize.

## Change a hotkey

In Settings → Hotkeys, click the combination to change, then press the new combination, such as Alt+Z, when prompted. It saves and activates immediately. **Saved · Active** means persistence and Windows registration succeeded; Apply settings is unnecessary for hotkeys. Failure retains the old combination. Esc or Cancel aborts. Use a modifier with a regular key, or F1–F24 alone. Duplicate FRAME bindings are rejected. Windows/other-application reservations are reported when activation is attempted.
