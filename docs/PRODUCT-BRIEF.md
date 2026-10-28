# CODEX MASTER PROMPT — PROJECT FRAME
## Build an Exceptional, Premium, Ultra-Lightweight Windows Capture Application

You are acting as an elite software engineering team consisting of:

- Principal Windows Systems Engineer
- Senior C++ / C# Performance Engineer
- GPU Capture and Video Encoding Specialist
- Windows Desktop Application Architect
- World-Class UI/UX and Motion Designer
- Computer Vision Engineer
- Senior Quality Assurance and Test Automation Engineer

Your mission is to design, implement, optimize, and validate **FRAME**, a next-generation Windows 11 screen recording and capture application.

**Project directory:** `C:\dev\Frame`

This is not a prototype, concept demonstration, dashboard mockup, or ordinary coding exercise.

Build a genuine desktop application with commercially competitive functionality, exceptional engineering quality, and a striking visual identity.

Your quality benchmark is an application people would happily pay for.

Do not stop after producing an implementation plan. Execute the development process.

---

# 1. PRODUCT PHILOSOPHY

FRAME should combine the strengths of:

- NVIDIA ShadowPlay — efficient recording and Instant Replay
- OBS Studio — recording flexibility and reliability
- Windows Snipping Tool — immediate accessibility
- CleanShot X — refined capture interactions
- Screen Studio — exceptional visual polish

But FRAME should establish its own identity.

Our defining principles:

**EFFORTLESS. INSTANT. PRECISE. BEAUTIFUL. LIGHTWEIGHT.**

The interface should feel futuristic but remarkably easy to operate.

Advanced capabilities must be discoverable without overwhelming the primary interface.

Users should rarely need more than one or two interactions to perform common actions.

The application must remain responsive even when the system is under heavy GPU load.

Never prioritize visual decoration over recording reliability or system performance.

---

# 2. VISUAL IDENTITY — LUXURY CYBERPUNK

Create a breathtaking, modern, futuristic interface inspired by premium science-fiction technology.

Visual direction:

- Dark charcoal and obsidian surfaces
- Graphite and deep midnight-blue panels
- Controlled electric cyan lighting
- Restrained violet and magenta accents
- Beautifully subtle edge illumination
- Soft dimensional shadows
- Precise geometric composition
- Premium micro-interactions
- Refined glass and translucent surfaces
- Sophisticated, professional typography
- Elegant state transitions
- Exceptional layout spacing
- Custom, coherent iconography
- Thin, carefully engineered control surfaces

**The aesthetic should be closer to a sophisticated futuristic operating system than a stereotypical gaming application.**

Avoid:

- Excessive neon
- Unnecessary glowing borders
- Huge buttons
- Bloated dashboards
- Generic component-library layouts
- Sci-fi decorations without functionality
- Distracting transitions
- Fake performance indicators
- Excessive use of gradients

Design a coherent visual system with reusable tokens for spacing, typography, colors, surfaces, borders, animations, and interaction states.

Support dark mode as the primary theme and offer a polished alternative theme.

The design should be exceptional at 100–200% Windows display scaling.

### Important design requirement

Use actual image-generation capabilities or available image-design tooling to create original visual assets, where accessible.

Generate professionally designed application assets, branding concepts, and visual direction boards if the development environment supports image generation.

If image generation is unavailable, create excellent vector-based assets directly and document the limitation.

Do not pretend to have generated assets that do not exist.

The application's UI should primarily use native, vector, or GPU-rendered components rather than large raster graphics.

Visual beauty must not increase recording overhead.

---

# 3. APPLICATION ARCHITECTURE

Research the appropriate Windows capture technologies before making architectural decisions.

Prefer:

- Windows 11 native integration
- C# and modern .NET for application logic
- WinUI 3 / Windows App SDK when appropriate
- Native C++ components where performance justifies them
- Windows Graphics Capture for efficient capture
- Windows audio APIs for sound recording
- Hardware-assisted video encoding
- GPU-accelerated processing where beneficial

Consider a hybrid architecture if it materially improves recording performance.

Separate these subsystems:

1. UI and presentation
2. Capture session management
3. Monitor and window selection
4. GPU capture
5. Encoding and muxing
6. Audio capture and synchronization
7. Instant Replay engine
8. Screenshot engine
9. Smart Capture analysis
10. Hotkey management
11. Library and file management
12. Application configuration
13. Diagnostics and logging

