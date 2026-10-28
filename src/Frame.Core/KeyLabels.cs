using System.Globalization;

namespace Frame.Core;

// Hotkey key labels (0.5.5). Hotkeys are stored and registered as Windows virtual-key codes (unchanged); only the label
// depends on the keyboard layout: Norwegian VK_OEM_6 types Å, VK_OEM_3 Ø, VK_OEM_7 Æ (the old label was the US-centric
// "Oem6"). The app reads the key's character from the active layout (MapVirtualKeyEx, no keyboard-state side effects);
// these pure rules name the keys that type no character and turn a layout character into a label. Unit-tested.
public static class KeyLabels
{
    // Fixed English names (the UI is English). Letters and top-row digits keep the label of the key position Windows uses for
    // shortcuts (French AZERTY types & on the 1 key unshifted; the key is still "1"), numpad, F-keys and Scroll Lock keep
    // their established names. Null: a layout-dependent key (punctuation, OEM keys), read from the layout instead.
    public static string? Named(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),
        0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6E => "Num .", 0x6F => "Num /",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x13 => "Pause", 0x14 => "Caps Lock", 0x1B => "Esc", 0x20 => "Space",
        0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
        0x2C => "Print Screen", 0x2D => "Insert", 0x2E => "Delete", 0x5D => "Menu", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
        0xAD => "Volume Mute", 0xAE => "Volume Down", 0xAF => "Volume Up", 0xB0 => "Next Track", 0xB1 => "Previous Track", 0xB2 => "Stop Media", 0xB3 => "Play/Pause",
        _ => null
    };
    // MapVirtualKeyEx(vk, MAPVK_VK_TO_CHAR, layout) result → label: low word = the unshifted character, bit 31 = dead key
    // (¨ ´ ^ ~ `: the spacing accent itself is the label). Upper case in the layout's culture (å → Å, Turkish i → İ). Null for
    // no character, control characters, white space and combining marks (the caller falls back to the Windows key name).
    public static string? FromLayoutChar(uint mapped, CultureInfo culture)
    {
        char c = (char)(mapped & 0xFFFF);
        if (c == 0 || char.IsControl(c) || char.IsWhiteSpace(c) || char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) return null;
        return char.ToUpper(c, culture).ToString();
    }
    public static bool IsDeadKey(uint mapped) => (mapped & 0x80000000) != 0;
    // Culture of a keyboard layout handle (HKL): its low word is the input language id (0x0414 = Norwegian Bokmål).
    public static CultureInfo LayoutCulture(long layout, CultureInfo fallback)
    {
        int language = (int)(layout & 0xFFFF);
        try { return language == 0 ? fallback : CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { return fallback; }
    }
}
