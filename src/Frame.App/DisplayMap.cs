using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Frame.App;

// Capture source › monitor map: one focusable ToggleButton card per display, placed by DisplayLayout (Frame.Core).
// Click, Space, Enter and UI Automation Toggle all select/deselect; selection persists through settings.SelectedDisplays.
public partial class MainWindow
{
    const string Presentation = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    // Theme brushes are DynamicResources, so light/dark switches restyle the cards; the 2 px accent ring is a separate layer
    // so selecting never shifts the label. Hover is a faint Text-coloured wash plus a Muted edge; no glow, no animation.
    static readonly ControlTemplate DisplayCardTemplate = (ControlTemplate)XamlReader.Parse($@"<ControlTemplate {Presentation} TargetType=""ToggleButton""><Grid>
        <Border x:Name=""Face"" CornerRadius=""4"" Background=""{{DynamicResource Raised}}"" BorderBrush=""{{DynamicResource Line}}"" BorderThickness=""1""/>
        <Border x:Name=""Wash"" CornerRadius=""4"" Background=""{{DynamicResource Text}}"" Opacity=""0""/>
        <Border x:Name=""Ring"" CornerRadius=""4"" BorderBrush=""{{DynamicResource Accent}}"" BorderThickness=""2"" Visibility=""Collapsed""/>
        <ContentPresenter Margin=""4,3"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/></Grid>
        <ControlTemplate.Triggers>
         <MultiTrigger><MultiTrigger.Conditions><Condition Property=""IsMouseOver"" Value=""True""/><Condition Property=""IsEnabled"" Value=""True""/></MultiTrigger.Conditions><Setter TargetName=""Wash"" Property=""Opacity"" Value="".05""/><Setter TargetName=""Face"" Property=""BorderBrush"" Value=""{{DynamicResource Muted}}""/></MultiTrigger>
         <Trigger Property=""IsPressed"" Value=""True""><Setter TargetName=""Wash"" Property=""Opacity"" Value="".09""/></Trigger>
         <Trigger Property=""IsChecked"" Value=""True""><Setter TargetName=""Ring"" Property=""Visibility"" Value=""Visible""/></Trigger>
         <Trigger Property=""IsEnabled"" Value=""False""><Setter Property=""Opacity"" Value="".45""/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate>");
    // Keyboard-only focus cue (WPF shows FocusVisualStyle for keyboard focus, not after a mouse click): dashed, in the gap.
    static readonly Style DisplayCardFocus = (Style)XamlReader.Parse($@"<Style {Presentation}><Setter Property=""Control.Template""><Setter.Value><ControlTemplate><Rectangle Margin=""-2"" RadiusX=""5"" RadiusY=""5"" Stroke=""{{DynamicResource Text}}"" StrokeThickness=""1"" StrokeDashArray=""2 2"" SnapsToDevicePixels=""True""/></ControlTemplate></Setter.Value></Setter></Style>");
    CaptureTarget[]? fakeDisplays;
    bool cardsLocked;
    // --render diagnostics only: --fake-displays "x,y,w,h[,P];…" replaces the real monitors and --fake-selected "1,3"
    // (1-based) picks the selection, so the map can be rendered for any arrangement without touching Windows display settings.
    CaptureTarget[]? FakeDisplays()
    {
        if (fakeDisplays != null || !args.Contains("--render") || Array.IndexOf(args, "--fake-displays") is not (int at and >= 0)) return fakeDisplays;
        fakeDisplays = DisplayLayout.Fake(args[at + 1]);
        if (Array.IndexOf(args, "--fake-selected") is int pick and >= 0) { selected.Clear(); foreach (var n in args[pick + 1].Split(',')) selected.Add(fakeDisplays[int.Parse(n, CultureInfo.InvariantCulture) - 1].Id); settings.FollowPrimaryDisplay = false; settings.SelectedDisplays = selected.ToArray(); }
        return fakeDisplays;
    }
    void BuildDisplayCards()
    {
        string? focused = Keyboard.FocusedElement is ToggleButton { Tag: string id } f && MonitorCanvas.Children.Contains(f) ? id : null;
        MonitorCanvas.Children.Clear(); cardsLocked = engine.Busy || actions.CurrentCount == 0;
        if (displays.Length == 0) { MonitorCanvas.Height = 0; return; }
        var map = DisplayLayout.ArrangeReadable(displays.Select(d => d.Bounds).ToArray(), Math.Max(240, MonitorCanvas.ActualWidth));
        MonitorCanvas.Height = map.Height;
        int chosen = testSource.Length > 0 ? 0 : displays.Count(d => selected.Contains(d.Id));
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int i = 0; i < displays.Length; i++)
        {
            var d = displays[i]; var r = map.Cards[i];
            bool active = testSource.Length == 0 && selected.Contains(d.Id), primary = d.Name.Contains("Primary");
            string title = d.Name.Split('·')[0].Trim(), number = title.Split(' ')[^1], resolution = $"{d.Bounds.Width} × {d.Bounds.Height}";
            var card = new ToggleButton { Template = DisplayCardTemplate, FocusVisualStyle = DisplayCardFocus, Tag = d.Id, Width = r.Width, Height = r.Height, IsChecked = active, IsEnabled = !cardsLocked, Cursor = Cursors.Hand, Content = CardLabel(number, d.Bounds, primary, r.Width - 10, r.Height - 8, dip) };
            KeyboardNavigation.SetAcceptsReturn(card, true); ToolTipService.SetShowOnDisabled(card, true);
            AutomationProperties.SetName(card, DisplayLayout.Describe(int.TryParse(number, out int n) ? n : i + 1, d.Bounds, primary, active));
            TooltipPolish.Describe(card, cardsLocked ? "Stop capture before changing source." : $"{title} · {resolution}{(primary ? " · Primary" : "")}\n" + (!active ? "Click to capture this display" : chosen == 1 ? "Captured · select another display before removing this one" : "Captured · click to leave it out"));
            // Checked/Unchecked (not Click): UI Automation's Toggle pattern changes IsChecked without raising Click.
            card.Checked += (_, _) => ToggleDisplay(d.Id); card.Unchecked += (_, _) => ToggleDisplay(d.Id);
            Canvas.SetLeft(card, r.X); Canvas.SetTop(card, r.Y); MonitorCanvas.Children.Add(card);
            if (focused == d.Id) card.Focus(); // the card is rebuilt after every toggle; keyboard users keep their place
        }
    }
    // Richest label that fits the measured card: number + resolution (+ "Primary"), then the resolution split over two lines
    // (portrait cards), then number and resolution on one line (flat cards), then the number alone. The full description
    // is always in the tooltip and the automation name.
    static UIElement CardLabel(string number, Box bounds, bool primary, double width, double height, double dip)
    {
        var face = new Typeface("Segoe UI");
        Size Measure(string text, double size) { var t = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Brushes.White, dip); return new(t.WidthIncludingTrailingWhitespace, t.Height); }
        TextBlock Line(string text, double size, string brush) { var t = new TextBlock { Text = text, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center }; t.SetResourceReference(TextBlock.ForegroundProperty, brush); return t; }
        string resolution = $"{bounds.Width} × {bounds.Height}";
        var head = (number, 14d, "Text"); var mark = ("Primary", 10d, "Subtle");
        (string, double, string)[][] options = [
            .. primary ? new[] { new[] { head, (resolution, 10d, "Muted"), mark } } : [], [head, (resolution, 10d, "Muted")],
            .. primary ? new[] { new[] { head, ($"{bounds.Width} ×", 10d, "Muted"), ($"{bounds.Height}", 10d, "Muted"), mark } } : [], [head, ($"{bounds.Width} ×", 10d, "Muted"), ($"{bounds.Height}", 10d, "Muted")]];
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (options.FirstOrDefault(lines => lines.All(l => Measure(l.Item1, l.Item2).Width <= width) && lines.Sum(l => Measure(l.Item1, l.Item2).Height) <= height) is { } fit) { foreach (var l in fit) stack.Children.Add(Line(l.Item1, l.Item2, l.Item3)); return stack; }
        var one = Line(number, 14, "Text"); stack.Children.Add(one);
        if (Measure(number + "  ", 14).Width + Measure(resolution, 10).Width <= width && Measure(number, 14).Height <= height) { var run = new System.Windows.Documents.Run("  " + resolution) { FontSize = 10 }; run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted"); one.Inlines.Add(run); }
        return stack;
    }
    // Same persistence path as the former checkboxes. The last selected display cannot be removed: an empty selection cannot
    // start ("Select at least one display or window."), so that click is a quiet no-op (the tooltip says why). A click is a
    // deliberate choice: it ends any automatic display recovery (DisplayRecovery.cs).
    void ToggleDisplay(string id)
    {
        if (engine.Busy || actions.CurrentCount == 0) { DrawMonitors(); return; } // stale input while capture starts; cards are disabled
        selected.IntersectWith(displays.Select(d => d.Id)); // a disconnected display could not be deselected anywhere else
        if (!selected.Contains(id)) selected.Add(id);
        else if (selected.Count > 1) selected.Remove(id);
        else { DrawMonitors(); return; }
        ForgetDisplayRecovery();
        settings.FollowPrimaryDisplay = DisplaySelection.Primary(displays, PrimaryDisplayId()) is { } primary && selected.SetEquals([primary.Id]);
        settings.SelectedDisplays = settings.FollowPrimaryDisplay == true ? [] : selected.ToArray();
        SaveSettings(); DrawMonitors();
    }
    // --render --display-ui-test <report.json> (needs --fake-displays with 3 displays, the primary not first, and --fake-selected 1):
    // drives the cards through UI Automation and keyboard events and checks selection, persistence, locking and (0.5.5) the
    // display recovery when selected displays disappear or return (ValidateDisplayRecovery, DisplayRecovery.cs).
    async Task ValidateDisplayCards(string path)
    {
        var results = new List<object>();
        void Check(string name, bool success) => results.Add(new { name, passed = success });
        ToggleButton Card(int index) => MonitorCanvas.Children.OfType<ToggleButton>().Single(c => (string)c.Tag == displays[index].Id);
        void Toggle(int index) => ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(Card(index))!.GetPattern(PatternInterface.Toggle)!).Toggle();
        string[] Saved() => DisplaySelection.Resolve(displays, SettingsStore.Load(configPath), PrimaryDisplayId()).Selected;
        try
        {
            // Since 0.6.1 the shared canvas is in the active layout's display popup, not the hidden legacy expander.
            if (activeLayout != null)
            {
                Button? FindSource(DependencyObject node)
                {
                    if (node is Button b && AutomationProperties.GetName(b) == "Choose displays") return b;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                        if (FindSource(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
                    return null;
                }
                (FindSource(activeLayout.Root) ?? throw new InvalidOperationException("Display source button not found")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else SourceExpander.IsExpanded = true;
            UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var cards = MonitorCanvas.Children.OfType<ToggleButton>().ToArray();
            var rects = cards.Select(c => new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.ActualWidth, c.ActualHeight)).ToArray();
            Check("One card per display, inside the canvas, no overlap", cards.Length == displays.Length && rects.All(r => r.Left >= 0 && r.Top >= 0 && r.Right <= MonitorCanvas.ActualWidth + .5 && r.Bottom <= MonitorCanvas.ActualHeight + .5) && !rects.SelectMany((a, i) => rects.Skip(i + 1), (a, b) => a.IntersectsWith(b)).Any(o => o));
            Check("Initial selection from --fake-selected 1", Card(0).IsChecked == true && Card(1).IsChecked == false && AutomationProperties.GetName(Card(0)).EndsWith(", selected"));
            Toggle(1); Check("Automation Toggle selects a second display and persists it", Card(1).IsChecked == true && settings.SelectedDisplays.Length == 2 && Saved().Order().SequenceEqual(new[] { displays[0].Id, displays[1].Id }.Order()));
            Toggle(1); Check("Toggling again removes it and persists", Card(1).IsChecked == false && Saved().SequenceEqual([displays[0].Id]));
            Toggle(0); Check("The last selected display cannot be removed (quiet no-op)", Card(0).IsChecked == true && Saved().SequenceEqual([displays[0].Id]) && selected.Count == 1);
            var card = Card(1); card.Focus();
            card.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(card), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Check("Enter toggles the focused card", Card(1).IsChecked == true && Saved().Length == 2);
            Check($"Keyboard focus follows the rebuilt card (window active: {IsActive}; only checked while active)", !IsActive || Keyboard.FocusedElement == Card(1));
            card = Card(1); card.Focus();
            card.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(card), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            card.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(card), 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Check("Space toggles the focused card", Card(1).IsChecked == false && Saved().Length == 1);
            Check("Displays only: no window/game or region mode buttons (0.5.5)", FindName("WindowButton") == null && FindName("RegionButton") == null && FindName("SwitchToDisplaysButton") == null && FindName("CaptureModeButtons") == null && testSource.Length == 0);
            await actions.WaitAsync(); UpdateState();
            Check("Cards are disabled with an explanation while an operation is pending", MonitorCanvas.Children.OfType<ToggleButton>().All(c => !c.IsEnabled && (string)c.ToolTip == "Stop capture before changing source."));
            actions.Release(); UpdateState();
            Check("Cards are enabled again afterwards", MonitorCanvas.Children.OfType<ToggleButton>().All(c => c.IsEnabled));
            await ValidateDisplayRecovery(Check, Saved);
        }
        catch (Exception e) { Check("Exception: " + e.Message, false); }
        finally { await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
    }
}