The capture pipeline must never be tightly coupled to UI animation or rendering.

Use bounded queues, asynchronous processing, and efficient resource management.

Avoid unnecessary memory copies and GPU-to-CPU transfers.

Use zero-copy or GPU-resident frame processing where practical.

---

# 4. THE PRIMARY EXPERIENCE

The application should open into a remarkably clean, premium home interface.

The primary controls should be:

**RECORD — REPLAY — SCREENSHOT**

A compact monitor selection area should allow the user to choose exactly what is captured.

The main screen should also provide:

- Current recording status
- Selected displays
- Microphone and desktop audio controls
- Recording quality preset
- Instant Replay toggle
- Access to recent captures
- Minimal settings access

Do not prominently display rarely used advanced features.

Intelligent content detection belongs in an advanced capture submenu.

The main dashboard must feel effortless.

---

# 5. MULTI-MONITOR CAPTURE

Implement a beautifully designed monitor selection interface.

Requirements:

- Automatically detect all connected displays
- Show monitor names and arrangement
- Identify primary and secondary displays
- Allow selecting one monitor
- Allow selecting multiple monitors simultaneously
- Provide clear checkbox or selectable-tile interaction
- Clearly indicate which monitors will be captured
- Remember the user's selection
- Handle monitors connecting and disconnecting
- Support mixed resolutions and refresh rates
- Support mixed DPI and display scaling

For simultaneous multi-monitor recording, provide clearly defined recording modes:

**Independent:** Each selected display is recorded into a separate file.

**Combined:** Displays are composed into one video according to their physical layout.

Offer sensible resolution, scaling, and output configuration.

Maintain accurate synchronization between selected displays.

Prioritize Independent mode when combining displays would introduce unnecessary overhead.

Do not silently omit selected monitors.

---

# 6. RECORDING ENGINE

Build a robust, hardware-accelerated recording engine.

Features:

- Start recording instantly
- Stop recording instantly
- Pause and resume
- Capture full monitors
- Capture multiple monitors
- Capture selected application windows
- Capture manually selected regions
- Adjustable output resolution
- 30 / 60 / 120 FPS options where supported
- H.264 encoding
- HEVC encoding where available
- AV1 encoding where available
- Configurable bitrate and quality
- System audio recording
- Microphone recording
- Audio source selection
- Correct audio/video synchronization
- Configurable mouse cursor capture

Implement reliable timestamp handling.

Handle changes to monitor configurations, minimized windows, interrupted audio devices, storage errors, and application shutdowns.

Provide recoverable recording output when practical.

Use accurate file finalization and error reporting.

**Never mark a recording as successfully saved before the file is finalized and verified.**

---

# 7. EXTREME PERFORMANCE OPTIMIZATION

Performance is a foundational requirement.

The goal is to minimize the application's impact on game FPS, frame pacing, and desktop responsiveness.

The recording implementation must be built around efficient Windows capture APIs and GPU video encoding.

Support appropriate hardware encoding backends:

- NVIDIA NVENC
- AMD AMF
- Intel Quick Sync

Automatically detect supported capabilities.

Allow users to select an encoder when multiple options are available.

Implement an ultra-lightweight recording mode that disables unnecessary previews, overlays, effects, and analysis.

### Required engineering practices

- No inefficient full-screen CPU polling loops
- No unnecessary frame copies
- No unbounded memory growth
- No excessive GPU synchronization
- No continuous expensive UI rendering
- No unnecessary high-frequency timers
- No CPU-heavy image analysis during ordinary recording
- Avoid blocking the capture thread
- Keep frame acquisition independent from encoding and UI
- Use carefully controlled buffering
- Implement graceful backpressure handling

Create an internal diagnostics system that can measure:

- CPU usage
- RAM consumption
- GPU engine utilization where available
- Encoding latency
- Capture frame timing
- Dropped and duplicated frames
- Actual output FPS
- Audio/video drift
- Disk writing throughput

Measure baseline performance and compare it with active recording.

Do not invent performance results.

Identify bottlenecks and improve them.

### Acceptance goal

FRAME should produce the lowest reasonably achievable recording overhead on the current system while maintaining excellent output quality.

Prefer measurable improvements over speculative micro-optimizations.

---

# 8. GLOBAL HOTKEY SYSTEM

Implement system-wide customizable keyboard shortcuts.

Default shortcuts:

