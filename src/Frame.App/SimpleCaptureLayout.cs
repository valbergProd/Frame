using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Frame.App;

// Capture layout of the plain and scene modes (0.6.0): Dark / No Bullshit, Light, Pink and Emo. Straightforward controls,
// coloured only by the mode palette (ModePalettes: theme brushes + HeadingFont); no stone, glow or animation of its own.
// Composition follows Cinematic D in a plain form: title and main action, horizontal replay length, quiet manual recording,
// shortcut rows, notes, and a bottom bar with display, quality and audio. Pink/Emo scenes draw behind it; Emo puts its quote
// in HeroHost. Every value comes from CaptureView and every action goes through ICaptureCommands, so it behaves exactly like
// the Original GUI controls. Fixed slots (title width, two-line note slot, manual row) keep positions stable in every state.
sealed class SimpleCaptureLayout : ICaptureLayout
{
    readonly ICaptureCommands commands;
    readonly VisualMode mode;
    readonly Grid root = new();
    readonly StackPanel notes = new();
    readonly Decorator displays = new();
    readonly ContentControl hero = new() { Focusable = false, IsTabStop = false };
    readonly Ellipse dot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Button shadow = new() { MinWidth = 330, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(26, 0, 26, 0) };
    readonly TextBlock shadowTitle = new() { FontSize = 21, FontWeight = FontWeights.Light, TextWrapping = TextWrapping.NoWrap };
    readonly TextBlock loading = new() { FontSize = 11, Margin = new Thickness(2, 8, 0, 0), Height = 16 };
    readonly Button save = new() { Height = 40, Padding = new Thickness(16, 0, 16, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock saveTitle = new() { FontSize = 13 };
    readonly Button manual = new() { Padding = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock manualGlyph = new() { Width = 18, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock manualTitle = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock manualTimer = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, MinWidth = 72, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Button pause = new() { Content = "Pause", Padding = new Thickness(0, 4, 0, 4), Margin = new Thickness(14, 0, 0, 0), MinWidth = 52, HorizontalContentAlignment = HorizontalAlignment.Left };
    readonly Slider length = new() { Minimum = 0, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 1, Margin = new Thickness(0, 6, 0, 0) };
    readonly Grid lengthLabels = new() { Margin = new Thickness(0, 4, 0, 0), Height = 20 };
    readonly TextBlock lengthValue = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
    readonly TextBlock shadowKey = Key(), saveKey = Key(), saveKeyLabel = Small("");
    readonly Button source = new() { Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 0 };
    readonly TextBlock sourceTitle = new() { FontSize = 13 }, sourceDetail = Small("");
    readonly Popup sourcePopup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Top, VerticalOffset = -8 };
    readonly ComboBox quality = new() { MinWidth = 200 };
    readonly CheckBox desktop = new() { Content = "Desktop audio", FontSize = 12, Margin = new Thickness(0, 0, 16, 0) }, mic = new() { Content = "Microphone", FontSize = 12 };
    int[] choices = [];
    bool updating, dragging;
    CaptureView? last;
    public FrameworkElement Root => root;
    public Panel NotesHost => notes;
    public Decorator DisplayHost => displays;
    public ContentControl? HeroHost => hero;

    public SimpleCaptureLayout(ModeContext context, VisualMode mode)
    {
        commands = context.Commands; this.mode = mode;
        double radius = mode switch { VisualMode.Pink => 16, VisualMode.Emo => 2, _ => 4 };
        root.Margin = new Thickness(64, 30, 56, 46);
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var body = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        scroll.Content = body; root.Children.Add(scroll);

        hero.Margin = new Thickness(0, 0, 0, 22); hero.Visibility = Visibility.Collapsed; body.Children.Add(hero);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(dot); header.Children.Add(Eyebrow("SHADOW RECORDING")); body.Children.Add(header);

        // Main action: one plain button; the title never changes the button size (MinWidth fits the widest state).
        var shadowContent = new StackPanel(); shadowContent.Children.Add(shadowTitle);
        shadow.Content = shadowContent; Corner(shadow, radius);
        shadowTitle.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        shadow.Click += async (_, _) => await commands.ToggleShadow();
        AutomationName(shadow, "Start or stop shadow recording");
        body.Children.Add(shadow);
        loading.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(loading);
        var subtitle = Small("Automatically capture your gameplay in the background"); subtitle.Margin = new Thickness(2, 2, 0, 0); body.Children.Add(subtitle);

        // Replay length: horizontal, discrete (the same values as the Original GUI's minute slider), committed on release/keys.
        var lengthHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
        lengthHead.Children.Add(Eyebrow("REPLAY LENGTH")); lengthHead.Children.Add(lengthValue); body.Children.Add(lengthHead);
        var lengthBox = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Width = 560 };
        lengthBox.Children.Add(length); lengthBox.Children.Add(lengthLabels); body.Children.Add(lengthBox);
        AutomationName(length, "Replay length");
        length.ValueChanged += (_, _) => { if (!updating) { ShowLength(); if (!dragging) Commit(); } };
        length.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragging = true));
        length.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { dragging = false; Commit(); }));
        lengthValue.SetResourceReference(TextBlock.ForegroundProperty, "Text");

        // Save last … and the understated manual recording row (a text control, not a button face).
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };
        save.Content = saveTitle; Corner(save, radius); save.Click += async (_, _) => await commands.SaveReplay();
        ToolTipService.SetShowOnDisabled(save, true); actions.Children.Add(save);
        var manualRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var manualContent = new StackPanel { Orientation = Orientation.Horizontal }; manualContent.Children.Add(manualGlyph); manualContent.Children.Add(manualTitle);
        manual.Content = manualContent; Quiet(manual); manual.Click += async (_, _) => await commands.ToggleManual();
        AutomationName(manual, "Start or stop manual recording");
        Quiet(pause); pause.Click += async (_, _) => await commands.PauseResume();
        manualRow.Children.Add(manual); manualRow.Children.Add(manualTimer); manualRow.Children.Add(pause);
        manualTimer.SetResourceReference(TextBlock.ForegroundProperty, "Subtle");
        actions.Children.Add(manualRow); body.Children.Add(actions);

        // Shortcut rows: the same settings as Settings › Hotkeys (Change hotkey opens the editor on that field).
        var keys = new Grid { Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90 }); keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keys.RowDefinitions.Add(new RowDefinition()); keys.RowDefinitions.Add(new RowDefinition());
        void Row(int r, TextBlock label, TextBlock key, string action, string name)
        {
            label.Margin = new Thickness(0, 0, 18, 0); label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(label, r); keys.Children.Add(label); Grid.SetRow(key, r); Grid.SetColumn(key, 1); keys.Children.Add(key);
            var change = new Button { Content = "Change hotkey", FontSize = 11, Padding = new Thickness(8, 6, 8, 6) }; Quiet(change); AutomationName(change, name);
            change.Click += (_, _) => commands.ChangeHotkey(action); Grid.SetRow(change, r); Grid.SetColumn(change, 2); keys.Children.Add(change);
        }
        Row(0, Small("Hotkey for activating or deactivating shadow recording"), shadowKey, "Shadow", "Change the shadow recording on/off hotkey");
        Row(1, saveKeyLabel, saveKey, "Replay", "Change the save shadow recording hotkey");
        body.Children.Add(keys);
        notes.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(notes);

        // Bottom bar: display (cards in a popup), quality, audio.
        var bar = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(22, 14, 22, 14), Margin = new Thickness(0, 14, 0, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, "Panel"); bar.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetRow(bar, 1); root.Children.Add(bar);
        var cols = new Grid(); bar.Child = cols;
        // Display and audio keep their natural width; quality takes the rest (so nothing is clipped at the minimum window size).
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) cols.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var sourceText = new StackPanel(); sourceText.Children.Add(sourceTitle); sourceText.Children.Add(sourceDetail);
        var sourceRow = new StackPanel { Orientation = Orientation.Horizontal }; sourceRow.Children.Add(Glyph("▭")); sourceRow.Children.Add(sourceText); sourceRow.Children.Add(Glyph("⌄", 8));
        source.Content = sourceRow; Quiet(source); AutomationName(source, "Choose displays");
        source.Click += (_, _) => sourcePopup.IsOpen = !sourcePopup.IsOpen;
        var popupBody = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(16), Width = 400, CornerRadius = new CornerRadius(Math.Min(radius, 8)), Child = displays };
        popupBody.SetResourceReference(Border.BackgroundProperty, "Raised"); popupBody.SetResourceReference(Border.BorderBrushProperty, "Line");
        sourcePopup.Child = popupBody; sourcePopup.PlacementTarget = source;
        var sourceHost = new Grid { VerticalAlignment = VerticalAlignment.Center, MinWidth = 170 }; sourceHost.Children.Add(source); sourceHost.Children.Add(sourcePopup); cols.Children.Add(sourceHost);
        cols.Children.Add(Divider(1));
        foreach (var name in CaptureView.PresetNames) quality.Items.Add(new ComboBoxItem { Content = name, IsEnabled = name != CaptureView.PresetNames[3] });
        AutomationName(quality, "Video quality");
        quality.SelectionChanged += (_, _) => { if (!updating && quality.SelectedIndex is >= 0 and <= 2) commands.SetPreset(quality.SelectedIndex); };
        var qualityBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; qualityBox.Children.Add(Eyebrow("QUALITY")); quality.Margin = new Thickness(0, 6, 0, 0); qualityBox.Children.Add(quality);
        Grid.SetColumn(qualityBox, 2); cols.Children.Add(qualityBox);
        cols.Children.Add(Divider(3));
        var audio = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        audio.Children.Add(desktop); audio.Children.Add(mic); Grid.SetColumn(audio, 4); cols.Children.Add(audio);
        // Narrow windows (minimum size): the two audio checkboxes stack, so the quality choice keeps its full label.
        bar.SizeChanged += (_, e) => { bool narrow = e.NewSize.Width < 760; audio.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal; desktop.Margin = new Thickness(0, 0, narrow ? 0 : 16, narrow ? 4 : 0); };
        RoutedEventHandler audioChanged = (_, _) => { if (!updating) commands.SetAudio(desktop.IsChecked == true, mic.IsChecked == true); };
        desktop.Click += audioChanged; mic.Click += audioChanged;
        ToolTipService.SetShowOnDisabled(quality, true);
    }
    static Border Divider(int column) { var d = new Border { Width = 1, Margin = new Thickness(18, 4, 18, 4) }; d.SetResourceReference(Border.BackgroundProperty, "Line"); Grid.SetColumn(d, column); return d; }
    static TextBlock Glyph(string text, double left = 0) { var t = new TextBlock { Text = text, FontSize = 16, Margin = new Thickness(left, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return t; }
    static TextBlock Eyebrow(string text) { var t = new TextBlock { Text = string.Join(" ", text.ToCharArray()).Replace("   ", "     "), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return t; }
    static TextBlock Small(string text) { var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap }; t.SetResourceReference(TextBlock.ForegroundProperty, "Subtle"); return t; }
    static TextBlock Key() { var t = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }; t.SetResourceReference(TextBlock.ForegroundProperty, "Text"); return t; }
    static void AutomationName(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);
    // Text-only control: no face or edge; hover brightens the text; keyboard focus = the dotted underline used elsewhere.
    static void Quiet(Button b)
    {
        b.Background = Brushes.Transparent; b.BorderBrush = Brushes.Transparent; b.MinHeight = 0; b.Cursor = Cursors.Hand;
        b.SetResourceReference(Control.ForegroundProperty, "Subtle"); b.FocusVisualStyle = (Style)Application.Current.FindResource("QuietFocus");
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text"))); template.Triggers.Add(hover);
        var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; off.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); template.Triggers.Add(off);
        b.Template = template;
    }
    static void Corner(Button b, double radius)
    {
        // The app's Button template with this mode's corner radius (Pink: soft rounded; Emo: nearly square).
        b.Loaded += (_, _) => { if (b.Template?.FindName("B", b) is Border face) face.CornerRadius = new CornerRadius(radius); };
    }
    void ShowLength()
    {
        int i = (int)Math.Round(length.Value);
        if (i >= 0 && i < choices.Length) lengthValue.Text = Label(choices[i], true);
    }
    void Commit()
    {
        int i = (int)Math.Round(length.Value);
        if (i >= 0 && i < choices.Length && last != null && choices[i] != last.ReplaySeconds) commands.SetReplaySeconds(choices[i]);
    }
    static string Label(int seconds, bool unit) => seconds < 60 ? seconds + (unit ? " seconds" : "s") : $"{seconds / 60d:0.##}" + (unit ? seconds == 60 ? " minute" : " minutes" : "");

    public void Update(CaptureView v)
    {
        updating = true;
        try
        {
            last = v;
            shadowTitle.Text = v.ShadowTitle; shadow.IsEnabled = v.ShadowEnabled; shadow.ToolTip = v.ShadowTooltip; ToolTipService.SetShowOnDisabled(shadow, true);
            shadow.SetResourceReference(Control.BorderBrushProperty, v.ShadowActive ? "Recording" : "Line");
            shadow.BorderThickness = new Thickness(v.ShadowActive ? 2 : 1);
            shadowTitle.SetResourceReference(TextBlock.ForegroundProperty, v.ShadowActive ? "Recording" : "Text");
            // Status dot: grey off, ring while starting/stopping, solid while recording (the engine's real state).
            if (v.ShadowDot == "active") { dot.SetResourceReference(Shape.FillProperty, "Recording"); dot.Stroke = null; dot.Opacity = 1; }
            else if (v.ShadowDot == "transition") { dot.Fill = Brushes.Transparent; dot.SetResourceReference(Shape.StrokeProperty, "Recording"); dot.StrokeThickness = 1.5; dot.Opacity = .9; }
            else { dot.SetResourceReference(Shape.FillProperty, "Muted"); dot.Stroke = null; dot.Opacity = .55; }
            AutomationName(dot, v.ShadowDot == "active" ? "Shadow Recording is on" : v.ShadowDot == "off" ? "Shadow Recording is off" : "Shadow Recording is starting or stopping");

            if (!dragging && (!choices.SequenceEqual(v.ReplayChoices) || (int)Math.Round(length.Value) != Array.IndexOf(choices, v.ReplaySeconds)))
            {
                if (!choices.SequenceEqual(v.ReplayChoices))
                {
                    choices = [.. v.ReplayChoices]; length.Maximum = Math.Max(1, choices.Length - 1);
                    lengthLabels.Children.Clear(); lengthLabels.ColumnDefinitions.Clear();
                    for (int i = 0; i < choices.Length; i++)
                    {
                        var t = new TextBlock { Text = Label(choices[i], false), FontSize = 11, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == choices.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center };
                        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                        // Labels sit under their tick: tick i is at i/(n-1) of the track; each label is centred there.
                        t.RenderTransform = new TranslateTransform(); t.Tag = i; lengthLabels.Children.Add(t);
                    }
                    lengthLabels.SizeChanged -= PlaceLabels; lengthLabels.SizeChanged += PlaceLabels;
                }
                length.Value = Math.Max(0, Array.IndexOf(choices, v.ReplaySeconds));
                PlaceLabels(this, null!);
            }
            ShowLength();
            length.IsEnabled = v.ReplayLengthEnabled; length.ToolTip = v.ReplayLengthTooltip;

            saveTitle.Text = v.SaveTitle; save.IsEnabled = v.SaveEnabled; save.ToolTip = v.SaveTooltip;
            manualGlyph.Text = v.ManualActive ? "■" : "○"; manualTitle.Text = v.ManualTitle; manual.IsEnabled = v.ManualEnabled; manual.ToolTip = v.ManualTooltip;
            manualGlyph.SetResourceReference(TextBlock.ForegroundProperty, v.ManualActive && !v.ManualPaused ? "Recording" : "Muted");
            manualTimer.Text = v.ManualElapsed; pause.Content = v.PauseLabel;
            pause.Visibility = v.ManualActive ? Visibility.Visible : Visibility.Hidden; // Hidden: the row keeps its width
            shadowKey.Text = v.ShadowHotkey; saveKey.Text = v.SaveHotkey; saveKeyLabel.Text = v.SaveHotkeyLabel;
            sourceTitle.Text = v.SourceTitle; sourceDetail.Text = v.SourceDetail; source.ToolTip = v.SourceLocked ? "Stop capture before changing source." : "Choose which displays to capture";
            ToolTipService.SetShowOnDisabled(source, true);
            quality.SelectedIndex = v.PresetIndex; quality.IsEnabled = v.PresetEnabled; quality.ToolTip = v.PresetTooltip.Length > 0 ? v.PresetTooltip : null;
            desktop.IsChecked = v.DesktopAudio; mic.IsChecked = v.Microphone; desktop.IsEnabled = mic.IsEnabled = v.AudioEnabled;
            hero.Visibility = hero.Content == null ? Visibility.Collapsed : Visibility.Visible;
            if (shadow.MinWidth < 1) shadow.MinWidth = 330;
        }
        finally { updating = false; }
    }
    void PlaceLabels(object sender, SizeChangedEventArgs e)
    {
        double w = lengthLabels.ActualWidth, thumb = 12; if (w <= 0 || choices.Length < 2) return;
        foreach (TextBlock t in lengthLabels.Children)
        {
            int i = (int)t.Tag; t.HorizontalAlignment = HorizontalAlignment.Left; t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = thumb / 2 + (w - thumb) * i / (choices.Length - 1) - t.DesiredSize.Width / 2;
            ((TranslateTransform)t.RenderTransform).X = Math.Clamp(x, 0, w - t.DesiredSize.Width);
        }
    }
    public void ShowLoading(string text, bool visible) { loading.Text = visible ? text : ""; }
    public void Shown() { hero.Visibility = hero.Content == null ? Visibility.Collapsed : Visibility.Visible; }
    public void Hidden() => sourcePopup.IsOpen = false;
}
