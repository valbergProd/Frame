using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Frame.App;

// FRAME's own confirmation/notice window (0.5.5): replaces Windows message boxes, which play a system sound (Default Beep,
// Exclamation, Question) — FRAME makes no sounds of its own. Same chrome as Trim and Crop for social; Enter/Esc work,
// the safe choice is the default and has focus.
static class QuietDialog
{
    // Returns true only when the user chose the confirming action.
    public static bool Confirm(Window? owner, string title, string message, string confirm, string cancel = "Cancel") => Show(owner, title, message, confirm, cancel);
    public static void Inform(Window? owner, string title, string message) => Show(owner, title, message, null, "OK");
    static bool Show(Window? owner, string title, string message, string? confirm, string close)
    {
        bool confirmed = false;
        var window = new Window { Title = "FRAME · " + title, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner == null || !owner.IsVisible, WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Topmost = owner == null || !owner.IsVisible };
        if (owner is { IsVisible: true }) window.Owner = owner;
        var body = new StackPanel { Margin = new Thickness(26, 18, 26, 22) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Light, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var text = new TextBlock { Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap }; text.SetResourceReference(TextBlock.ForegroundProperty, "Subtle"); body.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var no = new Button { Content = close, MinWidth = 96, IsCancel = true, IsDefault = true }; no.Click += (_, _) => window.Close();
        if (confirm != null)
        {
            var yes = new Button { Content = confirm, MinWidth = 96, Margin = new Thickness(0, 0, 10, 0) }; yes.SetResourceReference(Control.BorderBrushProperty, "Accent");
            yes.Click += (_, _) => { confirmed = true; window.Close(); }; buttons.Children.Add(yes);
        }
        buttons.Children.Add(no); body.Children.Add(buttons);
        window.Content = FrameChrome.Wrap(window, body, title);
        window.Loaded += (_, _) => Keyboard.Focus(no);
        window.ShowDialog();
        return confirmed;
    }
}