- `Ctrl + Shift + R` — Start/stop recording
- `Ctrl + Shift + P` — Pause/resume recording
- `Ctrl + Shift + S` — Save Instant Replay
- `Ctrl + Shift + C` — Screenshot selection
- `Ctrl + Shift + M` — Open monitor selection

Hotkeys must:

- Work while the application is minimized
- Work from the system tray
- Be configurable
- Detect conflicts
- Avoid repeated unintended activation
- Provide immediate feedback

Hotkey processing must remain responsive during GPU-intensive games.

Do not promise activation in environments where Windows or exclusive-input restrictions prevent it.

---

# 9. AUDIO FEEDBACK

Implement a minimal premium audio-feedback system.

When a user manually starts recording, play a distinctive but subtle sound.

Additional optional cues:

- Stop recording
- Pause
- Resume
- Save Instant Replay
- Screenshot saved

Sounds should feel clean, futuristic, and professional.

No exaggerated game sound effects.

Provide:

- Independent sound cue toggle
- Adjustable notification volume
- Optional visual confirmation
- Reliable playback with low latency

Ensure notification sounds are not unintentionally included in recordings when avoidable.

---

# 10. INSTANT REPLAY — SHADOWPLAY CLASS

Implement Instant Replay as a first-class feature.

This is one of the application's most important functions.

Users must be able to enable or disable continuous replay capture.

When enabled, maintain a rolling recording buffer.

Supported duration presets:

- 15 seconds
- 30 seconds
- 60 seconds
- 2 minutes
- 5 minutes
- 10 minutes
- Custom duration

When the user activates Save Replay, the app must save the preceding configured duration.

### Architecture

Use an efficient circular buffer of encoded video packets whenever practical.

Do not retain raw video frames unnecessarily.

Use bounded memory and/or temporary disk-backed segments according to a selectable memory budget and system constraints.

Ensure H.264/HEVC/AV1 keyframe boundaries are handled correctly.

Replay exports must start with valid decodable frames and maintain correct timestamps and audio synchronization.

Support separate audio streams or a configurable audio mix.

Replay mode must be usable without opening the main window.

Allow replay capture to start automatically with Windows if explicitly enabled.

### Important behavior

Ordinary recording and Instant Replay should coexist when supported, with shared capture resources where practical.

Prevent accidentally launching duplicate expensive capture pipelines.

Instant Replay must not continue invisibly without a clearly accessible enabled-state indicator.

Handle insufficient memory, insufficient storage, session changes, and device failures gracefully.

---

# 11. SCREENSHOT EXPERIENCE

Build a screenshot workflow that feels better than Windows Snipping Tool.

Support:

- Entire desktop
- Specific monitor
- Specific window
- Custom selection region
- Precision selection and resizing
- Magnified cursor crosshair
- Clipboard copy
- Automatic saving
- Delayed screenshot
- Optional cursor inclusion
- Screenshot history
- Fast annotation tools

The capture selection interface must feel incredibly fluid.

Opening it should feel immediate.

Support accurate positioning across mixed-DPI monitors.

---

# 12. ADVANCED SMART CAPTURE
## Intelligent image and video detection

This is an important premium feature, but **NOT the application's primary focus**.

It must be implemented as an advanced secondary capture capability.

Do not place a large Smart Capture panel on the main dashboard.

Access should be available through:

- Advanced Capture submenu
- A compact optional toolbar action
- A customizable hotkey
- Contextual capture options

It must remain invisible during everyday usage unless requested.

### Goal

Automatically identify the visual boundaries of content the user wants to capture.

Examples include:

- Images inside browsers
- Images inside desktop applications
- Video player surfaces
- Embedded web videos
- Media viewers
- Image galleries
- Windows and application content regions

The user should be able to hover the cursor over an image or video and receive a precise, elegant selection preview.

One click should select the detected region.

The user should then be able to capture a screenshot or begin video recording.

### Detection architecture

Do not rely on naive edge detection alone.

Implement a layered hybrid detection system.

**Layer 1 — Native window detection**

Use Windows window management APIs to identify application bounds and client areas.

**Layer 2 — UI Automation**

Inspect accessible element geometry and supported control properties.

Detect likely image and media elements when applications expose them.

**Layer 3 — Geometry and image analysis**

When accessibility data is insufficient, use efficient visual analysis.

