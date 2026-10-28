using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Frame.App;

// Display selection recovery (0.5.5; rules in Frame.Core DisplaySelection, unit-tested). The saved selection can stop matching
// the connected displays: a TV switched off in the GPU control panel, an unplugged monitor, Windows renumbering its devices.
// Before 0.5.5 the Capture page then showed "0 displays" and every start (also the automatic start) failed with "A selected
// display is disconnected". Now FRAME keeps the selected displays that remain, or selects Windows' primary display (the
// MONITORINFOF_PRIMARY / MonitorFromPoint(0,0) display, never simply the first listed), and says so once (status line + log,
// a short hint under Capture source; no dialog). 0.6.3: primary is a saved policy, not a frozen device id. Recovery only
// changes the effective selection: deliberate choices survive unavailable displays and restarts. A running capture is never
// touched: the change waits until capture is idle (DrawMonitors when the capture lock lifts) or the next start (Targets()).
public partial class MainWindow
{
    bool displayRecoveryPending;
    string displayHint = "", lastDisplayNote = "";
    string? startupDisplayNote;
    DispatcherTimer? displayChange;
    // The selection may change only while capture is idle and no action runs; `starting`: the caller is that start (Targets()).
    bool SelectionLocked(bool starting) => engine.Busy || (!starting && actions.CurrentCount == 0);
    // WM_DISPLAYCHANGE arrives several times while Windows switches a configuration; re-enumerate once it has settled (1 s, one-shot).
    void ScheduleDisplayRefresh()
    {
        if (quitting) return;
        if (displayChange == null)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => { timer.Stop(); if (!quitting) RefreshDisplays(); };
            displayChange = timer;
        }
        displayChange.Stop(); displayChange.Start();
    }
    // Windows' primary display among the enumerated ones: same monitor handle as MonitorFromPoint(0,0, PRIMARY), else the display
    // Native.Displays() flagged with MONITORINFOF_PRIMARY (fake diagnostic displays carry the flag only in their name).
    string? PrimaryDisplayId()
    {
        nint primary = displays.Any(d => d.MonitorHandle != 0) ? Native.PrimaryMonitor() : 0;
        return (displays.FirstOrDefault(d => primary != 0 && d.MonitorHandle == primary) ?? displays.FirstOrDefault(d => d.Name.EndsWith("· Primary", StringComparison.Ordinal)))?.Id;
    }
    static string DisplayTitle(CaptureTarget d) => d.Name.Split('·')[0].Trim();
    static string JoinNames(IEnumerable<string> names) { var a = names.ToArray(); return a.Length <= 1 ? string.Concat(a) : string.Join(", ", a[..^1]) + " and " + a[^1]; }
    void RecoverDisplaySelection(bool starting = false)
    {
        if (testSource.Length > 0 || displays.Length == 0) return; // diagnostics source, or nothing to choose from (often transient): saved selection kept
        if (SelectionLocked(starting)) { displayRecoveryPending = true; return; }
        displayRecoveryPending = false;
        if (settings.FollowPrimaryDisplay == null)
        {
            settings.FollowPrimaryDisplay = DisplaySelection.FollowsPrimary(displays, settings, PrimaryDisplayId());
            if (settings.FollowPrimaryDisplay == true) settings.SelectedDisplays = [];
            SaveSettings();
        }
        var choice = DisplaySelection.Resolve(displays, settings, PrimaryDisplayId());
        bool restored = displayHint.Length > 0 && choice.Recovery == DisplayRecovery.None;
        if (selected.SetEquals(choice.Selected) && !restored && (displayHint.Length > 0 || !choice.Changed)) return;
        bool changed = !selected.SetEquals(choice.Selected);
        selected.Clear(); selected.UnionWith(choice.Selected);
        string names = JoinNames(displays.Where(d => selected.Contains(d.Id)).Select(DisplayTitle));
        if (changed || restored) LogDisplaySelection($"{choice.Recovery}: not connected [{string.Join(", ", choice.Missing)}] → selected [{string.Join(", ", choice.Selected)}]");
        if (choice.Recovery is DisplayRecovery.Initial or DisplayRecovery.None)
        {
            displayHint = "";
            if (restored) ShowDisplayNote($"{names} connected again; FRAME captures the previous selection.");
            return;
        }
        int n = choice.Missing.Length;
        if (choice.Recovery == DisplayRecovery.Primary)
        {
            displayHint = "previous display not connected, using the primary display";
            ShowDisplayNote($"{(n == 1 ? "The selected display is" : "The selected displays are")} not connected. FRAME now captures {names} (primary).");
        }
        else
        {
            displayHint = (n == 1 ? "one selected display" : $"{n} selected displays") + " not connected";
            ShowDisplayNote($"{(n == 1 ? "One selected display is" : $"{n} selected displays are")} not connected. FRAME keeps capturing {names}.");
        }
    }
    void LogDisplaySelection(string what)
    {
        string? primary = PrimaryDisplayId();
        DiagnosticLog.Write("display-selection", what + " · connected: " + string.Join("; ", displays.Select(d => $"{d.Id} {d.Bounds.Width}x{d.Bounds.Height} at {d.Bounds.X},{d.Bounds.Y}{(d.Id == primary ? " (primary)" : "")}")));
    }
    // Once per change (no repeats while nothing changes). At start-up the engine check and the automatic start overwrite the status
    // line within a second, so the note is shown again after them (ShowStartupDisplayNote, OnLoaded).
    void ShowDisplayNote(string note)
    {
        if (note == lastDisplayNote) return;
        lastDisplayNote = note; Notify(note);
        if (!ready || checkingEngine) startupDisplayNote = note;
    }
    void ShowStartupDisplayNote() { if (startupDisplayNote is { } note) { startupDisplayNote = null; Notify((Message.Text + " " + note).Trim()); } }
    // A deliberate card click replaces any automatic choice (no restore later, hint and note reset).
    void ForgetDisplayRecovery() { displayHint = ""; lastDisplayNote = ""; }

    // --render --display-ui-test (part of ValidateDisplayCards; needs --fake-displays with 3 displays, Windows' primary not listed
    // first): simulates displays disappearing and returning by replacing the fake enumeration; nothing touches Windows.
    async Task ValidateDisplayRecovery(Action<string, bool> check, Func<string[]> saved)
    {
        var all = displays; int p = Array.FindIndex(all, d => d.Name.EndsWith("· Primary", StringComparison.Ordinal));
        if (all.Length < 3 || p < 1) { check("Display recovery checks need --fake-displays with 3 displays and the primary not listed first", false); return; }
        var primary = all[p]; var others = all.Where((_, i) => i != p).ToArray();
        void Use(params CaptureTarget[] set) { fakeDisplays = set; RefreshDisplays(); }
        void Choose(params CaptureTarget[] set) { ForgetDisplayRecovery(); selected.Clear(); foreach (var d in set) selected.Add(d.Id); settings.FollowPrimaryDisplay = false; settings.SelectedDisplays = selected.ToArray(); SaveSettings(); }
        string Header() => SourceExpander.Header as string ?? "";
        bool Saved(params CaptureTarget[] set) => saved().Order().SequenceEqual(set.Select(d => d.Id).Order());
        bool Preferred(params CaptureTarget[] set) => SettingsStore.Load(configPath).SelectedDisplays.Order().SequenceEqual(set.Select(d => d.Id).Order());
        bool Checked(params CaptureTarget[] set) => MonitorCanvas.Children.OfType<ToggleButton>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).Order().SequenceEqual(set.Select(d => d.Id).Order());
        bool Selected(params CaptureTarget[] set) => selected.SetEquals(set.Select(d => d.Id));

        Choose(others[0]); Use(all); Message.Text = "";
        Use(others[1], primary); // the selected display is gone; Windows' primary is listed second
        check($"Selected display disconnected: primary fallback, original preference kept, header and card, noted [{Header()} | {Message.Text}]", Selected(primary) && Saved(primary) && Preferred(others[0]) && Checked(primary) && Header() == primary.Name && Message.Text.Contains("not connected") && Message.Text.Contains(DisplayTitle(primary)) && SourceLabel.Text.Contains("primary display"));
        Message.Text = ""; Use(others[1], primary);
        check("Refreshing again keeps that selection without repeating the note", Selected(primary) && Message.Text.Length == 0);
        Use(all);
        check($"The display returns in the same session: the original selection comes back and is persisted [{Message.Text}]", Selected(others[0]) && Saved(others[0]) && Checked(others[0]) && Message.Text.Contains("connected again") && !SourceLabel.Text.Contains("not connected"));

        Choose(others[0], others[1]); Use(all); Message.Text = "";
        Use(primary, others[1]); // one of two selected displays is gone
        check($"One of two selected displays disconnected: remaining one used, both preferences kept, noted [{Header()} | {Message.Text}]", Selected(others[1]) && Saved(others[1]) && Preferred(others[0], others[1]) && Header() == others[1].Name && Message.Text.Contains("One selected display"));
        Use(all);
        check("Both displays connected again: the two-display selection returns", Selected(others[0], others[1]) && Saved(others[0], others[1]) && Header() == "2 displays");

        await actions.WaitAsync(); // an action (or a running capture) holds the selection
        Use(primary);
        bool deferred = Selected(others[0], others[1]) && Preferred(others[0], others[1]) && displayRecoveryPending && !Header().Contains("0 displays");
        actions.Release(); UpdateState(); // the capture lock lifts: DrawMonitors applies the deferred recovery
        check($"While an action runs the selection is left alone (no '0 displays'); applied once idle [{Header()}]", deferred && Selected(primary) && Saved(primary) && !displayRecoveryPending);

        Use();
        check("No displays enumerated: saved preference kept, 'No displays detected', no cards", Selected(primary) && Preferred(others[0], others[1]) && SourceLabel.Text == "No displays detected" && MonitorCanvas.Children.Count == 0 && Header() == "Capture source");

        fakeDisplays = all; Choose(); selected.Add(@"\\.\DISPLAY-GONE"); settings.SelectedDisplays = [@"\\.\DISPLAY-GONE"]; SaveSettings();
        var targets = Targets(); // a start (also the automatic start): fresh enumeration + recovery
        check("A start whose saved display is gone (auto-start case) uses primary without overwriting the preference", targets.Length == 1 && targets[0].Id == primary.Id && Saved(primary) && SettingsStore.Load(configPath).SelectedDisplays.SequenceEqual([@"\\.\DISPLAY-GONE"]));

        Choose(others[1]); Use(all); Message.Text = "";
        Use(others[1], primary, others[0]); // Windows lists the displays in another order
        check("A connected secondary selection is kept when the enumeration order changes (no note)", Selected(others[1]) && Saved(others[1]) && Message.Text.Length == 0);

        // Simulated process restart while a preferred display is temporarily unavailable. Recovery must not become the preference.
        Use(primary); settings = SettingsStore.Load(configPath); selected.Clear(); ForgetDisplayRecovery(); Use(primary);
        check("Restart during recovery keeps the absent secondary preference on disk and uses primary", Selected(primary) && Preferred(others[1]));
        Use(all);
        check("Preferred secondary returns after a restart and is selected again", Selected(others[1]) && Checked(others[1]));

        Choose(others[0]); Use(all); ToggleDisplay(primary.Id); ToggleDisplay(others[0].Id);
        check("Selecting only primary saves a primary policy, with no frozen device id", Selected(primary) && SettingsStore.Load(configPath) is { FollowPrimaryDisplay: true, SelectedDisplays.Length: 0 });
        var newPrimary = others[1] with { Name = others[1].Name + " · Primary" };
        var oldPrimary = primary with { Name = DisplayTitle(primary) };
        Use(oldPrimary, others[0], newPrimary);
        check("Primary policy follows a changed Windows primary even while the previous display stays connected", Selected(newPrimary) && Checked(newPrimary));
        settings = SettingsStore.Load(configPath); selected.Clear(); ForgetDisplayRecovery();
        Use(oldPrimary, others[0], newPrimary); targets = Targets();
        check("Reload and auto-start target resolution follow current primary", Selected(newPrimary) && targets.Length == 1 && targets[0].Id == newPrimary.Id && SettingsStore.Load(configPath).FollowPrimaryDisplay == true);

        // A deliberate change after fallback replaces the old preference permanently.
        Use(all); Choose(others[0]); Use(primary, others[1]); ToggleDisplay(others[1].Id); ToggleDisplay(primary.Id); Use(all);
        check("An explicit selection after fallback replaces the absent preference", Selected(others[1]) && Preferred(others[1]) && SettingsStore.Load(configPath).FollowPrimaryDisplay == false);

        Choose(); settings.FollowPrimaryDisplay = null; SaveSettings(); Use();
        check("Empty startup enumeration leaves primary migration undecided", SettingsStore.Load(configPath).FollowPrimaryDisplay == null);
        Use(all);
        check("First available displays choose primary and persist the primary policy", Selected(primary) && SettingsStore.Load(configPath) is { FollowPrimaryDisplay: true, SelectedDisplays.Length: 0 });

        Choose(all[0]); Use(all); Message.Text = "";
    }
}
