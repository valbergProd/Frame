# FRAME 0.3.4 — default atmosphere and Performance control

2026-10-12. Main app only; Advanced retains 0.2.0 and its existing motion default.

- Startup label is now **Preparing capture…**, typed at 55ms per character, holding the complete phrase for approximately 0.7 seconds before repeating. Readiness/failure stops it immediately; hidden windows suspend it.
- Sand and active glow pulse default on in both themes. Existing settings without AtmosphereVersion adopt this user-requested default once; subsequent saved Performance choices persist through restart. No recording, audio or capture-quality setting changes.
- A subtle gray **Performance** button appears at the upper right, before the window controls. Clicking toggles ReduceMotion with atomic save before applying. **Performance · on** means decorative motion is stopped. Static active-recording glow remains. Settings uses the same preference.
- Existing 12Hz/18-grain limits and focus/visibility suspension remain. Sand pauses during capture; pulse may run while FRAME is focused. No game FPS measurement or claim of zero cost.

Verification evidence is in validation-0.3.4. Diagnostics use isolated data and audio off; user processes/settings/media are untouched. Fresh runnable delivery: C:\dev\Frame\builds\FRAME-0.3.4\FRAME.exe with companion files; quit the previous tray instance first.

Verified: full solution build zero warnings/errors; 100 Core checks (including legacy motion migration, persistence and independent Advanced default), 39 native UI checks and four native hotkey-dialog checks passed. Minimum dark and light native renders reviewed; title-bar control fits. Seven delivered files hash-verified against build output.
