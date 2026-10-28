using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Frame.App;

// Optical centring of the sidebar corner frame. WPF sizes the frame from the label's layout box, but the visible ink
// (icon + text) sits inside that box unevenly: the icon glyph has side bearing on the left and the font's line box has
// more room above the capitals than below the descenders, so the frame looked shifted left and up. On the "Framed" Grid
// (children: Corners, then the Label ContentPresenter) this moves the corners by half the ink imbalance, measured from
// the actual glyph outlines with the TextBlock's own font, and snapped to device pixels so the 1 px strokes stay crisp.
// The frame size still comes from layout; recalculated on load and whenever the frame's size changes.
public static class NavCorners
{
    public static readonly DependencyProperty CentreOnInkProperty = DependencyProperty.RegisterAttached("CentreOnInk", typeof(bool), typeof(NavCorners), new PropertyMetadata(false, (d, e) => {
        if (d is FrameworkElement framed && (bool)e.NewValue) { framed.Loaded += (_, _) => Centre(framed); framed.SizeChanged += (_, _) => Centre(framed); }
    }));
    public static void SetCentreOnInk(DependencyObject d, bool value) => d.SetValue(CentreOnInkProperty, value);
    public static bool GetCentreOnInk(DependencyObject d) => (bool)d.GetValue(CentreOnInkProperty);

    // Ink rectangle of the label text inside its TextBlock (DIP, TextBlock coordinates), or null when there is no text.
    public static Rect? InkBounds(TextBlock text)
    {
        if (string.IsNullOrEmpty(text.Text)) return null;
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        var ink = formatted.BuildGeometry(new Point()).Bounds; return ink.IsEmpty ? null : ink;
    }

    static void Centre(FrameworkElement framed)
    {
        if (framed is not Panel { Children.Count: >= 2 } panel || panel.Children[0] is not FrameworkElement corners || panel.Children[1] is not ContentPresenter label) return;
        if (VisualTreeHelper.GetChildrenCount(label) == 0 || VisualTreeHelper.GetChild(label, 0) is not TextBlock text || InkBounds(text) is not { } ink) { corners.RenderTransform = Transform.Identity; return; }
        double scale = VisualTreeHelper.GetDpi(text).PixelsPerDip;
        double Snap(double v) => Math.Round(v * scale) / scale;
        // Positive dx: more empty space left of the ink than right of it, so the corners move right (likewise down for dy).
        double dx = (ink.Left - (text.ActualWidth - ink.Right)) / 2, dy = (ink.Top - (text.ActualHeight - ink.Bottom)) / 2;
        corners.RenderTransform = new TranslateTransform(Snap(dx), Snap(dy));
    }
}
