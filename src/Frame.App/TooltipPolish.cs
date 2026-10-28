using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;

namespace Frame.App;

// Event-driven placement; no timer, rendering subscription or animation clock.
static class TooltipPolish
{
    static bool installed;
    public static void Install()
    {
        if (installed) return;
        installed = true;
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(650));
        ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(180));
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(6500));
        ToolTipService.ShowsToolTipOnKeyboardFocusProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(Opened));
    }
    public static void Describe(FrameworkElement control, string text)
    {
        control.ToolTip = text;
        AutomationProperties.SetHelpText(control, text);
    }
    static void Opened(object sender, RoutedEventArgs e)
    {
        var tip = (ToolTip)sender;
        if (tip.PlacementTarget is not FrameworkElement target || Window.GetWindow(target) is not { } window) return;
        tip.MaxWidth = Math.Max(80, Math.Min(280, window.ActualWidth - 24));
        tip.Placement = PlacementMode.Custom;
        tip.CustomPopupPlacementCallback = (size, targetSize, _) =>
        {
            var origin = target.TranslatePoint(new Point(), window);
            double x = Math.Clamp(0, 8 - origin.X, Math.Max(8 - origin.X, window.ActualWidth - origin.X - size.Width - 8));
            double y = targetSize.Height + 8;
            if (origin.Y + y + size.Height > window.ActualHeight - 8) y = -size.Height - 8;
            y = Math.Clamp(y, 8 - origin.Y, Math.Max(8 - origin.Y, window.ActualHeight - origin.Y - size.Height - 8));
            return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
        };
    }
}