Evaluate rectangular content boundaries, consistent edges, contrast transitions, and other useful indicators.

Avoid unnecessarily analyzing entire screens at full resolution.

**Layer 4 — Temporal motion detection**

Analyze changes within a candidate region to determine whether content is moving.

Use efficient downsampled frame comparisons, adaptive thresholds, and temporal smoothing.

**Layer 5 — Candidate scoring**

Assign confidence scores to candidate regions.

Prefer semantically identified elements over weak visual guesses.

Use geometry, cursor location, window hierarchy, and content behavior to rank candidates.

### Expected behavior

When the user activates Smart Capture:

1. Detect the element under the cursor.
2. Highlight the likely content boundaries.
3. Allow cycling between nested candidates.
4. Let the user select the preferred candidate.
5. Permit manual correction.
6. Remember corrected regions where useful.
7. Start the requested capture operation.

### Intelligent video detection

Video detection must be especially robust.

It should distinguish:

- Static images
- Animated images
- Actual moving video content
- Blinking interface elements
- Cursor movement
- Loading animations
- Other small transient changes

Offer automatic motion-triggered recording.

Default trigger:

**Sustained motion for at least 500 milliseconds.**

Make the trigger duration configurable.

Allow recordings to stop after a configurable inactivity period.

Implement hysteresis and sensitivity controls to avoid rapid start-stop oscillation.

The motion analysis must be computationally lightweight.

### Advanced handling

- Support browser zoom and DPI scaling
- Handle scrolling when selecting content
- Re-evaluate boundaries when content changes
- Follow the target window when appropriate
- Deal gracefully with overlapping windows
- Handle letterboxing and player controls when identifiable
- Support manual overrides
- Never repeatedly force a selection after the user corrects it

Do not assume every application exposes accessible image or video metadata.

Use confidence-based fallbacks and report detection limitations accurately.

Never attempt to bypass DRM or protected-content restrictions.

### Quality standard

Smart Capture should feel intelligent, predictable, and helpful.

A small number of accurate, reliable detection behaviors is better than a large number of fragile heuristics.

Build a repeatable test suite with representative desktop layouts, media players, browser pages, nested images, and difficult boundaries.

---

# 13. COMPACT FLOATING CONTROLS

Create an exceptionally polished floating recording toolbar.

It should be:

- Small
- Elegant
- Lightweight
- Draggable
- Collapsible
- Context-aware
- Optional
- Never visually intrusive

Default compact mode should show only essential controls.

Expand only when additional actions are requested.

Offer:

- Recording timer
- Pause/resume
- Stop
- Microphone toggle
- Audio toggle
- Replay save action

Use subtle animation.

Avoid excessive redraws.

Provide a setting to hide the toolbar entirely and control recording through hotkeys or the system tray.

Exclude the toolbar from captures where supported.

---

# 14. THOUGHTFUL MICRO-FEATURES

Implement carefully selected quality-of-life features.

Consider:

- Save a replay without opening the application
- Remember last monitor selection
- Remember preferred audio devices
- Automatically name files intelligently
- One-click open output folder
- Copy file path to clipboard
- Drag captures directly into other applications
- Optional delayed recording
- Optional recording countdown
- Optional mouse click visualizations
- Optional cursor highlight
- Quick screenshot while recording
- Disk-space warnings
- Automatic safe filename collision handling
- Compact save notifications
- Optional Do Not Disturb integration
- Low-disk-space protection
- Smart audio device reconnection
- Recording presets
- Privacy exclusions for selected windows where supported
- Optional GIF conversion for short clips
- Recent recordings accessible from the tray
- Temporary capture history with configurable retention

Do not add every feature merely because it is possible.

Prioritize the ones that measurably improve usability.

Keep rarely used options inside clearly structured settings.

---

# 15. CAPTURE LIBRARY

Create a beautifully designed capture library.

Include:

- Screenshot thumbnails
- Video thumbnails
- Recording duration
- Resolution
- File size
- Capture date
- Search
- Sorting
- Filtering
- Rename
- Delete
- Open
- Show in Explorer
- Copy to clipboard where supported
- Quick preview

The library must remain fast even with thousands of captures.

Use efficient thumbnail caching and incremental loading.

Never synchronously scan a large library on the UI thread.

---

# 16. FIRST-RUN EXPERIENCE

The first launch should be elegant and exceptionally simple.

Present:

