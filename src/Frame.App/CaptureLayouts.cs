using System.Windows;
using System.Windows.Controls;

namespace Frame.App;

// 0.6.0 visual modes: the contract between MainWindow (application state, recording engine, settings) and the per-mode
// presentation. One engine and one state for every mode; a mode only chooses
//   - a palette (ModePalettes: the application-wide theme brushes used by every page),
//   - a Capture layout (ICaptureLayout): Original GUI keeps the 0.5.5 HomePage untouched (no ICaptureLayout object); every
//     other mode shows its own layout in MainWindow.CaptureHost and is fed by CaptureView snapshots,
//   - an optional scene (IModeScene) behind the whole window (Pink, Emo; the desert of Original/Cinematic D is the existing
//     CaptureAtmosphere layer set).
// Layouts never touch the engine or the settings file: they show CaptureView and call ICaptureCommands. Decorative work
// runs only from CaptureAtmosphere's clock (IDecorativeClient), which stops whenever FRAME is not on screen.

// Everything a Capture layout shows. Built by MainWindow.CaptureSnapshot() on every state change (and once a second while
// the window timer runs); compare before re-rendering if a change is expensive.
public sealed record CaptureView
{
    public bool Ready { get; init; }
    // Shadow Recording (the stone / main action)
    public string ShadowTitle { get; init; } = "Start shadow recording";   // Start / Starting… / Stop / Stopping recording…
    public bool ShadowActive { get; init; }       // engine confirmed Shadow Recording (pulse/light only when true)
    public string ShadowDot { get; init; } = "off"; // "off" | "transition" | "active"
    public bool ShadowEnabled { get; init; }
    public string ShadowTooltip { get; init; } = "";
    // Manual recording (kept understated in every new layout)
    public bool ManualActive { get; init; }       // recording or paused
    public bool ManualPaused { get; init; }
    public bool ManualEnabled { get; init; }
    public string ManualTitle { get; init; } = "Start manual recording";
    public string ManualTooltip { get; init; } = "";
    public string ManualElapsed { get; init; } = ""; // "hh:mm:ss" while a manual recording runs, else ""
    public string PauseLabel { get; init; } = "Pause";
    // Replay length (seconds); choices are the supported discrete values (HistoryDial), ascending
    public int ReplaySeconds { get; init; } = 300;
    public IReadOnlyList<int> ReplayChoices { get; init; } = [];
    public bool ReplayLengthEnabled { get; init; }
    public string ReplayLengthTooltip { get; init; } = "";
    public string Duration { get; init; } = "5 minutes"; // "15 minutes", "30 seconds", …
    // Save replay ("Save last 15 minutes")
    public string SaveTitle { get; init; } = "";
    public bool SaveEnabled { get; init; }
    public string SaveTooltip { get; init; } = "";
    // Global shortcuts as the user's keyboard layout shows them ("Alt+Z", "Not set")
    public string ShadowHotkey { get; init; } = "Not set";
    public string SaveHotkey { get; init; } = "Not set";
    public string SaveHotkeyLabel { get; init; } = "";  // "Hotkey for saving the 15 minutes"
    // Quality preset: 0 Balanced, 1 Original, 2 Lightweight, 3 Custom (see Settings); PresetNames has the four labels
    public int PresetIndex { get; init; }
    public bool PresetEnabled { get; init; }
    public string PresetTooltip { get; init; } = "";
    public static readonly string[] PresetNames = ["Balanced · 1080p / 60 FPS", "Original · Native / 60 FPS", "Lightweight · 720p / 30 FPS", "Custom · see Settings"];
    // Audio
    public bool DesktopAudio { get; init; }
    public bool Microphone { get; init; }
    public bool AudioEnabled { get; init; }
    // Capture source summary (the display cards themselves are the shared MonitorCanvas, see ICaptureLayout.DisplayHost)
    public string SourceTitle { get; init; } = "";   // "Display 1", "2 displays", …
    public string SourceDetail { get; init; } = "";  // "2560 × 1440", "Combined canvas", …
    public bool SourceLocked { get; init; }
    public string StatusText { get; init; } = "";    // "● Shadow active", "● Ready", …
}

