namespace Frame.Core;

// Visual modes: five looks over the same application state and recording engine. Only the appearance changes; a mode
// switch never touches capture settings, the replay buffer, hotkeys or audio. Stored as a stable key in Settings.VisualMode;
// a missing/unknown key (first run, files from 0.5.5 and older) means Original GUI.
// 0.6.1: "Original GUI" is the former Cinematic D design (the default); the 0.5.5 design is no longer a mode. Saved keys of
// both former modes ("cinematic-d" and "original") load as Original GUI.
public enum VisualMode { Original, Dark, Pink, Light, Emo }

public static class VisualModes
{
    // Title-bar order: all five names are shown at once and each one activates its mode.
    public static readonly VisualMode[] All = [VisualMode.Original, VisualMode.Dark, VisualMode.Pink, VisualMode.Light, VisualMode.Emo];
    public const VisualMode Default = VisualMode.Original;
    public static string Name(VisualMode mode) => mode switch
    {
        VisualMode.Original => "Original GUI", VisualMode.Dark => "Dark / No Bullshit",
        VisualMode.Pink => "Pink mode", VisualMode.Light => "Light mode", _ => "Emo mode"
    };
    public static string Key(VisualMode mode) => mode switch
    {
        VisualMode.Original => "original", VisualMode.Dark => "dark",
        VisualMode.Pink => "pink", VisualMode.Light => "light", _ => "emo"
    };
    const string FormerCinematicD = "cinematic-d"; // 0.6.0 key of the design that is Original GUI now
    public static VisualMode Parse(string? key) => string.Equals(key?.Trim(), FormerCinematicD, StringComparison.OrdinalIgnoreCase) ? VisualMode.Original
        : All.FirstOrDefault(m => string.Equals(Key(m), key?.Trim(), StringComparison.OrdinalIgnoreCase), Default);
    public static bool IsKnown(string? key) => string.Equals(key?.Trim(), FormerCinematicD, StringComparison.OrdinalIgnoreCase) || All.Any(m => string.Equals(Key(m), key?.Trim(), StringComparison.OrdinalIgnoreCase));
    // Decorative motion: Dark / No Bullshit and Light have none at all (no clock subscription, no scene).
    public static bool Decorative(VisualMode mode) => mode is not (VisualMode.Dark or VisualMode.Light);
}

// Pink mode: exactly one background and any combination of effects (also none or all). First entry: Sakura garden with
// Sakura petals only; afterwards the saved combination is restored as it was left.
public static class PinkOptions
{
    public const string SimplyPink = "simply-pink";
    public static readonly string[] Backgrounds = ["sakura-garden", "pink-1", "pink-2", "pink-3", "pink-4", SimplyPink];
    public static readonly string[] Effects = ["hearts", "flowers", "sakura", "sparkles", "butterflies", "ribbons", "dogs"];
    public const string FirstBackground = "sakura-garden";
    public static readonly string[] FirstEffects = ["sakura"];
    public static string Background(string? saved) => Backgrounds.Contains(saved) ? saved! : FirstBackground;
    // null = never entered (first-entry default); an empty array is a deliberate "all effects off".
    public static string[] ActiveEffects(string[]? saved) => saved == null ? [.. FirstEffects] : Effects.Where(saved.Contains).ToArray();
    public static string[] Toggle(string[] active, string effect) =>
        !Effects.Contains(effect) ? active : active.Contains(effect) ? active.Where(e => e != effect).ToArray() : Effects.Where(e => e == effect || active.Contains(e)).ToArray();
}

// Emo mode: a small curated set. First entry: Dead rose garden with dark rose petals.
public static class EmoOptions
{
    public static readonly string[] Backgrounds = ["simply-emo", "dead-rose-garden", "rainy-neon-alley", "moonlit-graveyard"];
    public static readonly string[] Effects = ["petals", "rain", "bats", "fog"];
    public const string FirstBackground = "dead-rose-garden";
    public static readonly string[] FirstEffects = ["petals"];
    public static string Background(string? saved) => Backgrounds.Contains(saved) ? saved! : FirstBackground;
    public static string[] ActiveEffects(string[]? saved) => saved == null ? [.. FirstEffects] : Effects.Where(saved.Contains).ToArray();
    public static string[] Toggle(string[] active, string effect) =>
        !Effects.Contains(effect) ? active : active.Contains(effect) ? active.Where(e => e != effect).ToArray() : Effects.Where(e => e == effect || active.Contains(e)).ToArray();
}

// Emo mode quotes (exact text, user spec). Shuffle bag: every quote is shown once per round, in a random order, and a new
// round never starts with the quote that ended the previous one (no immediate repetition). The bag and the last index are
// persisted, so each entry into Emo mode shows a different quote, also across restarts.
public static class EmoQuotes
{
    public static readonly string[] All =
    [
        "\"My thoughts are too loud.\"",
        "\"Behind my smile is everything you'll never understand.\"",
        "\"I'm drowning in my own mind.\"",
        "\"Broken crayons still color.\"",
        "\"Smiling has always been easier than explaining.\"",
        "\"I am a ghost in my own life.\"",
        "\"The sun sets, and so do I.\"",
        "\"Silently screaming inside.\"",
        "\"I am losing myself again.\"",
        "\"Shadows are my only company.\"",
    ];
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    public static string Longest => All.MaxBy(q => q.Length)!;
    // Returns the next quote index and the remaining bag. bag: indices not yet shown in this round (null/empty = new round).
    public static (int Index, int[] Bag) Next(int last, int[]? bag, Random random)
    {
        var remaining = (bag ?? []).Where(i => i >= 0 && i < All.Length && i != last).Distinct().ToList();
        if (remaining.Count == 0)
        {
            remaining = Enumerable.Range(0, All.Length).ToList();
            for (int i = remaining.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (remaining[i], remaining[j]) = (remaining[j], remaining[i]); }
            if (remaining[0] == last) (remaining[0], remaining[^1]) = (remaining[^1], remaining[0]);
        }
        return (remaining[0], remaining.Skip(1).ToArray());
    }
}
