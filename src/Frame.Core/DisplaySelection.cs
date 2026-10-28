namespace Frame.Core;

// None: the saved selection is valid (kept exactly, also a deliberate secondary or multi-display choice). Initial: nothing
// was saved (first launch) → Windows' primary display. Partial: some saved displays are gone → the remaining ones.
// Primary: none of the saved displays is connected → Windows' primary display. NoDisplays: nothing to choose from; the
// saved selection is kept untouched (an empty enumeration is often transient while Windows changes the configuration).
public enum DisplayRecovery { None, Initial, Partial, Primary, NoDisplays }
public sealed record DisplayChoice(string[] Selected, DisplayRecovery Recovery, string[] Missing)
{
    public bool Changed => Recovery is DisplayRecovery.Initial or DisplayRecovery.Partial or DisplayRecovery.Primary;
}

// Which displays FRAME captures when the saved selection no longer matches the connected displays (0.5.5): a TV turned off
// in the GPU control panel, an unplugged monitor, Windows renumbering its devices. Selection is by device id, never by list
// position; the primary is Windows' own (MONITORINFOF_PRIMARY, passed in by the app), so "Display 1" is never assumed.
// Pure logic, unit-tested; the app keeps preferences separate from recovery and applies changes only while capture is idle.
public static class DisplaySelection
{
    // Older files did not distinguish the automatic primary choice from a clicked card. Keep secondary/multi-display
    // choices; an empty selection or the current primary alone becomes the primary policy on the first enumeration.
    public static bool FollowsPrimary(IReadOnlyList<CaptureTarget> displays, Settings settings, string? primaryId) =>
        settings.FollowPrimaryDisplay ?? (settings.SelectedDisplays.Length == 0
            || settings.SelectedDisplays.Length == 1 && settings.SelectedDisplays[0] == Primary(displays, primaryId)?.Id);

    public static DisplayChoice Resolve(IReadOnlyList<CaptureTarget> displays, Settings settings, string? primaryId) =>
        Resolve(displays, FollowsPrimary(displays, settings, primaryId) ? Array.Empty<string>() : settings.SelectedDisplays, primaryId);

    // Windows' primary display: the one the app identified as primary; otherwise the one at the desktop origin (Windows keeps
    // the primary's top-left corner at 0,0, whatever the other displays' negative or positive coordinates); otherwise the
    // first listed (last resort, only if Windows reported no primary at all). Null when there is no display.
    public static CaptureTarget? Primary(IReadOnlyList<CaptureTarget> displays, string? primaryId) =>
        displays.FirstOrDefault(d => primaryId != null && d.Id == primaryId) ?? displays.FirstOrDefault(d => d.Bounds.X == 0 && d.Bounds.Y == 0) ?? displays.FirstOrDefault();

    public static DisplayChoice Resolve(IReadOnlyList<CaptureTarget> displays, IEnumerable<string> saved, string? primaryId)
    {
        var wanted = saved.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        if (displays.Count == 0) return new(wanted, DisplayRecovery.NoDisplays, []);
        var connected = displays.Select(d => d.Id).ToHashSet();
        var valid = wanted.Where(connected.Contains).ToArray();
        var missing = wanted.Where(id => !connected.Contains(id)).ToArray();
        if (wanted.Length == 0) return new([Primary(displays, primaryId)!.Id], DisplayRecovery.Initial, []);
        if (missing.Length == 0) return new(wanted, DisplayRecovery.None, []);
        if (valid.Length > 0) return new(valid, DisplayRecovery.Partial, missing);
        return new([Primary(displays, primaryId)!.Id], DisplayRecovery.Primary, missing);
    }
}
