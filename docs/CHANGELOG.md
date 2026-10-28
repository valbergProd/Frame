# FRAME 0.6.6 · GitHub source and in-app updates · 28 October 2026 (not packaged as an installer)

- Source and installer releases use the public `torsteinvalberg/Frame` GitHub repository. Future pushes build and run Core tests on GitHub Actions; source pushes do not automatically publish an app update.
- Settings > General now offers update checks, verified downloads, cancellation and user-confirmed installation. An optional startup check runs once per launch; download/install are always user initiated. Stable installations exclude beta releases.
- Install waits until recording and exports are stopped, closes FRAME, revalidates the installer and opens normal Setup. Settings and recordings retain the existing upgrade behavior. The first newer installer still needs end-to-end upgrade qualification.
- User confirmed after a Windows restart that Primary display, recording preferences and startup settings were retained.

# FRAME 0.6.5 · replay audio quality and trim timing · 28 October 2026 (not packaged as an installer)

- Fixed an audible-risk peak transient at replay joins: independently encoded AAC at the clip's start could overshoot even with continuous timestamps. Moving replays now keep the original continuous AAC packets alongside the copied video body. Still-tail audio is encoded once from the joined original stream when padding is needed.
- Exact trim/share exports preserve video timestamp precision, avoiding an extra rounding offset relative to audio. Recording gain, mixing, devices and capture quality are unchanged.
- Added isolated diagnostics with raw PCM and independent AAC controls, deterministic recorded-source fixtures, and short/long manual recording, pause and trim synchronization measurements. See `FRAME_065_VALIDATION.md` for evidence and limits.
- Windows reboot/lifecycle qualification remains deferred at the user's request.

# FRAME 0.6.4 · reliable Stop on still screens and bounded exit · 28 October 2026 (not packaged as an installer)

- Manual Stop saves the still tail without waiting for the display to change. Recording duration includes still time and excludes pauses. Separate and combined displays are supported; Shadow can keep running after manual Stop.
- Pending audio is included from the existing 30-second audio history. Mixing, devices, recording quality and live capture timing remain unchanged; audio older than that history during a longer complete video stall can still be silence.
- Closing FRAME waits at most 60 seconds for pending work and finalization. On failure or timeout, FRAME ends its own media processes, retains recovery files and shows a recovery notice next time. Audio-device cleanup cannot block the UI's shutdown deadline.
- Failed exports keep their source segments and snapshots. Normal saves are verified before reporting success.

# FRAME 0.6.3 · primary display default and remembered preferences · 28 October 2026 (not packaged as an installer)

- The default capture source follows Windows' current primary display at launch and before recording starts, including after display IDs or the primary display change. Selecting only the primary display also saves this policy.
- Deliberate secondary/multiple-display selections remain remembered. A temporarily missing display uses the remaining selection or primary as a fallback without overwriting the saved preference; the preference returns when available, including after restarting FRAME.
- Older settings migrate once displays are available: an empty selection or the current primary alone becomes the primary policy; secondary/multiple-display selections remain explicit. Older files cannot distinguish a previous automatic fallback from a deliberate secondary choice; selecting only primary once opts into the primary policy.
- Other saved settings and recording quality, encoder, audio and timing remain unchanged.

# FRAME 0.6.2 · FFmpeg no longer keeps running after FRAME ends · 24 October 2026 (not packaged as an installer)

