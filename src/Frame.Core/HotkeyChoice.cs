namespace Frame.Core;

// Which shortcuts FRAME may register globally (RegisterHotKey consumes the key system-wide, also in games and text fields).
// Single keys are allowed only where they do not take a normal key away: F1–F11, F13–F24, Pause, Scroll Lock.
public static class HotkeyChoice
{
    static bool IsModifierKey(uint k) => k is 16 or 17 or 18 or 91 or 92 or (>= 160 and <= 165);
    // Letters, digits, space, Enter, Tab, Backspace, navigation/editing keys, numpad and punctuation.
    public static bool IsTypingKey(uint k) => k is 8 or 9 or 13 or 32 or (>= 33 and <= 46) or (>= 48 and <= 57) or (>= 65 and <= 90) or (>= 96 and <= 111) or (>= 186 and <= 192) or (>= 219 and <= 223) or 226;
    public static bool SafeAlone(uint k) => k is (>= 112 and <= 135) and not 123 || k is 19 or 145;
    // Combinations Windows or every app relies on (Alt+F4, Alt+Tab, Alt+Esc, Alt+Space, Ctrl+Esc, Ctrl+Shift+Esc, Ctrl+Alt+Del).
    static readonly (uint Modifiers, uint Key)[] Reserved = [(1, 115), (1, 9), (1, 27), (1, 32), (2, 27), (6, 27), (3, 46)];

    public static Hotkey Create(string action, uint modifiers, uint key, IEnumerable<Hotkey> others)
    {
        if (key == 27) throw new ArgumentException("Esc cancels. Choose another key.");
        if (key is 0 or > 254 || IsModifierKey(key)) throw new ArgumentException("Press a key.");
        if (modifiers > 15) throw new ArgumentException("Unsupported modifier.");
        if (modifiers == 0 && key == 123) throw new ArgumentException("F12 is reserved by Windows. Choose another key.");
        if (modifiers == 0 && !SafeAlone(key)) throw new ArgumentException("On its own this key would stop working in games and typing. Add Ctrl, Alt or Shift, or use an F key.");
        if (modifiers == 4 && IsTypingKey(key)) throw new ArgumentException("Shift + this key is needed for typing. Add Ctrl or Alt.");
        if (Reserved.Contains((modifiers, key))) throw new ArgumentException("Reserved by Windows. Choose another combination.");
        if (others.Any(h => h.Modifiers == modifiers && h.Key == key)) throw new ArgumentException("Already used by another FRAME action.");
        return new(action, modifiers, key);
    }
}