1. Preferred capture directory
2. Default monitor selection
3. Preferred audio configuration
4. Important hotkeys
5. Optional Instant Replay activation

Do not force lengthy onboarding.

Allow sensible defaults.

Never automatically enable background capture without the user's knowledge.

---

# 17. ENGINEERING QUALITY AND RELIABILITY

The application must be engineered for production quality.

Implement:

- Structured diagnostics
- Graceful error handling
- Resource cleanup
- Capture session lifecycle management
- Reliable device discovery
- Correct disposal of graphics resources
- Recovery from unexpected failures
- Safe configuration persistence
- Proper file finalization
- Thread-safe state transitions
- Cancellation handling
- Correct shutdown behavior

No fake buttons.

No misleading status indicators.

No hidden placeholders.

No silently failing operations.

Avoid using the term "complete" for unimplemented or unverified functionality.

---

# 18. TESTING AND VERIFICATION

Testing must be part of development, not an afterthought.

Create automated tests for:

- Recording state transitions
- Hotkey command handling
- Monitor selection configuration
- Multi-monitor session management
- Replay buffer trimming
- Replay segment extraction
- File naming and collision prevention
- Settings persistence
- Smart Capture candidate ranking
- Motion-trigger sensitivity
- Motion-trigger cooldowns
- Failure recovery

Create integration tests where technically feasible.

Where a physical GPU, monitor, or audio device is required, clearly distinguish between automated validation and hardware-dependent testing.

Evaluate:

- Idle resource usage
- Recording overhead
- Replay overhead
- Memory behavior over long sessions
- Frame pacing
- Audio synchronization
- Mixed monitor configurations
- Extended recording sessions
- File integrity after stopping recording

Set measurable performance budgets and establish baselines before optimizing.

Do not claim that the application is flawless merely because it compiles.

Identify and correct reproducible defects.

Document any remaining known issues.

---

# 19. BUILD PRIORITIES

Follow this implementation order.

**Phase 1 — Foundation and visual system**

Establish the architecture, technology choices, application shell, design system, and working Windows build.

**Phase 2 — Functional capture**

Implement real screenshot capture, real video capture, monitor selection, and global hotkeys.

**Phase 3 — Performance and audio**

Implement hardware encoding, audio capture, synchronization, and recording diagnostics.

**Phase 4 — Instant Replay**

Implement encoded rolling buffers, replay export, configuration, and background operation.

**Phase 5 — Premium UI and workflow**

Complete the floating toolbar, dashboard, capture library, tray controls, settings, and polished interactions.

**Phase 6 — Advanced Smart Capture**

Implement the layered image/video detection engine, selection refinement, motion triggering, and related testing.

**Phase 7 — Hardening and optimization**

Conduct integration testing, profiling, resource optimization, failure recovery testing, UI refinement, and release packaging.

These phases define development order, not permission to stop after phase one.

---

# 20. CODEX EXECUTION DIRECTIVES

You have permission to make reasonable engineering and design decisions autonomously.

Work in:

`C:\dev\Frame`

Start by inspecting the local environment, existing files, installed SDKs, available build tools, and relevant hardware capabilities.

Do not overwrite unrelated files.

Create a concise implementation plan, then start writing the application.

Continue through the development phases, building and testing at meaningful milestones.

When a build fails, diagnose and fix the actual cause.

Prefer finished, validated functionality over ambitious but incomplete features.

Use official platform documentation and maintained dependencies where practical.

Avoid unnecessary third-party frameworks that introduce substantial overhead.

If a capability cannot be implemented reliably within the available environment, document the limitation and implement the best production-appropriate alternative.

At the end, provide:

- Architecture overview
- Project file structure
- Implemented features
- Validation and test results
- Performance measurements, if obtained
- Known limitations
- Instructions to run the application
- Instructions to build a distributable Windows executable
- Recommended next engineering improvements

**FINAL PRODUCT STANDARD**

FRAME should feel like a carefully engineered, premium 2026 Windows application.

It must be:

- Visually exceptional
- Immediately understandable
- Extremely responsive
- Lightweight
- Reliable
- Efficient during gaming
- Technically sophisticated
- Minimal in appearance
- Powerful beneath the surface

Its primary experience should be recording, replay, and screenshots.

Its advanced Smart Capture functionality should be unusually capable but discreet and unobtrusive.

Do not settle for a generic Windows utility.

Build something exceptional.

**Begin actual implementation now.**