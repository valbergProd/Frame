# FRAME 0.5.4 Beta

Thank you for testing FRAME. This guide covers installing, using, troubleshooting and removing the beta.

## What FRAME does

- **Shadow Recording** keeps the last minutes of your screen (1 to 60 minutes, default 5) in the background. When something happens, press a hotkey and those minutes are saved as a video, without having recorded everything.
- **Manual recording** records from start to stop, with pause and resume.
- **Library** lists your recordings and clips, with trimming and **Crop for social** (vertical, square or other formats for YouTube Shorts, TikTok and similar; FRAME only creates the file, it never uploads anything).
- FRAME runs quietly in the system tray, and its hotkeys work while you play.

## Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11, 64-bit.
- **FFmpeg 8.1 or newer**, a free video tool that is installed separately. FRAME helps you install it (see below).
- A graphics card with a hardware video encoder is recommended:
  - **NVIDIA (NVENC):** used and tested during development (GeForce RTX 4090).
  - **Intel Quick Sync and AMD (AMF):** detected and offered automatically when your FFmpeg supports them, but **not yet tested** with FRAME.
  - Without a hardware encoder FRAME records with the CPU (H.264), which costs noticeably more performance.
- Disk space for the replay buffer (default limit 1 GB) and your clips.

## Install

1. Run `FRAME-0.5.4-Beta-Setup.exe`.
2. Windows SmartScreen may warn that the app is not recognized, because this beta is not code-signed. Choose **More info**, then **Run anyway**, if you received the file from the FRAME team.
3. Follow the wizard. FRAME installs for your Windows account only (no administrator rights) into `%LOCALAPPDATA%\Programs\FRAME`. You can add a desktop shortcut and start FRAME at the end.
4. **FFmpeg:** if FFmpeg is missing, the Capture page shows *FFmpeg is needed to record*:
   - **Install FFmpeg (winget)** opens a window that runs `winget install --id Gyan.FFmpeg --exact --source winget`. Follow it, close it when it is done, then click **Check again**.
   - Or **Choose ffmpeg.exe…** if you already have FFmpeg 8.1+ (ffprobe.exe must be in the same folder).
   - Without winget: download a Windows build of FFmpeg 8.1+ (links on ffmpeg.org), unzip it and choose its ffmpeg.exe.

## Using FRAME

| What | How |
|---|---|
| Start / stop Shadow Recording | Click the stone on the Capture page, or use the tray menu. You can also set a hotkey for it (not set by default). |
| Save the last minutes | **Ctrl+Shift+S** (default), the *Save last …* button, or the tray menu. A small "Clip saved" note appears top-right. |
| Replay length | The slider next to the stone (also while recording; turning it down never deletes footage already recorded). |
| Manual recording | *Start manual recording* or **Ctrl+Shift+R**; pause/resume **Ctrl+Shift+P**. |
| Open FRAME | **Ctrl+Alt+F** brings FRAME to your primary display, also from the tray. |
| Change hotkeys | *Change hotkey* on the Capture page, or Settings › Hotkeys: click a shortcut, press the new combination (Esc cancels). Combinations already used by FRAME, Windows or another app are refused with a short explanation. |
| Where recordings go | `Videos\FRAME` by default. *Choose capture folder* / *View capture folder* are on the Capture page and in Settings › Storage. Trimmed clips go to `Trimmed Clips` inside that folder. |
| Library | Thumbnails, search, sort and filter. *Trim* opens the editor; *Crop for social* creates a formatted copy; the ••• menu has Play, Show in folder, Copy file path, Rename and Delete (to the Recycle Bin). |
| Start with Windows | Settings › Start-up (then *Apply settings*, at the top right of Settings). |

Settings that need *Apply settings* show the button at the top right of the Settings page as soon as you change something. Hotkeys, the capture folder and *Minimize FRAME when a game starts* save immediately.

## If something goes wrong

**Export diagnostics:** Settings › Advanced settings › Diagnostics › **Export diagnostics**. FRAME creates a ZIP (for example `FRAME-Diagnostics-2026-10-16-143000.zip`) where you choose. It contains FRAME's logs, version, Windows/CPU/GPU/display information, your FRAME settings and FFmpeg's error messages. It never contains videos, screenshots or audio, and your Windows user name, computer name, user-folder paths and e-mail addresses are replaced. Please look through it before you send it: technical logs can still mention details such as folder or device names. Nothing is ever uploaded automatically; FRAME has no telemetry.

*Open log folder* (next to it) shows the logs: `%LOCALAPPDATA%\FRAME\Logs` (one file per day, at most 5 MB each, kept 14 days, 50 MB in total).

If FRAME closed unexpectedly, the next start shows a short note on the Capture page with *Export diagnostics*.

### Troubleshooting

- **"FFmpeg is needed to record" / Start is greyed out:** install FFmpeg as described above, then *Check again*.
- **A hotkey does nothing:** another program may use the same combination (for example the NVIDIA overlay uses Alt+Z). FRAME tells you when a combination is unavailable; choose another in Settings › Hotkeys.
- **"FRAME is already running":** FRAME is in the tray (look under ^ next to the clock). Right-click it to open or quit.
- **The save note is not visible in a game:** games in exclusive fullscreen can draw over it; borderless or windowed mode shows it. The clip is still saved.
- **Recording stopped by itself:** the Capture page explains why and offers *Restart Shadow Recording*. Please export diagnostics and report it.

## Reporting bugs

Please include:

1. What you were doing when the problem occurred.
2. What you expected to happen.
3. What actually happened.
4. Whether you can make it happen again (and how).
5. The approximate time it happened.
6. The diagnostics ZIP, if you are comfortable sharing it.

Send it through the channel you received this beta from (for example the e-mail or Discord conversation). You do not need to include any personal information.

## Upgrade

Run the newer `FRAME-…-Setup.exe`. It finds the existing installation and replaces the program files. Your settings, hotkeys, capture folder, start-up choices and recordings stay as they are. Quit FRAME from the tray first (Setup reminds you).

## Uninstall

Windows Settings › Apps › Installed apps › **FRAME** › Uninstall (or Start menu › FRAME › right-click › Uninstall). This removes the program, its shortcuts and its Start-with-Windows entry. You are then asked whether to also remove FRAME's settings, logs, thumbnails and recovery files in `%LOCALAPPDATA%\FRAME` (default: keep them). **Your recordings are never removed.** FFmpeg is a separate program; remove it in Installed apps or with `winget uninstall Gyan.FFmpeg` if you no longer need it.

## Known limitations

- Tested on one development PC (Windows 11, NVIDIA RTX 4090, several displays including a 4K TV). AMD and Intel-only PCs, Windows 10, laptops with switchable graphics and clean installations on other PCs are not tested yet. Please tell us how it works for you.
- Intel Quick Sync and AMD hardware encoders are offered when detected but have not been verified.
- Exclusive-fullscreen games may hide the top-right save note.
- Stopping a recording while the captured screen is completely unchanged can take a few seconds longer.
- Recording several displays as one combined video is less tested than a single display.
- The installer and FRAME.exe are not code-signed; SmartScreen warnings are expected.
- FRAME includes the Microsoft .NET 8 runtime; you do not need to install .NET.
