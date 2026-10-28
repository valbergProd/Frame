using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;

namespace Frame.App;

// Hotkey labels in the active keyboard layout (0.5.5). Before, Alt+Å on a Norwegian keyboard read "Alt+Oem6" (WPF's US-centric
// key name). Stored hotkeys and their Windows registration stay virtual-key codes; only the text changes. FormatHotkey is the
// single entry point (Capture shortcut rows, Settings › Hotkeys, notes, diagnostics export).
public partial class MainWindow
{
    static string FormatHotkey(Hotkey key, nint layout = 0) => key.Key == 0 ? "Not set" : FormatModifiers(key.Modifiers) + HotkeyNames.Label(key.Key, layout);

    // --render <png> --hotkey-names-test <report.json>: the labels with the installed layouts (read-only: GetKeyboardLayoutList +
    // MapVirtualKeyEx with an explicit layout handle; no layout is loaded, activated or added). Norwegian checks only when a
    // Norwegian layout (language 0x0414 / 0x0814) is installed; otherwise the report says so.
    async Task ValidateHotkeyNames(string path)
    {
        var results = new List<object>();
        void Check(string name, bool ok, object? detail = null) => results.Add(new { name, passed = ok, detail });
        try
        {
            var layouts = HotkeyNames.InstalledLayouts();
            var described = layouts.Select(l => new { layout = ((long)l).ToString("X8"), culture = KeyLabels.LayoutCulture(l, CultureInfo.InvariantCulture).Name, oem3 = HotkeyNames.Label(0xC0, l), oem6 = HotkeyNames.Label(0xDD, l), oem7 = HotkeyNames.Label(0xDE, l), oem1 = HotkeyNames.Label(0xBA, l), oemPlus = HotkeyNames.Label(0xBB, l), oem102 = HotkeyNames.Label(0xE2, l) }).ToArray();
            Check("Installed layouts read without changing them", layouts.Length > 0 && HotkeyNames.InstalledLayouts().Length == layouts.Length, new { active = ((long)HotkeyNames.ActiveLayout).ToString("X8"), layouts = described });
            if (layouts.FirstOrDefault(l => ((long)l & 0xFFFF) is 0x0414 or 0x0814) is var nb and not 0)
            {
                Check("Norwegian layout: VK_OEM_6 → Å, VK_OEM_3 → Ø, VK_OEM_7 → Æ", HotkeyNames.Label(0xDD, nb) == "Å" && HotkeyNames.Label(0xC0, nb) == "Ø" && HotkeyNames.Label(0xDE, nb) == "Æ", new { oem6 = HotkeyNames.Label(0xDD, nb), oem3 = HotkeyNames.Label(0xC0, nb), oem7 = HotkeyNames.Label(0xDE, nb) });
                Check("Norwegian layout: Alt+Å, Ctrl+Shift+Ø, Alt+Æ formatted with modifiers", FormatHotkey(new Hotkey("Replay", 1, 0xDD), nb) == "Alt+Å" && FormatHotkey(new Hotkey("Record", 6, 0xC0), nb) == "Ctrl+Shift+Ø" && FormatHotkey(new Hotkey("Pause", 1, 0xDE), nb) == "Alt+Æ");
                Check("Norwegian dead key (¨ on VK_OEM_1) shows its accent, not 'Oem1'", HotkeyNames.Label(0xBA, nb) is { Length: 1 } accent && !accent.Contains("Oem"), HotkeyNames.Label(0xBA, nb));
            }
            else Check("Norwegian layout not installed: Norwegian checks skipped (no layout is added by the test)", true, described.Select(d => d.culture));
            if (layouts.FirstOrDefault(l => ((long)l & 0xFFFF) == 0x0409) is var us and not 0)
                Check("US layout: VK_OEM_6 → ], VK_OEM_3 → `, VK_OEM_7 → '", HotkeyNames.Label(0xDD, us) == "]" && HotkeyNames.Label(0xC0, us) == "`" && HotkeyNames.Label(0xDE, us) == "'");
            Check("Every installed layout labels OEM keys without WPF 'Oem' names", described.All(d => !$"{d.oem3}{d.oem6}{d.oem7}{d.oemPlus}".Contains("Oem")), described);
            Check("Established names kept: digits, letters, numpad, F-keys, Scroll Lock, Pause; Not set", HotkeyNames.Label(0x31) == "1" && HotkeyNames.Label(0x5A) == "Z" && HotkeyNames.Label(0x63) == "Num 3" && HotkeyNames.Label(0x78) == "F9" && HotkeyNames.Label(0x91) == "Scroll Lock" && HotkeyNames.Label(0x13) == "Pause" && FormatHotkey(new Hotkey("Shadow", 0, 0)) == "Not set" && FormatHotkey(new Hotkey("Open", 3, 0x46)) == "Ctrl+Alt+F" && FormatHotkey(new Hotkey("Shadow", 3, 0x78)) == "Ctrl+Alt+F9");
            var stored = SettingsStore.Load(configPath).Hotkeys; UpdateState();
            Check("Labels change nothing stored or registered", JsonSerializer.Serialize(stored) == JsonSerializer.Serialize(settings.Hotkeys), stored.Select(h => h.Action + " " + FormatHotkey(h)));
        }
        catch (Exception e) { Check("Exception: " + e.Message, false); }
        finally { await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); }
    }
}

// Reads a key's character from a keyboard layout without side effects: MapVirtualKeyEx(MAPVK_VK_TO_CHAR) does not touch the
// keyboard state or the dead-key buffer (ToUnicodeEx would, unless asked not to). Rules for the text: Frame.Core KeyLabels.
static class HotkeyNames
{
    [DllImport("user32.dll")] static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")] static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);
    [DllImport("user32.dll", EntryPoint = "GetKeyNameTextW", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder text, int size);
    const uint VkToScanCode = 0, VkToChar = 2;
    // The layout of the calling (UI) thread: the input language the user types with in FRAME.
    public static nint ActiveLayout => GetKeyboardLayout(0);
    public static nint[] InstalledLayouts()
    {
        int count = GetKeyboardLayoutList(0, null); if (count <= 0) return [];
        var list = new nint[count]; int read = GetKeyboardLayoutList(count, list); return list[..Math.Max(0, Math.Min(read, count))];
    }
    // 1. Fixed names (keys that type nothing, letters/digits/numpad/F-keys); 2. the character the key types in the layout
    // (dead keys: their accent), upper-cased in the layout's culture; 3. Windows' key name; 4. WPF's key name.
    public static string Label(uint vk, nint layout = 0)
    {
        if (KeyLabels.Named(vk) is { } named) return named;
        if (layout == 0) layout = GetKeyboardLayout(0);
        if (KeyLabels.FromLayoutChar(MapVirtualKeyEx(vk, VkToChar, layout), KeyLabels.LayoutCulture(layout, CultureInfo.CurrentCulture)) is { } typed) return typed;
        uint scan = MapVirtualKeyEx(vk, VkToScanCode, layout);
        if (scan != 0) { var text = new StringBuilder(64); if (GetKeyNameText((int)(scan << 16), text, text.Capacity) > 0) return text.ToString(); }
        var key = KeyInterop.KeyFromVirtualKey((int)vk); return key == Key.None ? $"Key {vk}" : key.ToString();
    }
}
