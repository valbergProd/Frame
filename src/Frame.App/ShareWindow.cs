using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace Frame.App;

// Share / Export modal: choose a destination, adjust framing, export locally, then open the destination yourself.
// No accounts or uploads. Idle cost is nil: nothing runs until the window opens (one probe + one preview frame) or Export is pressed.
public sealed class ShareWindow : Window
{
    readonly string source, ffmpeg, cache;
    readonly Func<Settings> loadSettings;
    readonly Action<Settings> saveSettings;
    readonly Func<bool, Encoder> chooseEncoder;
    readonly Func<bool> captureBusy;
    MediaInfo? media;
    // Set by the Trim editor: the selected interval of the original file. Exported directly, without an intermediate file.
    readonly TimeRange? range;
    SharePreset preset;
    Framing framing;
    double focusX = .5, focusY = .5;
    int aspectW, aspectH, shortSide;
    ExportQuality quality;
    string exportFolder;
    CancellationTokenSource? running;
    string? exported;

    readonly UniformGrid tiles = new() { Columns = 5, Margin = new Thickness(0, 18, -10, 0) };
    readonly StackPanel framingRow = new() { Orientation = Orientation.Horizontal };
    readonly Border frame = new() { ClipToBounds = true, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
    readonly Canvas canvas = new();
    readonly Image back = new() { Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Performance } }, front = new() { Stretch = Stretch.Fill };
    readonly Shapes.Rectangle shade = new() { Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)) };
    readonly Slider position = new() { Minimum = 0, Maximum = 1, Value = .5, Margin = new Thickness(0, 6, 0, 0), SmallChange = .02, LargeChange = .1 };
    readonly TextBlock positionLabel = new() { Text = "Framing position · drag the preview", FontSize = 11, Margin = new Thickness(0, 14, 0, 0) };
    readonly TextBlock summary = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    readonly ComboBox aspectBox = Combo("Original", "16:9 landscape", "9:16 vertical", "1:1 square", "4:5 portrait");
    readonly ComboBox sizeBox = Combo("Original size", "720p", "1080p", "1440p", "2160p");
    readonly ComboBox qualityBox = Combo("High quality", "Balanced", "Smaller file");
    readonly TextBlock folderLabel = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBox customName = new(), customUrl = new();
    readonly StackPanel customPanel = new() { Margin = new Thickness(0, 14, 0, 0) };
    readonly ProgressBar bar = new() { Height = 2, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0) };
    readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
    readonly Button export = new() { Content = "Export", MinWidth = 140 }, cancel = new() { Content = "Cancel", Visibility = Visibility.Collapsed, MinWidth = 110 };
    readonly WrapPanel after = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
    static readonly int[] Aspects = [0, 0, 16, 9, 9, 16, 1, 1, 4, 5];
    static readonly int[] Sizes = [0, 720, 1080, 1440, 2160];

    public ShareWindow(Window owner, string source, string ffmpeg, string cache, Func<Settings> loadSettings, Action<Settings> saveSettings, Func<bool, Encoder> chooseEncoder, Func<bool> captureBusy, TimeRange? range = null)
    {
        this.source = source; this.ffmpeg = ffmpeg; this.cache = cache; this.loadSettings = loadSettings; this.saveSettings = saveSettings; this.chooseEncoder = chooseEncoder; this.captureBusy = captureBusy; this.range = range;
        var s = loadSettings();
        preset = SharePresets.Find(s.SharePreset); quality = s.ShareQuality; exportFolder = s.ShareExportDirectory;
        Owner = owner; Title = "FRAME · Crop for social"; Width = 860; Height = 700; MinWidth = 760; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Background");
        Content = FrameChrome.Wrap(this, Build(s), "Crop for social"); // FRAME title bar and caption buttons, like the main window
        ApplyPreset(preset, false);
        Loaded += (_, _) => Ready = Prepare();
        Closing += (_, e) => { if (running != null) { e.Cancel = true; status.Text = "Cancel the export before closing."; } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && running == null) Close(); };
    }

    static ComboBox Combo(params string[] items) { var box = new ComboBox { Margin = new Thickness(0, 6, 0, 0) }; foreach (var i in items) box.Items.Add(new ComboBoxItem { Content = i }); return box; }
    static TextBlock Muted(string text, double size = 11) { var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; t.SetResourceReference(ForegroundProperty, "Muted"); return t; }

    UIElement Build(Settings s)
    {
        var root = new Grid { Margin = new Thickness(32, 26, 32, 24) };
        var surface = new Border { Child = root }; surface.SetResourceReference(Border.BackgroundProperty, "Background");
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = h });
        var head = new StackPanel();
        var eyebrow = new TextBlock { Text = "C R O P   F O R   S O C I A L" }; eyebrow.SetResourceReference(StyleProperty, "Eyebrow"); head.Children.Add(eyebrow);
        head.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(source), FontSize = 20, FontWeight = FontWeights.Light, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (range != null) { var clip = Muted($"Selected clip · {TimeText.Format(range.Start)} – {TimeText.Format(range.End)} · {TimeText.Format(range.End - range.Start)}", 12); clip.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); clip.Margin = new Thickness(0, 2, 0, 2); head.Children.Add(clip); }
        head.Children.Add(Muted("Exports a new file beside your recording. The original is never changed."));
        root.Children.Add(head);

        foreach (var p in SharePresets.All) tiles.Children.Add(Tile(p));
        Grid.SetRow(tiles, 1); root.Children.Add(tiles);

        var body = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var stage = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(12) }; stage.SetResourceReference(Border.BackgroundProperty, "Surface");
        canvas.Children.Add(back); canvas.Children.Add(shade); canvas.Children.Add(front); frame.Child = canvas; stage.Child = frame;
        stage.SizeChanged += (_, _) => Layout();
        bool dragging = false; Point last = default;
        frame.MouseLeftButtonDown += (_, e) => { if (framing != Framing.Crop) return; dragging = true; last = e.GetPosition(frame); frame.CaptureMouse(); };
        frame.MouseMove += (_, e) => { if (!dragging || media == null) return; var now = e.GetPosition(frame); Drag(now.X - last.X, now.Y - last.Y); last = now; };
        frame.MouseLeftButtonUp += (_, _) => { dragging = false; frame.ReleaseMouseCapture(); };
        TooltipPolish.Describe(frame, "Preview of the exported framing");
        body.Children.Add(stage);

        var side = new StackPanel();
        side.Children.Add(Muted("FRAMING"));
        foreach (var (f, label, tip) in new[] { (Framing.Original, "Original", "Keep the recording's own shape"), (Framing.Crop, "Crop", "Fill the frame; drag to choose what stays"), (Framing.Fit, "Fit", "Show everything with dark bars"), (Framing.Blur, "Blur", "Show everything over a soft blurred fill") })
        {
            var b = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 6, 6, 0), MinHeight = 30, FontSize = 12, Tag = f };
            TooltipPolish.Describe(b, tip); b.Click += (_, _) => { framing = f; Refresh(); }; framingRow.Children.Add(b);
        }
        side.Children.Add(framingRow);
        positionLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        side.Children.Add(positionLabel); side.Children.Add(position);
        TooltipPolish.Describe(position, "Slide to shift the cropped area");
        position.ValueChanged += (_, _) => { if (media != null && Horizontal()) focusX = position.Value; else focusY = position.Value; Layout(); };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); side.Children.Add(summary);

        var format = new Expander { Header = "Format and destination", Margin = new Thickness(0, 18, 0, 0), FontSize = 12 }; format.SetResourceReference(ForegroundProperty, "Text");
        format.SetResourceReference(StyleProperty, "QuietExpander"); // same chevron ring and IsExpanded-based colours as Settings
        var fields = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        fields.Children.Add(Muted("Shape")); fields.Children.Add(aspectBox); TooltipPolish.Describe(aspectBox, "Choose the exported frame shape");
        fields.Children.Add(Muted("Resolution")); fields.Children.Add(sizeBox); TooltipPolish.Describe(sizeBox, "Short side of the exported video");
        fields.Children.Add(Muted("Quality")); fields.Children.Add(qualityBox); TooltipPolish.Describe(qualityBox, "Trade file size against detail");
        foreach (var t in fields.Children.OfType<TextBlock>()) t.Margin = new Thickness(0, 10, 0, 0);
        var folderButton = new Button { Content = "Choose export folder", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, Padding = new Thickness(12, 7, 12, 7) };
        TooltipPolish.Describe(folderButton, "Where exported files are saved"); folderButton.Click += (_, _) => ChooseFolder();
        folderLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); fields.Children.Add(folderButton); fields.Children.Add(folderLabel);
        customPanel.Children.Add(Muted("Destination name")); customPanel.Children.Add(customName);
        customPanel.Children.Add(Muted("Upload page address (https://…)")); customPanel.Children.Add(customUrl);
        foreach (var t in customPanel.Children.OfType<TextBlock>()) t.Margin = new Thickness(0, 8, 0, 4);
        customName.Text = s.ShareCustom?.Name ?? ""; customUrl.Text = s.ShareCustom?.Url ?? "";
        TooltipPolish.Describe(customName, "A short name for this destination"); TooltipPolish.Describe(customUrl, "Opened in your browser after export");
        fields.Children.Add(customPanel);
        format.Content = fields; side.Children.Add(format);
        aspectBox.SelectionChanged += (_, _) => { int i = aspectBox.SelectedIndex; if (i < 0) return; aspectW = Aspects[i * 2]; aspectH = Aspects[i * 2 + 1]; if (aspectW == 0) framing = Framing.Original; else if (framing == Framing.Original) framing = Framing.Fit; Refresh(); };
        sizeBox.SelectionChanged += (_, _) => { if (sizeBox.SelectedIndex >= 0) { shortSide = Sizes[sizeBox.SelectedIndex]; Refresh(); } };
        qualityBox.SelectionChanged += (_, _) => { if (qualityBox.SelectedIndex >= 0) { quality = (ExportQuality)qualityBox.SelectedIndex; Refresh(); } };
        var scroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(scroll, 2); body.Children.Add(scroll);
        Grid.SetRow(body, 2); root.Children.Add(body);

        var foot = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        bar.SetResourceReference(ForegroundProperty, "Accent"); bar.SetResourceReference(BackgroundProperty, "Line"); bar.Visibility = Visibility.Hidden; foot.Children.Add(bar);
        var line = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
        cancel.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(export); buttons.Children.Add(cancel);
        export.SetResourceReference(BorderBrushProperty, "Accent");
        TooltipPolish.Describe(export, "Create the prepared video file"); TooltipPolish.Describe(cancel, "Stop and remove the unfinished file");
        export.Click += async (_, _) => await Export(); cancel.Click += (_, _) => running?.Cancel();
        line.Children.Add(buttons); status.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(status);
        foot.Children.Add(line); foot.Children.Add(after);
        Grid.SetRow(foot, 3); root.Children.Add(foot);
        return surface;
    }

    Button Tile(SharePreset p)
    {
        var icon = new Shapes.Path { Data = Geometry.Parse(p.Id switch
        {
            "youtube" => "M 3,4 H 21 Q 23,4 23,6 V 16 Q 23,18 21,18 H 3 Q 1,18 1,16 V 6 Q 1,4 3,4 Z M 9.5,7.5 L 15.5,11 9.5,14.5 Z",
            "shorts" => "M 7,1 H 17 Q 19,1 19,3 V 19 Q 19,21 17,21 H 7 Q 5,21 5,19 V 3 Q 5,1 7,1 Z M 10,8 L 15,11 10,14 Z",
            "tiktok" => "M 13,2 V 14.5 A 3.5,3.5 0 1 1 9.5,11 M 13,2 Q 14,6 18,6.5",
            "discord" => "M 4,5 Q 12,1.5 20,5 Q 22.5,10.5 21.5,16 Q 18.5,18.5 16,19 L 15,17 Q 12,18 9,17 L 8,19 Q 5.5,18.5 2.5,16 Q 1.5,10.5 4,5 Z M 9,10.5 A 1.3,1.5 0 1 0 9,10.6 M 15,10.5 A 1.3,1.5 0 1 0 15,10.6",
            _ => "M 12,4 V 20 M 4,12 H 20"
        }), StrokeThickness = 1.4, Width = 26, Height = 22, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        icon.SetResourceReference(Shapes.Shape.StrokeProperty, "Accent");
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(icon); icon.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, Margin = new Thickness(0, 10, 0, 2) });
        stack.Children.Add(Muted(p.Hint, 10));
        var tile = new Button { Content = stack, Height = 104, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 12, 12, 10), HorizontalContentAlignment = HorizontalAlignment.Left, Tag = p.Id };
        AutomationName(tile, "Format for " + p.Name);
        TooltipPolish.Describe(tile, p.Id switch
        {
            "youtube" => "Prepare a landscape video for YouTube",
            "shorts" => "Prepare a vertical clip for Shorts",
            "tiktok" => "Prepare a vertical clip for TikTok",
            "discord" => "Keep the original file for chat",
            _ => "Choose your own format and destination"
        });
        tile.Click += (_, _) => { if (running == null) ApplyPreset(p, true); };
        return tile;
    }
    static void AutomationName(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    void ApplyPreset(SharePreset p, bool fromUser)
    {
        preset = p; framing = p.Framing; aspectW = p.AspectW; aspectH = p.AspectH; shortSide = p.ShortSide; focusX = focusY = .5;
        aspectBox.SelectedIndex = Math.Max(0, Array.FindIndex(Enumerable.Range(0, Aspects.Length / 2).ToArray(), i => Aspects[i * 2] == aspectW && Aspects[i * 2 + 1] == aspectH));
        sizeBox.SelectedIndex = Math.Max(0, Array.IndexOf(Sizes, shortSide));
        qualityBox.SelectedIndex = (int)quality;
        framing = p.Framing; // aspect selection above may have adjusted it
        customPanel.Visibility = p == SharePresets.Custom ? Visibility.Visible : Visibility.Collapsed;
        exported = null; after.Visibility = Visibility.Collapsed;
        if (fromUser) status.Text = "";
        Refresh();
    }

    // Diagnostics hooks for --ui-test.
    internal Task Ready { get; private set; } = Task.CompletedTask;
    internal bool HasMedia => media != null;
    internal string Summary => summary.Text;
    internal void Choose(string id) => ApplyPreset(SharePresets.Find(id), true);
    internal void SetFraming(Framing value) { framing = value; Refresh(); }

    async Task Prepare()
    {
        status.Text = "Reading recording…"; export.IsEnabled = false;
        try
        {
            media = await ShareExportJob.Probe(ffmpeg, source);
            string? still = await ShareExportJob.PreviewFrame(ffmpeg, source, range != null ? (range.Start + range.End) / 2 : Math.Min(media.Duration * .3, 5), cache);
            if (still != null)
            {
                var image = await Task.Run(() => { var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(still); b.EndInit(); b.Freeze(); return b; });
                back.Source = front.Source = image;
            }
            status.Text = still == null ? "Preview unavailable; export still works." : "";
            export.IsEnabled = true; Refresh();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or System.Text.Json.JsonException or KeyNotFoundException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            status.Text = e.Message; export.IsEnabled = false;
        }
    }

    bool Horizontal()
    {
        if (media == null) return true;
        var (w, h) = Output();
        return (double)media.Width / media.Height >= (double)w / h;
    }
    (int W, int H) Output() => media == null ? (16, 9) : ExportPlanner.OutputSize(media, aspectW, aspectH, shortSide);

    void Refresh()
    {
        foreach (var b in tiles.Children.OfType<Button>()) Select(b, (string)b.Tag == preset.Id);
        bool shaped = aspectW > 0;
        foreach (var b in framingRow.Children.OfType<Button>()) { var f = (Framing)b.Tag; b.IsEnabled = shaped ? f != Framing.Original : f == Framing.Original; Select(b, f == framing); }
        bool crop = framing == Framing.Crop;
        position.Visibility = positionLabel.Visibility = crop ? Visibility.Visible : Visibility.Collapsed;
        position.Value = Horizontal() ? focusX : focusY;
        folderLabel.Text = "Saved to: " + (exportFolder.Length > 0 ? exportFolder : "the recording's folder");
        if (media != null)
        {
            var plan = ExportPlanner.Plan(Request());
            summary.Text = $"{plan.Width} × {plan.Height} · {plan.Fps:0.##} FPS · {(plan.StreamCopy ? "copied without re-encoding" : "H.264 / AAC MP4")}" + (media.HasAudio ? "" : " · no audio in source");
        }
        Layout();
    }
    void Select(Button b, bool on)
    {
        if (on) { b.SetResourceReference(BorderBrushProperty, "Accent"); b.SetResourceReference(BackgroundProperty, "Raised"); b.BorderThickness = new Thickness(1.5); }
        else { b.SetResourceReference(BorderBrushProperty, "Line"); b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(1); }
    }

    // Mirrors ExportPlanner.Filter exactly: Fit/Blur scale-to-fit centred, Crop scale-to-cover offset by focus.
    void Layout()
    {
        if (frame.Parent is not Border stage || stage.ActualWidth <= 0) return;
        var (w, h) = Output();
        double room = stage.ActualWidth - 24, tall = stage.ActualHeight - 24;
        double scale = Math.Min(room / w, tall / h); if (scale <= 0 || !double.IsFinite(scale)) return;
        double fw = w * scale, fh = h * scale; frame.Width = fw; frame.Height = fh; canvas.Width = fw; canvas.Height = fh;
        double sw = media?.Width ?? 16, sh = media?.Height ?? 9;
        double cover = Math.Max(fw / sw, fh / sh), contain = Math.Min(fw / sw, fh / sh);
        bool crop = framing == Framing.Crop && aspectW > 0, blur = framing == Framing.Blur && aspectW > 0;
        back.Visibility = shade.Visibility = blur ? Visibility.Visible : Visibility.Collapsed;
        if (blur) { Place(back, -sw * cover / 2 + fw / 2, -sh * cover / 2 + fh / 2, sw * cover, sh * cover); Place(shade, 0, 0, fw, fh); }
        if (crop)
        {
            var (ox, oy) = ExportPlanner.CropOffset(sw, sh, fw, fh, focusX, focusY);
            Place(front, -ox, -oy, sw * cover, sh * cover);
        }
        else Place(front, (fw - sw * contain) / 2, (fh - sh * contain) / 2, sw * contain, sh * contain);
    }
    static void Place(FrameworkElement e, double x, double y, double w, double h) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); e.Width = Math.Max(0, w); e.Height = Math.Max(0, h); }
    void Drag(double dx, double dy)
    {
        if (media == null) return;
        double fw = frame.Width, fh = frame.Height, cover = Math.Max(fw / media.Width, fh / media.Height);
        double overX = media.Width * cover - fw, overY = media.Height * cover - fh;
        if (overX > .5) focusX = Math.Clamp(focusX - dx / overX, 0, 1);
        if (overY > .5) focusY = Math.Clamp(focusY - dy / overY, 0, 1);
        position.Value = Horizontal() ? focusX : focusY; Layout();
    }

    ExportRequest Request()
    {
        string folder = exportFolder.Length > 0 ? exportFolder : Path.GetDirectoryName(source)!;
        return new(source, media!, aspectW, aspectH, shortSide, aspectW > 0 ? framing : Framing.Original, focusX, focusY, quality, preset.MaxFps, preset.CopyWhenPossible, folder, range);
    }

    void ChooseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where exported videos are saved", UseDescriptionForTitle = true, SelectedPath = exportFolder.Length > 0 ? exportFolder : Path.GetDirectoryName(source)! };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { exportFolder = dialog.SelectedPath; Refresh(); }
    }

    string? DestinationUrl() => preset == SharePresets.Custom ? (ShareDestinations.ValidUrl(customUrl.Text.Trim()) ? customUrl.Text.Trim() : null) : preset.Url;

    async Task Export()
    {
        if (media == null || running != null) return;
        CustomDestination? custom = null;
        if (preset == SharePresets.Custom && (customName.Text.Trim().Length > 0 || customUrl.Text.Trim().Length > 0))
        {
            custom = new(customName.Text.Trim(), customUrl.Text.Trim(), preset.Id);
            if (custom.Name.Length is 0 or > 40 || !ShareDestinations.ValidUrl(custom.Url)) { status.Text = "Give the destination a short name and an http(s) address, or leave both empty."; return; }
        }
        var request = Request(); var plan = ExportPlanner.Plan(request);
        if (!plan.StreamCopy && captureBusy()) { status.Text = "Stop recording before re-encoding, so capture keeps full performance. Discord-style copies still work."; return; }
        var s = loadSettings(); s.SharePreset = preset.Id; s.ShareQuality = quality; s.ShareExportDirectory = exportFolder; if (preset == SharePresets.Custom) s.ShareCustom = custom;
        try { saveSettings(s); } catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { status.Text = "Preferences were not saved: " + e.Message; }

        running = new(); after.Visibility = Visibility.Collapsed; exported = null;
        export.IsEnabled = false; cancel.Visibility = Visibility.Visible; bar.Visibility = Visibility.Visible; bar.Value = 0; tiles.IsEnabled = false;
        var progress = new Progress<(ExportStage Stage, double Fraction)>(p =>
        {
            bar.Value = p.Fraction;
            status.Text = p.Stage switch { ExportStage.Preparing => "Preparing…", ExportStage.Encoding => plan.StreamCopy ? $"Copying · {p.Fraction:P0}" : $"Encoding · {p.Fraction:P0}", ExportStage.Finalizing => "Checking the exported file…", _ => status.Text };
        });
        try
        {
            string label = preset == SharePresets.Custom && custom != null ? custom.Name : preset.Name;
            exported = await ShareExportJob.Run(ffmpeg, request, chooseEncoder(true), Path.GetFileNameWithoutExtension(source).Replace("FRAME ", "") + " · " + label + (range != null ? " clip" : ""), progress, running.Token);
            status.Text = "Ready · " + Path.GetFileName(exported); bar.Value = 1; ShowAfter();
        }
        catch (OperationCanceledException) { status.Text = "Export cancelled. The unfinished file was removed."; bar.Visibility = Visibility.Hidden; }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        { status.Text = "Export failed · " + e.Message; bar.Visibility = Visibility.Hidden; DiagnosticLog.Error("crop-for-social", "Export failed: " + e.Message, e); }
        finally { running.Dispose(); running = null; export.IsEnabled = true; cancel.Visibility = Visibility.Collapsed; tiles.IsEnabled = true; }
    }

    void ShowAfter()
    {
        after.Children.Clear();
        void Add(string text, string tip, Action action) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), FontSize = 12, Padding = new Thickness(12, 7, 12, 7) }; TooltipPolish.Describe(b, tip); b.Click += (_, _) => { try { action(); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.ExternalException) { status.Text = "That action failed: " + e.Message; } }; after.Children.Add(b); }
        string? url = DestinationUrl();
        if (url != null) Add(preset == SharePresets.Discord ? "Open Discord" : "Open upload page", "Open the site, then drag in the file", () => { OpenDestination(url); Reveal(); status.Text = "Drag the selected file from Explorer into the page, or choose it in the site's file picker."; });
        Add("Show in folder", "Select the file in File Explorer", Reveal);
        Add("Copy path", "Copy the file's full path", () => { Clipboard.SetText(exported!); status.Text = "Path copied."; });
        Add("Copy file", "Copy the file to paste in Explorer or chat", () => { Clipboard.SetFileDropList([exported!]); status.Text = "File copied. Paste it into a chat or folder."; });
        after.Visibility = Visibility.Visible;
    }
    void Reveal() => Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + exported + "\"", UseShellExecute = true });
    void OpenDestination(string url)
    {
        // Discord: the installed app when it registered its protocol, else the web app.
        if (preset == SharePresets.Discord) using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("discord")) if (key != null) { Process.Start(new ProcessStartInfo("discord://-/channels/@me") { UseShellExecute = true }); return; }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
