using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

public static class DesertAppearance
{
    public static void Apply(bool light)
    {
        string[] keys = ["Background", "Surface", "Raised", "Line", "Text", "Muted", "Accent", "Hero", "HeroText", "Subtle", "NavActive", "NavFrame"];
        string[] colors = light ? ["#F1E9DE", "#F8F3EB", "#E8DFD2", "#B6A18C", "#29211C", "#655448", "#AC431A", "#DADBC1AA", "#29211C", "#4A3D33", "#6B2E12", "#9A5E14"]
            : ["#171310", "#B0211C18", "#A0302721", "#504238", "#F5EEE6", "#C7B8AA", "#F27C39", "#70492218", "#FFF4EC", "#DACEC2", "#FFE3C4", "#E5AC52"]; // Subtle: small secondary text, a little brighter than Muted; NavActive: warm active sidebar item; NavFrame: logo gold for the sidebar corner frame
        for (int i = 0; i < keys.Length; i++) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); b.Freeze(); Application.Current.Resources[keys[i]] = b; }
    }
    public static ImageBrush Background()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/Assets/desert-drift.png")); image.Freeze();
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill }; brush.Freeze(); return brush;
    }
}