- Fixed: if FRAME ended without a normal Quit (crash, End task in Task Manager, a hang that was then killed), its capture `ffmpeg.exe` could keep running with nobody reading it and grow in memory for hours (12 GB seen overnight). Every FFmpeg FRAME starts is now tied to FRAME's process: Windows ends it as soon as FRAME ends, for any reason.
- At start-up FRAME also ends a capture `ffmpeg.exe` left behind by an older version (only FRAME's own capture processes for the same data folder whose FRAME is gone; other FFmpeg programs are never touched).
- Recording quality, encoder settings, audio and timing are unchanged. A normal Quit works as before.

﻿# FRAME 0.6.1 · five GUI modes, all named in the title bar · 17 October 2026 (not packaged as an installer)

- The former "Cinematic D" design is now called **Original GUI** and stays the default. The FRAME 0.5.5 design is no longer offered as a mode.
- FRAME has exactly five modes: Original GUI, Dark / No Bullshit, Pink mode, Light mode and Emo mode. All five names are shown at the top right at all times: click one to switch at once (no cycling, menu or confirmation). The active mode's name is slightly brighter.
- Saved choices carry over: a saved "Cinematic D" or old "Original GUI" opens the new Original GUI; Pink and Emo backgrounds, effects and quotes are kept. Recording, hotkeys and capture settings are not affected, and switching still never interrupts recording.

# FRAME 0.6.0 · visual modes: Cinematic D, Original GUI, Dark / No Bullshit, Pink, Light and Emo · 17 October 2026 (not packaged as an installer)

Visual modes
- FRAME has six looks over the same recording engine and settings. Switch with the small text button at the top right: it names the next mode (Cinematic D → Original GUI → Dark / No Bullshit → Pink mode → Light mode → Emo mode). An "Original GUI" button next to it always brings back the 0.5.5 design. The title bar shows the active mode (e.g. "FRAME · Pink mode").
- A switch is instant, needs no Apply and is remembered after a restart. It never stops or restarts recording and never changes your replay length, displays, quality, hotkeys or audio.
- **Cinematic D is the new default** (also for existing installations): a large slowly turning obsidian stone, the title next to it, a horizontal replay-length slider with every available length, a quiet manual-recording control, and display, quality and audio in one panel at the bottom. The stone is one light source: its fissures, the light around its whole outline, the glow, the warm light on the ground and a subtle warmth on the nearby title and slider all breathe together while Shadow Recording is active.
- **Original GUI**: the FRAME 0.5.5 design, unchanged (vertical minute slider, stone, living desert).
- **Dark / No Bullshit** and **Light**: plain, practical layouts in grey or white. No stone, scenery, glow or animation at all.
- **Pink mode**: an illustrated pink world. Pick one of six backgrounds (five illustrations or "Simply Pink") and switch effects on or off with the small icons under the mode buttons: hearts, flowers, sakura petals, sparkles, butterflies, ribbons and bows, and three playful dogs. Any combination works, all of them at once too. Effects share one wind and react to each other, and there are a few small secrets to find. First visit: Sakura garden with sakura petals; after that your own choice is kept.
- **Emo mode**: dark, melancholic illustrations (Simply Emo, Dead rose garden, Rainy neon alley, Moonlit graveyard), optional dark rose petals, rain, bats and fog, and a quiet quote that changes each time you open Emo mode and every 10 minutes while FRAME is on screen.
- All decorative motion stops completely while FRAME is minimized, in the tray, on another virtual desktop, behind the lock screen, the display is off or a screen saver runs (new: a secure screen saver now counts as "not visible").

Removed
- "On-screen controls" (the floating manual-recording bar with timer, pause, stop and save) and its setting. Manual recording itself is unchanged: start/stop on the Capture page, with its hotkeys or from the tray, and it is saved when you stop or close FRAME.
- "Light appearance" in Settings: use Light mode instead.

# FRAME 0.5.5 · beta refinements, recording reliability and a living desert · 17 October 2026 (not packaged as an installer)

Recording
- If the display you chose is switched off or disconnected (for example a TV disabled in the graphics control panel), FRAME now keeps your other selected displays or, if none is left, selects your Windows primary display and says so once. Recording and automatic start no longer fail with "A selected display is disconnected", and the Capture page never shows "0 displays" when a display is available. If the display comes back in the same session, your original choice returns.
- FRAME records displays only: the "Choose window / game" and "Region" modes are gone.
- You can change the recording quality (Lightweight, Balanced, Original) while Shadow Recording runs. FRAME restarts Shadow Recording with the new quality automatically. Your replay length (for example 15 minutes) stays exactly as chosen; footage recorded before the change is cleared, and FRAME says so. If the new quality cannot start, the previous one is restored. The same applies to Settings › Recording › Apply settings while Shadow Recording runs.
- The close button (X) now exits FRAME completely: a manual recording is saved, Shadow Recording stops and nothing keeps running. Minimize still hides FRAME in the system tray.

Sound
- FRAME makes no sounds of its own: the recording cues, the "Cues" and "Notification volume" settings and the Windows message-box sounds are gone (FRAME's own dialogs are silent). Recorded desktop audio and microphone are unchanged.
- Recorded audio level was measured: FRAME does not lower or raise the level (a test tone comes out at exactly the same peak, RMS and loudness, without clipping near full scale).

Interface
- Hotkeys show the keys of your keyboard layout: Alt+Å instead of "Alt+Oem6" (also Ø, Æ and other layouts). Your shortcuts are unchanged.
- Settings › Hotkeys: "Open FRAME" is now "Show FRAME window".
- Library: thumbnails are made when you open Library, also while Shadow Recording runs (newer clips had none), visible clips first; broken cached thumbnails are replaced; nothing is generated in the background while Library is closed.
- Trim has the FRAME title bar, like Crop for social.
- Settings: a small information icon explains Recovery; Light appearance is shown at once when you tick or untick it and saved with Apply; Apply keeps you on Settings at the same place (it used to jump to Capture), and appearance, notifications and start-up options now apply while recording. "Reduce visual motion" is gone (decorative motion stops whenever FRAME is not visible and follows the Windows animation setting).
- The Capture page no longer moves when recording starts or stops: the "00:01 available / 10 min" line is gone (how much can be saved is in the Save button's tooltip); notes such as an audio interruption appear in a reserved line.
- The dot next to SHADOW RECORDING is grey when off, an amber ring while starting or stopping, and amber while recording.
- Manual recording is quieter (graphite edge instead of orange). The replay-length slider is easier to see, with a "replay length" caption.
- A living desert: fine wind-driven sand and dust with gusts, sand hopping and creeping over the dunes, flowing light along the dune crests. The obsidian stone turns slowly (one turn per 40 seconds) and pulses warmly while Shadow Recording is active. All of it stops completely while FRAME is minimized, hidden or in the tray.

# FRAME 0.5.4 Beta · updated .NET runtime · 16 October 2026

- FRAME now includes the latest .NET 8 runtime (8.0.32 instead of 8.0.8), with Microsoft's accumulated security and reliability fixes. You still do not need to install .NET yourself.
- Nothing else changes: same features, settings, hotkeys and recordings. This installer replaces the earlier 0.5.4 Beta installer from the same day.

# FRAME 0.5.4 Beta · public beta release · 16 October 2026

- **Windows installer:** `FRAME-0.5.4-Beta-Setup.exe` installs FRAME for your account (no administrator rights), adds a Start-menu entry and an optional desktop shortcut, upgrades in place and uninstalls cleanly. Settings and recordings are never removed by installing, upgrading or uninstalling.
- **Diagnostics:** FRAME keeps local logs (`%LOCALAPPDATA%\FRAME\Logs`, 5 MB per file, 14 days, 50 MB in total). Settings › Advanced › **Export diagnostics** saves a ZIP with redacted logs and system details for a bug report. Nothing is uploaded; no telemetry.
- **Crashes:** a fatal error is logged before FRAME closes; the next start shows a short note with Export diagnostics.
- **FFmpeg assistant:** when FFmpeg is missing or too old, the Capture page explains it and can open a winget installation window for you, or let you choose ffmpeg.exe.
- An unreadable settings file is kept as a copy instead of being replaced silently.

# FRAME 0.5.4 Shadow hotkey and primary display · 15 October 2026 (not packaged)

- Ctrl+Alt+F now always brings FRAME to your primary display (from the tray, minimized, or open on another screen), keeping its size; Shadow Recording keeps running.
- New hotkey to start or stop Shadow Recording, also while gaming or with FRAME in the tray. It is not set until you choose one (Capture › Change hotkey or Settings › Hotkeys).
- The Capture page shows both shortcuts: the Shadow on/off hotkey and "Hotkey for saving the 5 minutes" (follows the replay length). Your existing hotkeys are unchanged.
- Apply settings now sits at the far right of the Settings heading.

# FRAME 0.5.4 Apply in the heading · 15 October 2026 (not packaged)

- Apply settings now appears next to the Settings heading only when you have unsaved changes, and stays in place while you scroll. It disappears after applying or when you change things back.
- "Minimize FRAME when a game starts" is now the last option under Start-up; the separate Games section is gone. It still saves immediately.
- Settings and Library content no longer scrolls underneath the heading or the status line.

# FRAME 0.5.4 five refinements · 15 October 2026 (not packaged)

- The gold corners in the sidebar are now centred precisely on the icon and name.
- Crop for social has the same title bar and window buttons as the main FRAME window.
- Expand/collapse arrows are amber when open and grey when closed, also right after clicking or while hovering.
- Settings: Start-up and Games now sit directly below Hotkeys; the hotkey list starts with Save shadow recording, then manual recording. Your hotkeys and settings are unchanged.

# FRAME 0.5.4 sidebar corners · 15 October 2026 (not packaged)

- The active page in the sidebar is now framed by four thin gold corners, like the FRAME logo, sized to its icon and name. No highlighted block, bar or dot.
- Clicking a sidebar item no longer leaves a focus rectangle; keyboard users see a quiet dotted underline instead, and Tab now goes Capture → Library → Settings.

# FRAME 0.5.4 "Crop for social" · 15 October 2026 (not packaged)

- "Share" is now called "Crop for social" in the Library, the trim editor and its window title. It still makes a formatted copy on your PC (vertical, square, crop, fit or blur) and never uploads anything; your saved choices are kept.

# FRAME 0.5.4 UI refinements · 15 October 2026 (not packaged; tested)

- Every display card now shows its number and full resolution (for example 1920 × 1080), still in your real Windows arrangement such as a TV above the other screens.
- Apply settings is grey until you change a setting that needs it, turns orange when something is pending, and goes grey again if you change it back or after applying. Pending changes survive switching pages.
- The sidebar marks the current page with a small amber dot and warmer text instead of a highlighted block.
- "Start manual recording" and the save hint are smaller and fit their text.

# FRAME 0.5.4 display selector and tidier Settings · 15 October 2026 (not packaged)

- Fixed: the display map no longer overlaps when monitors of different sizes are connected (for example a 4K TV beside 1080p screens). Displays keep their real Windows arrangement, scale to the available space and stay readable at the smallest window size.
- Choose displays by clicking them: selected displays get an orange outline, and several can be selected. The separate checkbox row is gone. At least one display always stays selected.
- "Choose window / game" and "Region" are now the same size.
- "Reset to displays" is now "Switch to display capture" and only appears while a window or region is being captured.
- Settings moved to the bottom of the left sidebar.
- Settings: Recording, General and Advanced settings fold open and closed (collapsed by default); Hotkeys and Storage stay open.

# FRAME 0.5.4 living desert · 15 October 2026 (not packaged)

- The lower dunes of the background now flow very slowly, like waves of sand (20–40 s cycles, a few pixels). The motion stops completely when FRAME is hidden, minimized or in the tray, and follows "Reduce visual motion".
- The brightest upper-right part of the background is calmer, so the recording stone stays the centre of attention.
- "Start manual recording" now has the same translucent look as the save hint, slightly more present.
- Small text in the lower Capture area and the status bar is a little easier to read.

# FRAME 0.5.4 quieter capture page · 14 October 2026 (not packaged)

- The replay-length slider is now minimal and half as tall: a thin graphite track with a small graphite handle and a muted amber hint; it sits a little closer to the recording stone.
- The glow around the recording stone is softer, so the molten core stays the centre of attention.
- The "Press … to save" hint blends into the page with a more transparent background.

# FRAME 0.5.4 obsidian shard slider · 14 October 2026 (not packaged)

- The replay-length slider now sits left of the Shadow Recording stone, with a new handle: an obsidian double pyramid (pointing up and down) with a glowing seam.
- Replay length can be changed while Shadow Recording runs; the next save uses it at once. Turning it down never deletes footage already recorded in the session.

# FRAME 0.5.4 open hotkey and faster saving · 14 October 2026 (not packaged)

- New hotkey Ctrl+Alt+F opens FRAME from anywhere (also from the tray or while recording and saving); change it in Settings → Hotkeys.
- Shadow replays save about 3–4x faster than the previous version (measured on real 1440p60 gameplay: 5 minutes in ~13 s instead of ~48 s), with unchanged quality and sync and very little CPU use while gaming.
- Replays now end exactly when you press the save hotkey.

# FRAME 0.5.4 audio sync and fast saving · 14 October 2026 (not packaged)

- Fixed: sound in Shadow Recording replays was about half a second late. Audio and video now share one start time; measured offset went from about +0.6 s to within ±30 ms, with no drift over 5 minutes (desktop audio and microphone).
- Replays save much faster: the recorded video is copied instead of re-encoded (about 4x faster and 9x less CPU at 1440p60, no GPU encoding), with unchanged quality. Clips start and end exactly at the requested time.
- Fixed: saved clips had a tiny hitch every second and could be ~3 % shorter than requested; manual recordings no longer lose a sliver of audio every second.

# FRAME 0.5.4 capture page alignment · 14 October 2026 (not packaged)

- "Choose capture folder" and "View capture folder" now sit directly under the Shadow Recording label, aligned with its left edge, and stay put when the label changes.
- The minute slider is vertically centred on the obsidian stone.
- The minute slider handle adapts to the updated Minute_rock image.

# FRAME 0.5.4 slider polish · 14 October 2026 (not packaged)

- The minute slider sits closer to the Start Shadow Recording text, and its obsidian stone is about 20 % smaller. The minute value still follows the stone.

# FRAME 0.5.4 minute slider · 14 October 2026 (not packaged)

- Capture page: the replay-history dial is now a slim vertical slider with an obsidian stone as the handle. Drag up for more minutes; mouse wheel and arrow keys also work. The value ("5 min") follows the stone. Same choices (1–60 min) and the same saved setting.
- The "Performance" button is gone from the title bar. FRAME still pauses all decorative animation automatically when hidden, minimized or in the tray. "Reduce visual motion" remains in Settings → General.
- Settings: Storage now follows Hotkeys; desktop audio source, microphone source and Refresh audio devices sit directly below Output resolution.

# FRAME 0.5.4 typewriter fix · 14 October 2026 (not packaged)

- "Preparing capture…" types in letter by letter again and loops until capture is ready — also when Performance mode is on (it previously showed static text there). Still paused when FRAME is hidden, minimized or in the tray.

# FRAME 0.5.4 obsidian polish · 14 October 2026 (not packaged)

- The obsidian stone no longer moves when the label changes between Start / Starting… / Stop / Stopping recording… (fixed position, measured identical in every state).
- While Shadow Recording starts, "Preparing shadow recording..." types in letter by letter (repeats with a short pause, stops as soon as recording is active or the start fails; paused when FRAME is hidden).

# FRAME 0.5.4 obsidian button · 14 October 2026 (not packaged)

- Capture page: the Shadow Recording button is now an obsidian stone — dark when off, a glowing molten core when on (smooth transition once the engine confirms), with gentle hover, a tactile press and a very subtle breathing halo that stops completely when FRAME is hidden, minimized or in Performance mode. Labels and behaviour unchanged.

# FRAME 0.5.4 hotkeys · 13 October 2026 (not packaged)

- Changing a hotkey: click the field, it shows "Change key…", press the key or combination — saved and active immediately, also while recording. No dialog; Esc cancels. Single keys F1–F11, F13–F24, Pause and Scroll Lock work alone; keys that would block typing or game input everywhere (a letter alone, Shift + letter) and Windows shortcuts (Alt+F4, F12 …) are refused with a short note, and the previous hotkey is kept.

# FRAME 0.5.4 main page · 13 October 2026 (not packaged)

- Capture page: "View capture folder" next to "Choose capture folder", directly below Start shadow recording; opens the capture folder in File Explorer, also while recording.

# FRAME 0.5.4 settings fix · 13 October 2026 (not packaged)

- Settings › Storage: "Choose capture folder" and "View capture folder" now sit side by side on one row (the second button had wrapped to a new row); the folder path is shown above them.

# FRAME 0.5.4 recording controls · 13 October 2026 (not packaged)

- Fix: Stop Shadow Recording responds immediately ("Stopping recording…"), also while a replay is being saved; duplicate clicks are ignored; a discarded buffer no longer waits up to 12 s for FFmpeg on a still screen.
- Hotkeys can be changed while recording; the previous hotkey stays active if the new one cannot be registered.
- Save replay works right after starting: the replay length is a maximum (1 s recorded → about 1 s saved), including before the first segment is complete; clear "Replay not saved" if no video exists yet.
- Settings › Storage: "View capture folder" next to "Choose capture folder".

# FRAME 0.5.4 audio reliability fix · 13 October 2026 (not packaged)

- Fix: Shadow Recording no longer stops with "Audio output stalled for more than one second" / "Audio device interrupted". Cause: FFmpeg stops reading the audio input while the screen is still (no video frames), the audio writer blocked and FRAME declared an audio failure when the screen changed again. Audio is now produced on the wall clock without waiting for FFmpeg and spooled (max 30 s of real audio, the rest timed silence), so A/V timing stays exact.
- Lost or unavailable audio devices no longer stop recording: video continues, the source is replaced by silence and reconnected automatically (also after the Windows default device changes). Notes: "Audio interrupted · Recording continues without audio" and "Audio restored"; status line and tray say so.
- Fix: replays spanning a still period longer than ~10 s with audio no longer lose the picture after the still (muxer waits for the audio). FFmpeg's "Guessed Channel Layout" warning removed.
- Desktop audio is on by default for new installations (microphone stays off; saved choices are kept). Details: [audio report](FRAME_054_AUDIO_VALIDATION.md).

# Project separation · 13 October 2026 (FRAME source 0.5.4, not packaged)

- FRAME Advanced Capture is now an independent product in its own repository (`C:\dev\AdvancedCapture`, version 0.2.1: own solution, engine copy, tests, build/validate/package scripts and documentation). It was built, tested (incl. a real recording) and verified before anything was removed here.
- This repository builds only FRAME: `Frame.Advanced` and `Frame.Advanced.Core` removed from `Frame.sln` and `src/`; Advanced tests moved; `Publish.ps1` publishes only FRAME.exe; `Run-Advanced.ps1` removed. FRAME's behavior and recording pipeline are unchanged. Rollback snapshot: `work/rollback/20261013-premigration-0.5.4/`. Details: [separation report](FRAME_SEPARATION_VALIDATION.md).

# FRAME 0.5.4 Beta · 13 October 2026 (not packaged)

- Save Replay feedback: an accepted save (hotkey, button, tray, toolbar) shows "Saving replay… · Processing your clip" at once (21 ms measured on real capture), then "Clip saved · Last N saved" or "Replay not saved". Repeated presses during a save show "Still saving replay…" instead of being dropped; one overlay, no stacking. Save pipeline, timing and priority unchanged.
- Minimize FRAME when a game starts (Settings → General → Games, default on): event-driven via the existing foreground WinEvent hook; known game executables or Direct3D exclusive fullscreen only (browsers and video players never count); once per game process, never while an editor or dialog is open; capture, hotkeys and tray keep running.
- Fix: the Library ••• menu was empty (light text on WPF's light default menu). Menus are now themed; actions Play clip, Show in folder, Copy file path, Rename, Copy file, Delete clip (confirmation, Recycle Bin, locked files reported).
- Fix: trim editor Save clip did nothing while Shadow Recording ran (re-encoding was silently refused). It now saves in a low-impact mode during capture (CPU x264, Idle priority, two threads) into `<capture folder>\Trimmed Clips\`, with progress, a note and Library refresh; failures are shown and logged.
- Save to… uses the native Save As dialog (files visible, editable name) and remembers the last folder across restarts, separately from Save clip. Library lists Trimmed Clips. Details: [0.5.4 report](FRAME_054_VALIDATION.md).

# FRAME 0.5.3 Beta · 13 October 2026

- Shadow Recording keeps running on an unchanged screen and now also *starts* on one: encoders run without frame delay (NVENC -delay 0 -zerolatency, x264 zerolatency, QSV async_depth 1, AMF lowlatency when accepted) and a single source uses output-side constant frame rate instead of the fps filter, which held the newest frame back.
- Saves end at the moment of the request: FRAME waits up to 1.5 s for the current segment, otherwise adds the still tail (open segment + a fresh frame of the unchanged source, held to the request time; audio padded). Verified with real Windows capture: final still image included, 8 s window exact.
- Unexpected stops are classified (disk, encoder, source, audio, start, engine), restarted once automatically when sensible (max 3 per hour), and reported with a top-right note, an orange tray badge and an in-app explanation with Restart / Copy details / Technical details; persisted across restarts as "last session".
- "Shadow Recording active" note after a confirmed start (including automatic start after sign-in); all tray balloon pop-ups removed. Details: [0.5.3 report](FRAME_053_VALIDATION.md).

# FRAME 0.5.2 Beta · 13 October 2026

- Fix: recording no longer stops with "Capture stopped delivering frames" when the screen is unchanged for 15 s (Windows Graphics Capture only sends frames on change). Real failures (FFmpeg exit, lost display or device) still stop safely; long still periods are logged.
- New: Start FRAME with Windows (hidden in the tray, per-user start-up entry) and Start shadow recording when FRAME starts, in Settings → General → Start-up.

# FRAME 0.5.1 Beta · 13 October 2026

- First packaged beta: self-contained win-x64 single-file `FRAME.exe` and `FRAME-Advanced.exe` (no .NET install needed), README, testing guide and license notices. FFmpeg is not bundled; FRAME also finds a WinGet-installed FFmpeg and `toolsfmpeg.exe`.
- Local diagnostics log now records start-up, engine detection, export/trim failures and unexpected errors (no telemetry). Details: [beta report](FRAME_051_BETA_VALIDATION.md).

# FRAME 0.5.1 · 12 October 2026

- Capture-folder browsing shows files; title-bar mark removed; main logo larger and higher; one gold brand icon for exe, taskbar and tray (turquoise retired).
- Clearer, state-driven recording pulse; idle history sentence removed; circular "Display settings" toggle.
- Subtle "Clip saved" toast (actual length) or "Clip not saved" after shadow saves: top-right of the active display, click-through, no focus, excluded from capture, ~2.5 s; toggle in General.
- Settings reorganized: Hotkeys, Recording, Storage, General, collapsed Advanced settings (FFmpeg, encoder, diagnostics). Details: [0.5.1 report](FRAME_051_VALIDATION.md).

# FRAME 0.5.0 · 12 October 2026

- Phase 3: Library cards with resolution/duration, sorting, Trim/Share/menu actions and clearer empty/missing states.
- New clip editor: preview, thumbnail timeline, precise handles and time fields, frame stepping, selection playback/loop, Save clip / Save to… and direct Share of the selection.
- Trimmed exports always re-encode for exact, verified boundaries; lazy, cached, cancellable timeline thumbnails; editor releases everything on close. Details: [0.5.0 report](FRAME_050_VALIDATION.md).

# FRAME 0.4.0 · 12 October 2026

- Phase 2 Smart Sharing: Library Share modal with YouTube/Shorts/TikTok/Discord/Custom presets, Original/Crop/Fit/Blur framing with preview, verified local export with progress and cancel, upload-page/Explorer/clipboard shortcuts.
- No accounts or direct upload (dropped by user decision). Details: [0.4.0 report](FRAME_040_VALIDATION.md).

# FRAME 0.3.6 · 12 October 2026

- Phase 1 GUI refinement: edge-free breathing glow halo, organic noise-driven glow/sand/dune motion, tooltip fade and clearer Region/window wording.
- Hidden-window suspension unchanged and now covered by tick-count checks. Details: [0.3.6 report](FRAME_036_VALIDATION.md).

# FRAME 0.3.5 · 12 October 2026

- Increase sand density, add subtle bottom dune flow and strengthen recording glow/pulse.
- Keep effects running during recording and focus changes; suspend in tray/minimized/Performance mode.
- Remove all Performance button borders; use primary gold logo for the transparent tray icon.

# FRAME 0.3.4 · 12 October 2026

- Enable main sand/pulse by default, including legacy preferences; add a subtle persistent Performance toggle in the title bar.
- Refine loading to Preparing capture… with 55ms typing and a short hold between loops. Advanced unchanged.

# FRAME 0.3.3 · 12 October 2026

- Type the entire startup loading label at 40ms per character in a loop until ready; retain immediate stop on ready/failure and suspension while hidden.

# FRAME 0.3.2 · 12 October 2026

- Spread active recording glow outward; add optional slow pulse and lightweight sand drift with visibility/focus/capture suspension.
- Show Loading immediately during engine probing, then reveal the remaining label rapidly; stop on completion/readiness and show an honest failure state.
- Preserve static motion preference and Advanced behavior. Verification and limits: [0.3.2 report](FRAME_032_VALIDATION.md).
# FRAME 0.3.1 · 12 October 2026

- Move capture-folder selection from Settings to a subtle home button with immediate persistence, write probe and rollback; lock it during capture.
- Center Shadow text in the wordmark font, remove Shadow/manual dots, rename manual actions, add static active ember glow to both recording controls.
- Show configured Save hotkey and requested duration; Change hotkey navigates directly to Settings/Hotkeys.
- Rename Window to Choose window / game; explain window versus fixed-region video capture in tooltips. Both remain pending user review.
- Remove FRAME screenshot capture, tray action, default/legacy hotkey and dispatch. Old PNGs open in the Windows viewer; Advanced screenshots remain unchanged.
- Add isolated diagnostic mutex for non-capturing render/dialog tests. Running production instance remains untouched.
- Verification: complete isolated solution build 0 warnings/errors; 100 Core tests, 29 native checks and four WPF hotkey-dialog checks pass. Dark/light/minimum/active presentation reviewed. Standard output was locked by the running app; fresh copy is builds/FRAME-0.3.1/FRAME.exe. [Details](FRAME_031_VALIDATION.md).

# FRAME 0.3.0 / FRAME Advanced 0.2.0 · 12 October 2026

- Implement the approved Desert Drift reference in native WPF: warm abstract background-only asset, sparse navigation, translucent rectangular Shadow action, functional history ring and compact secondary controls. Advanced receives the same tokens with its own monitoring layout. Dark/light themes and WindowChrome remain; no slogan or Advanced controls in FRAME.
- History dial supports pointer capture/preview/commit, keyboard/wheel/UI Automation, presets plus valid custom settings, atomic persistence and rollback. Active capture locks changes with a save-and-stop explanation; Save reflects actual valid coverage and distinguishes warm-up/gaps. Configured hotkeys remain authoritative.
- Shadow Replay classification uses exact game executables, captured sources, bounded interval metadata and foreground-change events; Fortnite routes to Fortnite, cs2 to CS. Unknown/mixed/alt-tab/unreadable metadata falls back to root. Verified export is preserved through collision-safe folder routing/failure fallback. Library includes owned canonical direct game folders, excluding Advanced and unrelated trees. Manual/screenshot/Advanced destinations are unchanged.
- Remove FRAME's repeated hide-to-tray reminder while retaining capture/tray/Quit and genuine notifications. Advanced Close still quits.
- Persist ReduceMotion in each app (static default). Optional three-pixel/1 Hz drift stops on hide, minimize, deactivation, capture/monitoring and shutdown, with single lifetime subscriptions.
- Final input audit: picking up the minimum knob just left of twelve o'clock no longer jumps to maximum; Escape restores the committed value. Unchanged history ticks avoid unnecessary dial redraws; theme changes explicitly refresh it. Two native regressions added.
- Fix native-render findings: light-mode root background/contrast, primary text size inheritance and minimum-window spacing. Add isolated main interaction diagnostics, Advanced native render/idle assertions and two game-routing media checks.
- Verification executed: baseline75/75; final Build.ps1 **0 warnings/0 errors, 99 passed/0 failed**; **23** native main checks; **4** WPF hotkey-dialog checks; **9** synthetic-media checks. Both apps dark/light/minimum renders; FRAME150% rasterization and Library render. No physical audio/user captures tested. Real global hotkeys, live game/Advanced capture, mixed DPI/Snap and gameplay FPS remain unqualified. Full evidence/limitations: [Desert Drift validation](DESERT_DRIFT_VALIDATION.md).
- Source versions advanced; no existing releases/dist overwritten or new runtime package published. Fresh binaries are under each app's bin/Release/net8.0-windows directory.

# 0.2.4 · 11 October 2026 (built; 66/66 Core tests passed before the preview/stills work; not rendered or hand-tested)

- Title bar refined within the existing WindowChrome (36 px caption, 6 px resize border, native system commands unchanged): lifted cool gradient surface distinct from the page background, cyan-to-violet edge line with a short cyan accent segment, small brand mark, and a faint cyan/violet window outline hidden while maximized.
- Caption buttons now use a dedicated style with visible hover/pressed states (accent tint; rose fill with white glyph for Close) and a keyboard focus outline. Min/Max glyphs use aliased edges and layout rounding for crisp lines at fractional DPI scaling.
- Title bar colors follow the dark/light appearance setting through ApplyTheme. No behavior, hotkey, capture or settings changes.
- Advanced Capture (window motion recording) extended; the existing 5 Hz window sampling, `MotionTrigger` and shared capture pipeline are kept. Window dropdown (application name + title; `#n` and size for identical windows; minimized flag) is built by `WindowCatalog` from one native `EnumWindows` pass on page visit or explicit Refresh only. `.txt` document windows, invisible/cloaked/tool/owned/shell windows, FRAME itself and tiny windows are excluded.
- One Activate / Deactivate button: disabled until a valid window is selected; Activate only arms monitoring and never records by itself; Deactivate keeps the selected window. A closed window ends the session safely and finalizes any running recording.
- The Activate / Deactivate button and its status line were moved to the very top of the Advanced page and enlarged (88 px high, 26 pt bold, full width); the shared `PrimaryAction` style is unchanged. Not yet rendered.
- Window list freshness: besides page visit and Refresh, the list is now refreshed when FRAME is activated again (e.g. after switching browser windows) and just before the dropdown opens. Event-driven only, no polling. Process names are cached per process to keep repeated listings cheap. The user reported that Refresh did not reflect a switched browser window; the root cause was not reproduced, so this is **unverified**.
- Independent Advanced storage folder (`AdvancedOutputDirectory`, default Videos\FRAME\Advanced): native folder picker, path display, Open in Explorer, persisted, validated and write-probed before activation and before each recording. Standard and shadow output are unaffected.
- Safeguards: a start queued behind another action is ignored if Advanced was deactivated meanwhile; starting shadow or manual recording while Advanced is only watching deactivates it with a notice and is refused while Advanced is recording.
- `Settings.ForAdvancedCapture()` returns a copy that records to the Advanced folder (used by the media validation). An earlier parallel implementation was excluded from the build (later moved to `work/retired/`).
- 15 new Core test statements (66 in total) cover the catalog, selection/activation state and storage. Build and all 66 tests passed on the user's machine on 2026-10-11 (Core logic only; UI and capture behavior untested).
- Preview window (Advanced page, **Preview** button): live view of the selected window at 5 Hz while open (one snapshot when inactive), a box around the changing region, a change meter against the motion threshold, classification (stable / change / image / video) and counters. New `StillDetector` and `FrameAnalysis` in `Frame.Core/ContentDetection.cs`: a short burst of change that settles saves a full-size PNG still to the Advanced folder (setting `AdvancedStills`, default on, checkbox on the page; 4 s cooldown; perceptual-hash duplicate skip; never while video records); sustained change keeps starting a recording through the unchanged `MotionTrigger`. 9 new Core tests (75 in total) whose logic was checked with a Python port only; not built or run since.
- **Split into two apps (2026-10-11, user request; built with 0 warnings/0 errors on the user's machine, both apps start; behavior untested):** window-motion Advanced Capture (window dropdown, Activate/Deactivate, preview, stills, video recording, destination) moved out of FRAME into the new project `src/Frame.Advanced` (`FRAME.Advanced.exe`, v0.1.0) in the same solution. It has its own title bar in FRAME's style, settings, recovery folder, diagnostics log and single-instance mutex, and a **Media engine** row (ffmpeg path, Check) because it no longer shares FRAME's settings page. On first start it copies the Advanced and encoder settings (never audio) from FRAME's `settings.json`, read-only; an unreadable own settings file is kept as `settings.json.unreadable`.
- The capture engine is shared as linked source (`Backend`, `CaptureCoordinator`, `AudioPipe`, `AudioDevices`, `MediaExport`, `Native`, `Screenshot` stay in `Frame.App`; `Frame.Advanced.csproj` compiles them too).
- FRAME: the Advanced page and its menu entry were removed entirely (user request: FRAME keeps shadow recording, manual recording, screenshot and library). Smart screenshot, entire-desktop and delayed-selection screenshots moved to the new app (saved to its folder, annotation window opens). The old Smart video region button was not carried over. The window-motion code, its fields, `--render-advanced`, the shadow/manual-recording hand-off (`YieldAdvancedTo`) and the smart parameters of `TakeScreenshot` were removed. `AdvancedPreview.cs` and the earlier unused Advanced files moved to `work/retired/` (nothing deleted).
- `Frame.Core` no longer contains Advanced code or settings: window catalog, activation state, storage probe, `StillDetector`, `FrameAnalysis` and `MotionTrigger` moved to the new library `Frame.Advanced.Core`, together with the new `AdvancedSettings` (own file, own validation). `Settings` lost `AdvancedOutputDirectory`, `AdvancedStills` and `Motion*`; `Settings.ForAdvancedCapture()` and its media-validation check were removed. Window enumeration and `PrintWindow` sampling moved from `Native.cs` into `Frame.Advanced/WindowNative.cs`. Five tests were rewritten for `AdvancedSettings` (75 statements in total).
- `Frame.sln` includes the new project; `Publish.ps1` publishes both executables into one folder; `Run-Advanced.ps1` added; `Install-Project.ps1` copies it.

# 0.2.3 · 10 October 2026

- Fix recursive Close from the hotkey dialog Deactivated event. Escape, Cancel and window close remain available.
- Hotkeys now save and activate immediately, independently of capture settings. Saved / Active appears only after Windows registration and atomic settings save succeed.
- On failed registration or persistence, retain the previous binding and do not display a success label.
- Four real offscreen WPF modal-dialog regression checks pass; physical keyboard and OS conflict behavior remain user-verification items.

# 0.2.2 · 10 October 2026

- Hotkeys replace typed shortcut fields: click a binding, press a combination, then Apply settings. Waiting feedback, Escape/click-away cancellation, modifier handling and duplicate detection are included.
- FRAME global bindings are temporarily unregistered during selection and restored afterwards; changed bindings activate on Apply.
- Navigation tracks the actual page with a cyan edge and selected background for Capture, Library, Advanced and Settings. Page navigation resets the scroll position.
- 51 deterministic checks pass, including six hotkey selection checks.

# 0.2.1 · 10 October 2026

- Cyberpunk shell with teal/violet gradients, a fine grid and static circuit traces on the background and sidebar, adapted for dark and light appearance.
- Borderless main window with a custom caption and minimize, maximize/restore and close controls. Native WindowChrome retains caption dragging, double-click maximize and edge resizing. Closing still uses the existing tray behavior.
- Visual changes only; no capture, audio or retention changes.

# 0.2.0 · 10 October 2026

- Shadow recording is the primary home action, with an explicit Start/Stop button and an adjacent history selector (1–60 minutes; existing custom durations remain available).
- New default: five minutes. Legacy 30-second defaults migrate once; other saved durations and all audio preferences are preserved.
- A separate Save button, actual buffered-time meter, warm-up behavior and operation feedback clarify the workflow. Low disk-budget estimates are surfaced beside the buffer status.
- Manual recording, pause/resume and screenshots remain separate controls. Tray and shortcuts share the same shadow actions.
- Cyan/violet gradient hero, history dial, compact source controls and revised dark/light appearance.
- 45 deterministic tests pass, including migration and custom-duration persistence. Live capture hardware acceptance remains pending.
# 0.1.0 · 10 October 2026

Initial native Windows engineering delivery: WPF visual system, capture/replay processes, source selection, screenshot/annotation workflow, tray/hotkeys, configuration, library and experimental advanced selection/motion mode.

Final hardening:

- Audio endpoint discovery is explicit and on demand; audio capture and notification cues default off.
- Preserve a saved audio endpoint instead of silently replacing it when it has not been discovered.
- Reject incomplete segment coverage before export, retaining originals on failure.
- Validate independent/combined source grouping and reject duplicate/invalid targets.
- Preserve semantic image candidates when duplicate bounds have higher nonsemantic confidence.
- Handle journal failure during finalization and release resources during failed shutdown.
- Detect stalled frame progress; cap media logs and rotate application diagnostics.
- Keep finalized output valid even if temporary cleanup fails.
- Correct screenshot bounds and annotation mode transitions; show Custom for non-preset quality settings.
- Expand deterministic and synthetic failure-path validation; package runtime files, documentation and source separately.

Hardware acceptance remains pending as detailed in LIMITATIONS.md.