// Actions a layout may trigger. All go through MainWindow's existing paths (Act gate, RequestShadowToggle, quality restart,
// hotkey editor), so behaviour is identical to the Original GUI controls.
public interface ICaptureCommands
{
    Task ToggleShadow();
    Task ToggleManual();
    Task PauseResume();
    Task SaveReplay();
    void SetReplaySeconds(int seconds);   // one of CaptureView.ReplayChoices; persisted and applied live as before
    void SetPreset(int index);            // 0..2 (3 = Custom is display-only)
    void SetAudio(bool desktop, bool microphone);
    void ChangeHotkey(string action);     // "Shadow" or "Replay": opens Settings › Hotkeys editing that field
    void ChooseCaptureFolder();
    void ViewCaptureFolder();
}

// A Capture page layout for one or more modes (not Original GUI, which is the 0.5.5 HomePage itself).
public interface ICaptureLayout
{
    FrameworkElement Root { get; }        // placed in MainWindow.CaptureHost (the whole content column, right of the sidebar)
    // Shared status elements are moved (re-parented) into these hosts while the layout is shown, and back afterwards:
    // NotesHost receives FailurePanel, EnginePanel, UnexpectedExitNote and ReplayInfo (in this order; ReplayInfo is a fixed
    // two-line slot so notes never move the page). DisplayHost receives MonitorCanvas (the clickable display cards).
    Panel NotesHost { get; }
    Decorator DisplayHost { get; }
    ContentControl? HeroHost { get; }     // optional slot for a scene's hero element (Emo mode quote)
    void Update(CaptureView view);
    void ShowLoading(string text, bool visible); // typed status under the Shadow title ("Preparing capture…")
    void Shown();                         // the layout became the visible Capture page (attach clock clients here)
    void Hidden();                        // another mode/page: stop everything the layout runs
}

// Receives CaptureAtmosphere's decorative clock. Advance is called only while FRAME is on screen and decorative motion is
// allowed; dt is clamped (≤ 0.1 s, no catch-up after a pause). Present() must show a correct still state (used whenever the
// clock is stopped). FullRate clients get every composition frame (~60/s) instead of ~30 updates/s: use it only for
// transform-based motion that would visibly judder (e.g. characters), never for per-frame OnRender redraws of many particles.
public interface IDecorativeClient
{
    bool WantsFrames { get; }
    bool FullRate { get; }
    void Advance(double dt);
    void Present();
}

// A mode's background world (Pink, Emo). Root is placed behind all UI (MainWindow.SceneHost, whole window, below the title
// bar and pages); it must not take mouse input except on small deliberate elements (secrets) away from FRAME's controls.
// Tools (optional) is shown at the top right under the mode buttons (Pink: background picker + effect icons). Hero
// (optional) is placed in the Capture layout's HeroHost (Emo: the quote). Enter/Leave are called on mode switches.
public interface IModeScene : IDecorativeClient
{
    FrameworkElement Root { get; }
    FrameworkElement? Tools { get; }
    FrameworkElement? Hero { get; }
    void Enter();
    void Leave();
    void PageChanged(bool capturePage); // Library/Settings: the scene stays, dimmed by MainWindow's scrim; may reduce work
}

// What a scene or layout may use from the host. Settings changes made here (Pink/Emo choices) are saved at once.
sealed class ModeContext(Window window, CaptureAtmosphere atmosphere, ICaptureCommands commands, Func<Settings> settings, Action<Action<Settings>> update)
{
    public Window Window { get; } = window;
    public CaptureAtmosphere Atmosphere { get; } = atmosphere;
    public ICaptureCommands Commands { get; } = commands;
    public Settings Settings => settings();
    public void Update(Action<Settings> change) => update(change);
    // Diagnostics (--render-animation, --mode-test): deterministic randomness for scenes when set.
    public int? Seed { get; init; }
    // Window-space rectangles of FRAME's visible UI on the current page (controls, text, panels, the stone): where a scene must
    // not put clickable secrets or let characters rest. Computed on demand (walks the visual tree); call on size/page changes.
    public Func<Rect[]> UiRects { get; init; } = () => [];
}
