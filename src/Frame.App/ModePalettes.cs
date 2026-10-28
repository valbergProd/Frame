using System.Windows;
using System.Windows.Media;

namespace Frame.App;

// Application-wide theme brushes per visual mode (0.6.0). Every page (Capture, Library, Settings, dialogs, Trim, Crop for
// social, context menus) uses these keys through DynamicResource/SetResourceReference, so a mode switch recolours the whole
// app at once. Extra keys:
//   SidebarBackground  sidebar tint (transparent in the desert modes of 0.5.5)
//   PageScrim          wash over a scene behind Library/Settings (Pink, Emo) so text stays readable
//   Panel              translucent surface for layout panels (bottom bar, notes) over a scene
//   Recording          the "recording" signal colour (status dot) in the plain modes
//   CloseGlyph         the title bar's close (X) glyph
//   HeadingFont        typeface for large titles (Segoe UI Light in most modes)
public static class ModePalettes
{
    static readonly string[] Keys = ["Background", "Surface", "Raised", "Line", "Text", "Muted", "Accent", "Hero", "HeroText", "Subtle", "NavActive", "NavFrame", "SidebarBackground", "PageScrim", "Panel", "Recording", "CloseGlyph"];
    public static void Apply(VisualMode mode)
    {
        string[] colors = mode switch
        {
            // Original GUI (0.6.1; the 0.6.0 Cinematic D design): the desert palette with a slightly deeper, warmer night;
            // panels are smoked glass over the scene. (The 0.5.5 palette, DesertAppearance, is no longer a mode.)
            VisualMode.Original => ["#120E0B", "#B01A1511", "#A0262019", "#4A3B30", "#F5EEE6", "#BDAE9F", "#F28A3C", "#70492218", "#FFF4EC", "#D6C9BC", "#FFE3C4", "#E5AC52", "#66090706", "#00000000", "#9E15110E", "#F28A3C", "#D4A390"],
            // Dark / No Bullshit: neutral charcoal and grey, one functional red for "recording".
            VisualMode.Dark => ["#141414", "#1C1C1C", "#262626", "#3A3A3A", "#ECECEC", "#A3A3A3", "#D4D4D4", "#262626", "#F2F2F2", "#C4C4C4", "#FFFFFF", "#8C8C8C", "#101010", "#00000000", "#1C1C1C", "#E5484D", "#BDBDBD"],
            // Light: white and pale grey, dark readable text.
            VisualMode.Light => ["#F4F4F4", "#FFFFFF", "#E9E9E9", "#CDCDCD", "#1C1C1C", "#5C5C5C", "#2E2E2E", "#E9E9E9", "#1C1C1C", "#404040", "#000000", "#7A7A7A", "#ECECEC", "#00000000", "#FFFFFF", "#D93036", "#5C5C5C"],
            // Pink mode: blush/rose panels with deep plum text over the illustrated scene; neon pink accent.
            VisualMode.Pink => ["#FBE3EE", "#E6FFF4F8", "#F7CFE1", "#E79BC0", "#4E1232", "#8E3F68", "#FF2E97", "#D9FFE6F0", "#4E1232", "#6E2550", "#C2186F", "#FF5DB1", "#B3FFF0F6", "#BFFBE3EE", "#D9FFF2F7", "#FF2E97", "#A33A6E"],
            // Emo mode: black, charcoal, burgundy, muted purple and deep crimson.
            _ => ["#0A0809", "#D9140F12", "#1E1519", "#3C2830", "#E4DADD", "#9C8B92", "#A3243D", "#B3170F13", "#F0E6E9", "#B9AAB0", "#D8A6B6", "#7E2943", "#B30A0809", "#B80A0809", "#C7120D10", "#A3243D", "#B58A97"],
        };
        Set(Keys, colors);
        Font(mode switch { VisualMode.Emo => "Palatino Linotype", VisualMode.Pink => "Segoe UI Semibold", _ => "Segoe UI" });
    }
    static void Set(string[] keys, string[] colors)
    {
        for (int i = 0; i < keys.Length; i++) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); b.Freeze(); Application.Current.Resources[keys[i]] = b; }
    }
    static void Font(string family) => Application.Current.Resources["HeadingFont"] = new FontFamily(family);
    public static bool IsLight(VisualMode mode) => mode is VisualMode.Light or VisualMode.Pink;
}
