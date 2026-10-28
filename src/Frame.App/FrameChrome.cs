using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shell;
using Shapes = System.Windows.Shapes;

namespace Frame.App;

// The main window's title bar for secondary FRAME windows (Crop for social): same WindowChrome (36 px caption, 6 px resize
// border, no glass), "FRAME" + muted subtitle at 10 px, the shared CaptionButton/CloseCaptionButton styles with the same
// glyphs, the faint accent rule under the name and the 1 px Line outline (hidden when maximized). Dragging, resizing,
// Alt+Space and Alt+F4 come from WindowChrome; the window keeps its own key handling (e.g. Esc).
public static class FrameChrome
{
    const string Restore = "M 0,3 L 7,3 7,10 0,10 Z M 3,3 L 3,0 10,0 10,7 7,7", Maximize = "M 0,0 L 10,0 10,10 0,10 Z";

    public static UIElement Wrap(Window window, UIElement body, string subtitle)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        window.FontFamily = new FontFamily("Segoe UI"); window.UseLayoutRounding = true;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); root.RowDefinitions.Add(new RowDefinition());
        root.SetResourceReference(Panel.BackgroundProperty, "Background");

        var bar = new Grid();
        var name = new TextBlock { FontSize = 10, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var brand = new Run("FRAME") { FontWeight = FontWeights.SemiBold }; brand.SetResourceReference(TextElement.ForegroundProperty, "Text");
        var rest = new Run("  ·  " + subtitle); rest.SetResourceReference(TextElement.ForegroundProperty, "Muted");
        name.Inlines.Add(brand); name.Inlines.Add(rest); bar.Children.Add(name);
        var rule = new Shapes.Rectangle { Width = 56, Height = 2, Margin = new Thickness(16, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Opacity = .25, IsHitTestVisible = false };
        rule.SetResourceReference(Shapes.Shape.FillProperty, "Accent"); bar.Children.Add(rule);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; WindowChrome.SetIsHitTestVisibleInChrome(buttons, true);
        Button Caption(string style, string glyph, double thickness, bool aliased, string automationName, string tip, Action click)
        {
            var b = new Button { ToolTip = tip }; b.SetResourceReference(FrameworkElement.StyleProperty, style); System.Windows.Automation.AutomationProperties.SetName(b, automationName);
            var path = new Shapes.Path { Data = Geometry.Parse(glyph), StrokeThickness = thickness };
            path.SetBinding(Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = b });
            if (aliased) RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);
            b.Content = path; b.Click += (_, _) => click(); buttons.Children.Add(b); return b;
        }
        Caption("CaptionButton", "M 0,0 L 10,0", 1, true, "Minimize", "Minimize", () => SystemCommands.MinimizeWindow(window));
        var maximize = Caption("CaptionButton", Maximize, 1, true, "Maximize or restore", "Expand or restore the window", () => { if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window); });
        Caption("CloseCaptionButton", "M 0,0 L 10,10 M 10,0 L 0,10", 1.2, false, "Close", "Close", window.Close);
        bar.Children.Add(buttons); root.Children.Add(bar);

        Grid.SetRow((FrameworkElement)body, 1); root.Children.Add(body);
        var outline = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false }; outline.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetRowSpan(outline, 2); root.Children.Add(outline);
        if (window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize) maximize.Visibility = Visibility.Collapsed;
        window.StateChanged += (_, _) => {
            bool max = window.WindowState == WindowState.Maximized;
            outline.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            ((Shapes.Path)maximize.Content).Data = Geometry.Parse(max ? Restore : Maximize);
        };
        return root;
    }
}
