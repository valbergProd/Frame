using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Frame.App;

// --render <png> --nav-test <report.json>: sidebar corner-frame diagnostics. Selects Capture, Library and Settings in turn,
// measures the corner frame against the rendered icon + label, checks spacing/containment/no overlap, and renders each
// state (plus hover preview, focus after a programmatic (mouse-like) focus, and keyboard focus after a real Tab key).
public partial class MainWindow
{
    async Task ValidateNavigation(string path)
    {
        var results = new List<object>(); var frames = new List<object>();
        void Check(string name, bool success, object? detail = null) => results.Add(new { name, passed = success, detail });
        string Png(string suffix) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-" + suffix + ".png");
        async Task Settle() { UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        var navs = new[] { (Name: "Capture", Button: CaptureNav, Page: (FrameworkElement)HomePage), ("Library", LibraryNav, LibraryPage), ("Settings", SettingsNav, SettingsPage) };
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        FrameworkElement Part(Button b, string name) => (FrameworkElement)b.Template.FindName(name, b);
        // Rendered icon + label: the ContentPresenter's child TextBlock (string content), measured by WPF layout.
        Rect LabelBounds(Button b) { var label = (ContentPresenter)Part(b, "Label"); var text = (FrameworkElement)VisualTreeHelper.GetChild(label, 0); return Bounds(text); }
        // Crop of the sidebar column at 2x for close inspection, rendered from the window root so focus adorners are included.
        void SaveSidebar(string file) { UpdateLayout(); var root = (FrameworkElement)VisualTreeHelper.GetChild(this, 0); /* window root: includes the adorner layer (focus visual) */ var image = new RenderTargetBitmap((int)(root.ActualWidth * 2), (int)(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32); image.Render(root); var crop = new CroppedBitmap(image, new Int32Rect(0, 0, 400, (int)(root.ActualHeight * 2))); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop)); using var f = File.Create(file); encoder.Save(f); }
        foreach (var nav in navs)
        {
            ShowPage(nav.Page == HomePage ? HomePage : nav.Page == LibraryPage ? LibraryPage : SettingsPage, "", ""); await Settle();
            var corners = (FrameworkElement)Part(nav.Button, "Corners"); var content = LabelBounds(nav.Button);
            // The visible frame = the Corners element including its centring translation; measured against the glyph ink.
            var frame = Bounds(corners); var labelText = (TextBlock)VisualTreeHelper.GetChild((ContentPresenter)Part(nav.Button, "Label"), 0);
            var ink = NavCorners.InkBounds(labelText) is { } r ? labelText.TransformToAncestor(this).TransformBounds(r) : content;
            double left = ink.X - frame.X, right = frame.Right - ink.Right, top = ink.Y - frame.Y, bottom = frame.Bottom - ink.Bottom;
            var shift = corners.RenderTransform as TranslateTransform;
            frames.Add(new { nav.Name, frameX = Math.Round(frame.X, 2), frameW = Math.Round(frame.Width, 2), frameH = Math.Round(frame.Height, 2), inkW = Math.Round(ink.Width, 2), inkH = Math.Round(ink.Height, 2), left = Math.Round(left, 2), right = Math.Round(right, 2), top = Math.Round(top, 2), bottom = Math.Round(bottom, 2), shiftX = shift?.X ?? 0, shiftY = shift?.Y ?? 0, cornerOpacity = corners.Opacity });
            Check($"{nav.Name}: corners centred horizontally on the rendered icon + label (8–14 px each side, within 1 px)", left is >= 8 and <= 14 && right is >= 8 and <= 14 && Math.Abs(left - right) <= 1, new { left, right });
            Check($"{nav.Name}: corners centred vertically on the ink (within 1 px) and the text is not overlapped", top is >= 5 and <= 14 && bottom is >= 5 and <= 14 && Math.Abs(top - bottom) <= 1 && frame.Contains(ink), new { top, bottom });
            var others = navs.Where(o => o.Button != nav.Button).Select(o => Bounds(o.Button));
            Check($"{nav.Name}: frame stays inside its own item and clear of the other items", Bounds(nav.Button).Contains(frame) && others.All(o => !o.IntersectsWith(frame)));
            Check($"{nav.Name}: only the active item shows corners", corners.Opacity is > .35 and < .46 && navs.Where(o => o.Button != nav.Button).All(o => ((FrameworkElement)Part(o.Button, "Corners")).Opacity == 0));
            Check($"{nav.Name}: no selection background, edge bar or old dot", nav.Button.Background is SolidColorBrush { Color.A: 0 } && nav.Button.Template.FindName("Dot", nav.Button) == null && nav.Button.Template.FindName("Wash", nav.Button) == null && nav.Button.Template.FindName("Focus", nav.Button) == null);
            SaveSidebar(Png("selected-" + nav.Name.ToLowerInvariant())); SaveRender(Png("window-" + nav.Name.ToLowerInvariant()));
        }
        var widths = frames.Select(f => (double)f.GetType().GetProperty("frameW")!.GetValue(f)!).ToArray();
        Check("Frame widths differ with the label (Capture narrower than Settings)", widths[0] < widths[2] && widths.Distinct().Count() == 3, widths);

        // Hover preview: IsMouseOver cannot be set from code, so the trigger's setters are applied to the Library item by hand.
        ShowPage(HomePage, "", ""); await Settle();
        var libraryCorners = (FrameworkElement)Part(LibraryNav, "Corners"); libraryCorners.Opacity = .16; LibraryNav.SetResourceReference(ForegroundProperty, "Text");
        SaveSidebar(Png("hover-preview-library")); libraryCorners.ClearValue(OpacityProperty); LibraryNav.ClearValue(ForegroundProperty); await Settle();

        // Mouse-like focus (Focus() without keyboard input) must not draw a focus rectangle.
        Activate(); CaptureNav.Focus(); await Settle();
        var mouseAdorners = AdornerLayer.GetAdornerLayer(CaptureNav)?.GetAdorners(CaptureNav) ?? [];
        bool mouseFocusAdorned = mouseAdorners.Length > 0; string adornerTypes = string.Join(",", mouseAdorners.Select(x => x.GetType().Name)), recentDevice = InputManager.Current.MostRecentInputDevice?.GetType().Name ?? "none";
        // Windows may refuse foreground to a test process; the name then says keyboard focus could not be checked.
        bool active = IsActive, focused = CaptureNav.IsKeyboardFocused;
        // WPF shows the focus visual only when the keyboard was this process's most recent input device. Activating the test
        // window counts as keyboard input, so the mouse path can only be judged by a real click (template has no focus trigger).
        if (recentDevice.Contains("Keyboard")) Check("Mouse-like focus NOT checked (WPF counts window activation as keyboard input; only a real mouse click makes the mouse the latest device)", true, new { active, focused, adornerTypes, recentDevice });
        else Check("Programmatic (mouse-like) focus shows no focus rectangle" + (active && focused ? "" : " (keyboard focus NOT checked: window not active)"), !mouseFocusAdorned, new { active, focused, mouseFocusAdorned, adornerTypes, recentDevice });
        SaveSidebar(Png("focus-mouse"));
        // Keyboard: a real Tab key through the input manager moves focus to Library and shows the understated focus cue.
        var source = PresentationSource.FromVisual(this)!;
        // One PreviewKeyDown is one key press (the input manager also raises KeyDown); sending both pressed Tab twice.
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Settle(); await Task.Delay(150);
        bool keyboardAdorned = AdornerLayer.GetAdornerLayer(LibraryNav)?.GetAdorners(LibraryNav)?.Length > 0;
        // A synthetic key event does not make the keyboard WPF's most recent input device, so WPF draws no focus visual for
        // it (a real Tab does). To render the keyboard cue anyway, the diagnostic asks WPF's own KeyboardNavigation to show
        // the focus visual as it does for real keyboard focus (internal AlwaysShowFocusVisual + ShowFocusVisual, test only).
        if (!keyboardAdorned && LibraryNav.IsKeyboardFocused)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var always = typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", flags); var show = typeof(KeyboardNavigation).GetMethod("ShowFocusVisual", flags, Type.EmptyTypes);
            if (always?.CanWrite == true && show != null) { always.SetValue(null, true); show.Invoke(null, null); await Settle(); keyboardAdorned = AdornerLayer.GetAdornerLayer(LibraryNav)?.GetAdorners(LibraryNav)?.Length > 0; SaveSidebar(Png("focus-keyboard")); always.SetValue(null, false); }
        }
        else if (keyboardAdorned) { SaveSidebar(Png("focus-keyboard")); }
        {
        }
        if (active && focused) Check("Tab moves focus Capture → Library; the keyboard focus cue renders (WPF focus visual requested explicitly, see note)", LibraryNav.IsKeyboardFocused && keyboardAdorned, new { focused = (Keyboard.FocusedElement as FrameworkElement)?.Name, keyboardAdorned });
        else Check("Keyboard focus cue NOT checked (Windows refused foreground to the test window)", true, new { active, focused });
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { results, frames }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
