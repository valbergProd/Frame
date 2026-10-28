using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace Frame.App;

// Pink mode's world (0.6.0): an illustrated pink background (one of five supplied paintings, or the programmatic Simply
// Pink), a soft readability wash behind FRAME's content column, title row and bottom bar, the effect layers in z-order
// (ribbons, flowers, sakura, hearts, dogs, butterflies, sparkles) and three hidden secrets. One PinkWorld + PinkStage is
// shared by all layers (one wind, the heart swirl, published things, signals), so the effects interact.
// Tools (top right, under the mode buttons): a background swatch that opens a thumbnail picker (exactly one background),
// and seven small effect icons (any combination; neon pink = on, faint pink = off). No tooltips anywhere: discovery by
// clicking; screen readers get AutomationProperties names and the ToggleButton/RadioButton state. Choices are saved at
// once (Settings.PinkBackground / PinkEffects; first entry: Sakura garden + Sakura).
// Lifecycle: work only from CaptureAtmosphere's clock; WantsFrames only while an effect is on (or a secret's short event
// runs) and the Capture page is shown. Library/Settings keep a still frame under the host's scrim. Leave() stops all
// layers, clears published things and drops the decoded images (background and thumbnails).
// Diagnostics (only read from the command line, never saved): --pink-effects all|none|a,b  --pink-background <key>
// --pink-picker-open  --pink-swirl <scene s>  --pink-secret heart@2,treat@4,wish@6  --pink-fake-dog
// --pink-report <json>  --pink-stats <json>  --pink-bench <seconds> <json>.
sealed class PinkScene : IModeScene
{
    static readonly (string Key, string Name)[] Backgrounds = [("sakura-garden", "Sakura garden"), ("pink-1", "Rebel skyline"), ("pink-2", "Crystal palace"), ("pink-3", "Enchanted valley"), ("pink-4", "Rose city"), (PinkOptions.SimplyPink, "Simply Pink")];
    static readonly string[] EffectNames = ["Hearts", "Flowers", "Sakura petals", "Sparkles", "Butterflies", "Ribbons and bows", "Dogs"];
    const double Sidebar = 190; // MainWindow's sidebar column (its empty middle hosts the hidden heart)
    const int ImageWidth = 1672, ImageHeight = 941;
    readonly ModeContext context;
    readonly string[] args = Environment.GetCommandLineArgs();
    readonly PinkWorld world = new();
    readonly PinkWind wind = new(1);
    readonly PinkStage stage;
    readonly Grid root = new() { ClipToBounds = true }; // no background: only the secrets are hit-testable
    readonly PinkBackdrop backdrop = new();
    readonly PinkWash wash = new();
    readonly SakuraLayer sakura; readonly FlowersLayer flowers; readonly HeartsLayer hearts; readonly SparklesLayer sparkles; readonly ButterfliesLayer butterflies; readonly RibbonsLayer ribbons;
    readonly IPinkLayer dogs = new PinkDogsLayer();
    readonly IPinkLayer[] layers;   // z-order, back to front
    readonly PinkSecrets secrets;
    readonly SwirlGlow swirlGlow = new();
    // Tools
    readonly StackPanel tools = new() { HorizontalAlignment = HorizontalAlignment.Right };
    readonly ToggleButton pickerButton;
    readonly Border swatch = new() { Width = 32, Height = 20, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.2) };
    readonly Border picker;
    readonly List<(ToggleButton Button, Action Paint)> effectButtons = [];
    readonly List<(RadioButton Button, Border Thumb, Action Paint)> choices = [];
    bool entered, capturePage = true, syncing, thumbsLoaded, layoutPending, decodePending, clipStarted, wasBusy;
    string background = PinkOptions.FirstBackground;
    BitmapSource? image;
    int decodedWidth;
    double slow, pending, secretsDt, decodeMs;
    long lastMove;
    // Diagnostics
    readonly bool animation;
    readonly List<(string Name, double At)> forcedSecrets = [];
    double forcedSwirl = -1;
    PinkThing? fakeDog; readonly MatrixTransform fakeDogPose = new(); FrameworkElement? fakeDogMark; double dogX = -1, dogDir = 1, dogJump, dogBark = 6;
    long advanceTicks, advanceCalls;
    bool bench;
    readonly long[] layerBytes = new long[7];
    List<object>? trace;

    public PinkScene(ModeContext context)
    {
        this.context = context;
        stage = new PinkStage(world, wind);
        world.Wind = (p, _) => wind.At(p.X, p.Y);
        sakura = new(stage); flowers = new(stage); hearts = new(stage); sparkles = new(stage); butterflies = new(stage); ribbons = new(stage);
        layers = [ribbons, flowers, sakura, hearts, dogs, butterflies, sparkles];
        secrets = new PinkSecrets(stage);
        secrets.Started += () => { context.Atmosphere.Refresh(); if (!Running) Present(); };
        root.Children.Add(backdrop); root.Children.Add(wash);
        foreach (var layer in layers) { root.Children.Add(layer.Visual); if (layer == ribbons) root.Children.Add(swirlGlow); }
        root.Children.Add(secrets);
        root.SizeChanged += (_, _) => Sized();
        animation = args.Contains("--render-animation");
        if (Arg("--pink-swirl") is string swirlAt) forcedSwirl = double.Parse(swirlAt, CultureInfo.InvariantCulture);
        if (Arg("--pink-secret") is string list)
            foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries)) { var parts = item.Split('@'); forcedSecrets.Add((parts[0], parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 1)); }

        // Tools: [swatch ▾] | heart flower sakura sparkle butterfly bow dog, and the picker panel below.
        var focus = Application.Current?.Dispatcher.CheckAccess() == true ? Application.Current.TryFindResource("QuietFocus") as Style : null; // (the bench harness runs on its own thread)
        var chevron = new Path { Data = Geometry.Parse("M 0,0 L 3.5,3.5 L 7,0"), Stroke = PinkArt.Muted, StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Margin = new Thickness(5, 1, 1, 0), VerticalAlignment = VerticalAlignment.Center };
        var pickerFace = new StackPanel { Orientation = Orientation.Horizontal }; pickerFace.Children.Add(swatch); pickerFace.Children.Add(chevron);
        pickerButton = new ToggleButton { Content = pickerFace, Template = Bare(typeof(ToggleButton)), Padding = new Thickness(3, 2, 3, 2), Cursor = Cursors.Hand, FocusVisualStyle = focus, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(pickerButton, "Pink mode background"); ToolTipService.SetIsEnabled(pickerButton, false);
        void PaintPicker() { swatch.BorderBrush = pickerButton.IsChecked == true ? PinkArt.NeonBrush : pickerButton.IsMouseOver || pickerButton.IsKeyboardFocused ? Edge : Line; chevron.Stroke = pickerButton.IsChecked == true ? PinkArt.NeonBrush : PinkArt.Muted; }
        pickerButton.Checked += (_, _) => { PaintPicker(); OpenPicker(); }; pickerButton.Unchecked += (_, _) => { PaintPicker(); ClosePicker(); };
        pickerButton.MouseEnter += (_, _) => PaintPicker(); pickerButton.MouseLeave += (_, _) => PaintPicker(); pickerButton.IsKeyboardFocusedChanged += (_, _) => PaintPicker(); PaintPicker();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(pickerButton); row.Children.Add(new Border { Width = 1, Height = 18, Background = Line, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        for (int i = 0; i < PinkOptions.Effects.Length; i++) row.Children.Add(EffectButton(i, focus));
        tools.Children.Add(new Border { Child = row, Background = PillBrush, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Right });
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (key, name) in Backgrounds) grid.Children.Add(Choice(key, name, focus));
        picker = new Border { Child = grid, Background = PanelBrush, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(7), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
        tools.Children.Add(picker);
        tools.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && pickerButton.IsChecked == true) { pickerButton.IsChecked = false; pickerButton.Focus(); e.Handled = true; } };
    }
    static readonly SolidColorBrush Line = PinkArt.Solid(0x99E79BC0), Edge = PinkArt.Solid(0xFFE79BC0), PillBrush = PinkArt.Solid(0xE6FFF4F9), PanelBrush = PinkArt.Solid(0xF7FFF5F9);
    static readonly RadialGradientBrush GlowBrush = MakeGlow();
    static RadialGradientBrush MakeGlow() { var b = new RadialGradientBrush(PinkArt.C(0x80FF2E97), PinkArt.C(0x00FF2E97)); b.Freeze(); return b; }
    static ControlTemplate Bare(Type type)
    {
        var template = new ControlTemplate(type);
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter); template.VisualTree = border; template.Seal(); return template;
    }
    // Effect icon: an illustrated glyph; on = neon pink with a soft glow, off = faint translucent pink. Hover/focus only
    // brighten the faint glyph a little (no tooltip, no text).
    ToggleButton EffectButton(int index, Style? focus)
    {
        string key = PinkOptions.Effects[index];
        var glow = new Ellipse { Width = 26, Height = 26, Fill = GlowBrush, Opacity = 0, IsHitTestVisible = false };
        var glyph = new Path { Data = PinkArt.Icons[index], Width = 16, Height = 16, Stretch = Stretch.None, Fill = PinkArt.NeonBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Grid { Width = 27, Height = 27 }; face.Children.Add(glow); face.Children.Add(glyph);
        var b = new ToggleButton { Content = face, Template = Bare(typeof(ToggleButton)), Padding = new Thickness(0), Cursor = Cursors.Hand, FocusVisualStyle = focus, Tag = key };
        AutomationProperties.SetName(b, EffectNames[index] + " effect"); ToolTipService.SetIsEnabled(b, false);
        void Paint() { bool on = b.IsChecked == true; glow.Opacity = on ? 1 : 0; glyph.Opacity = on ? 1 : b.IsMouseOver || b.IsKeyboardFocused ? .58 : .32; }
        b.Checked += (_, _) => { Paint(); Toggled(key, true); }; b.Unchecked += (_, _) => { Paint(); Toggled(key, false); };
        b.MouseEnter += (_, _) => Paint(); b.MouseLeave += (_, _) => Paint(); b.IsKeyboardFocusedChanged += (_, _) => Paint();
        Paint(); effectButtons.Add((b, Paint)); return b;
    }
    // Background choice: a rounded thumbnail (decoded at 160 px when the picker first opens) and its name; the active one
    // has a neon frame and a check badge. Exactly one is checked (RadioButtons in one group).
    RadioButton Choice(string key, string name, Style? focus)
    {
        var thumb = new Border { Width = 104, Height = 58, CornerRadius = new CornerRadius(7), Background = PinkArt.Solid(0xFFF7CFE1) };
        var badge = new Grid { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
        badge.Children.Add(new Ellipse { Fill = PinkArt.NeonBrush, Stroke = Brushes.White, StrokeThickness = 1.2 });
        badge.Children.Add(new Path { Data = Geometry.Parse("M 4,7.6 L 6.6,10 L 11,5"), Stroke = Brushes.White, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        var inner = new Grid(); inner.Children.Add(thumb); inner.Children.Add(badge);
        var frame = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), Padding = new Thickness(1), Child = inner };
        var caption = new TextBlock { Text = name, FontSize = 10.5, Margin = new Thickness(3, 3, 0, 1) };
        var item = new StackPanel(); item.Children.Add(frame); item.Children.Add(caption);
        var b = new RadioButton { Content = item, GroupName = "pink-background-" + RuntimeHelpersId(), Template = Bare(typeof(RadioButton)), Padding = new Thickness(0), Margin = new Thickness(3), Cursor = Cursors.Hand, FocusVisualStyle = focus, Tag = key };
        AutomationProperties.SetName(b, name + " background"); ToolTipService.SetIsEnabled(b, false);
        void Paint()
        {
            bool on = b.IsChecked == true;
            frame.BorderBrush = on ? PinkArt.NeonBrush : b.IsMouseOver || b.IsKeyboardFocused ? Edge : Brushes.Transparent;
            badge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            caption.Foreground = on ? PinkArt.Plum : PinkArt.Muted; caption.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        b.Checked += (_, _) => { Paint(); if (!syncing) ChooseBackground(key); }; b.Unchecked += (_, _) => Paint();
        b.MouseEnter += (_, _) => Paint(); b.MouseLeave += (_, _) => Paint(); b.IsKeyboardFocusedChanged += (_, _) => Paint();
        Paint(); choices.Add((b, thumb, Paint)); return b;
    }
    int RuntimeHelpersId() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public FrameworkElement Root => root;
    public FrameworkElement? Tools => tools;
    public FrameworkElement? Hero => null;
    bool AnyEffect { get { foreach (var l in layers) if (l.Enabled) return true; return false; } }
    public bool WantsFrames => entered && capturePage && (AnyEffect || secrets.Busy || fakeDog != null);
    public bool FullRate { get { if (!entered || !capturePage) return false; if (secrets.Busy || fakeDog != null) return true; foreach (var l in layers) if (l.Enabled && l.FullRate) return true; return false; } }
    bool Running => context.Atmosphere.Running && WantsFrames;
    string? Arg(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

    public void Enter()
    {
        entered = true; capturePage = true; clipStarted = false;
        Restart();
        var saved = context.Settings;
        background = Arg("--pink-background") is string bg && PinkOptions.Backgrounds.Contains(bg) ? bg : PinkOptions.Background(saved.PinkBackground);
        string[] active = Arg("--pink-effects") switch { "all" => PinkOptions.Effects, "none" => [], string list => PinkOptions.Effects.Where(list.Split(',').Contains).ToArray(), null => PinkOptions.ActiveEffects(saved.PinkEffects) };
        syncing = true;
        foreach (var (button, paint) in effectButtons) { button.IsChecked = active.Contains((string)button.Tag); paint(); }
        foreach (var (button, _, paint) in choices) { button.IsChecked = (string)button.Tag == background; paint(); }
        syncing = false;
        foreach (var layer in layers) layer.Enabled = active.Contains(layer.Effect);
        if (args.Contains("--pink-fake-dog")) StartFakeDog();
        Rebudget();
        LoadBackground(background);
        context.Window.PreviewMouseMove += PointerMoved; context.Window.MouseLeave += PointerLeft;
        Sized(); RefreshLayoutSoon();
        if (args.Contains("--pink-picker-open")) pickerButton.IsChecked = true;
        if (!bench && Arg("--pink-stats") is string stats) { Application.Current.Exit += (_, _) => WriteStats(stats); if (Arg("--pink-trace") != null) trace = []; }
        Present();
        if (!bench && Array.IndexOf(args, "--pink-bench") is int b and >= 0 && b + 2 < args.Length) StartBench(double.Parse(args[b + 1], CultureInfo.InvariantCulture), args[b + 2], context.Seed);
    }
    // Fresh deterministic state (also used when a --render-animation clip starts, so clips are reproducible).
    void Restart()
    {
        int seed = context.Seed ?? Environment.TickCount;
        world.Random = new Random(seed); wind.Reset(seed); world.Time = 0; slow = pending = secretsDt = 0; world.Gust = 0;
        stage.Swirl.Stop(); stage.Swirl.NextAt = -1; secrets.Reset();
        foreach (var layer in layers) if (layer.Enabled) { layer.Enabled = false; layer.Enabled = true; }
        if (fakeDog != null) { world.Things.Remove(fakeDog); world.Things.Add(fakeDog); dogX = -1; dogJump = 0; dogBark = 6; }
    }
    public void Leave()
    {
        entered = false;
        pickerButton.IsChecked = false; ReleaseThumbnails();
        foreach (var layer in layers) layer.Enabled = false;
        secrets.Reset(); secrets.Place(null, null, null);
        if (fakeDogMark != null) { root.Children.Remove(fakeDogMark); fakeDogMark = null; fakeDog = null; }
        image = null; decodedWidth = 0; backdrop.Show(null, simply: true); swatch.Background = null;
        context.Window.PreviewMouseMove -= PointerMoved; context.Window.MouseLeave -= PointerLeft;
        world.Things.Clear(); world.Pointer = null; stage.Swirl.Stop();
    }
    public void PageChanged(bool capture)
    {
        capturePage = capture;
        secrets.Visibility = capture ? Visibility.Visible : Visibility.Hidden; // secrets only on the Capture page (and never over other pages' UI)
        RefreshLayoutSoon(); context.Atmosphere.Refresh();
        if (!Running) Present();
    }

    public void Advance(double dt)
    {
        if (!entered) return;
        // --render-animation: ignore the real clock until the manual clip clock starts, then begin from a seeded state.
        if (animation) { if (context.Atmosphere.Running) return; if (!clipStarted) { clipStarted = true; Restart(); } }
        // Full-rate calls are capped at ~60–75 steps/s (high-refresh displays call every composition frame, up to 240/s).
        pending += dt; if (dt > 0 && pending < 1 / 75d) return; dt = pending; pending = 0;
        long t0 = Stopwatch.GetTimestamp();
        world.Time += dt; wind.Advance(dt); world.Gust = wind.Gust(world.Width * .5);
        stage.PointerVelocity *= Math.Exp(-dt * 5);
        for (int i = forcedSecrets.Count - 1; i >= 0; i--) if (world.Time >= forcedSecrets[i].At) { secrets.Trigger(forcedSecrets[i].Name); forcedSecrets.RemoveAt(i); }
        ScheduleSwirl();
        stage.Swirl.Advance(dt);
        if (fakeDog != null) FakeDog(dt);
        // Particle layers step at ~30/s; full-rate layers (butterflies, dogs) every frame.
        slow += dt; bool step = !FullRate || slow >= 1 / 40d;
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i]; if (!layer.Enabled || !layer.FullRate && !step) continue;
            long bytes = bench ? GC.GetAllocatedBytesForCurrentThread() : 0;
            layer.Advance(layer.FullRate ? dt : slow, world);
            if (bench) layerBytes[i] += GC.GetAllocatedBytesForCurrentThread() - bytes;
        }
        if (step) slow = 0;
        bool busy = secrets.Busy;
        secretsDt += dt; if (busy || step) { secrets.Advance(secretsDt); secretsDt = 0; } // idle twinkles at the particle rate
        swirlGlow.Show(stage.Swirl);
        if (trace != null && step) Trace();
        if (wasBusy && !busy && !AnyEffect) context.Window.Dispatcher.BeginInvoke(DispatcherPriority.Background, context.Atmosphere.Refresh); // event over: let the clock stop
        wasBusy = busy;
        advanceTicks += Stopwatch.GetTimestamp() - t0; advanceCalls++;
    }
    public void Present()
    {
        if (!entered) return;
        foreach (var layer in layers) if (layer.Enabled) layer.Present(world);
        secrets.Present();
    }

    // Flowers + Hearts: a heart-shaped swirl every 1–2 minutes (first after 40–75 s), in the free area right of the content.
    void ScheduleSwirl()
    {
        var swirl = stage.Swirl;
        if (!flowers.Enabled || !hearts.Enabled) { swirl.NextAt = -1; if (swirl.Active) swirl.Stop(); return; }
        if (swirl.NextAt < 0) swirl.NextAt = world.Time + (forcedSwirl >= 0 ? Math.Max(0, forcedSwirl - world.Time) : stage.Range(40, 75));
        if (swirl.Active || world.Time < swirl.NextAt) return;
        int k = Math.Min(12, Math.Min(flowers.Available, hearts.Available));
        if (k >= 3)
        {
            var f = stage.Free; double r; Point centre;
            if (f.Width >= 220 && f.Height >= 200) { r = Math.Min(110, Math.Min(f.Width, f.Height) * .36); centre = new Point(f.X + f.Width * .5, f.Y + f.Height * .5); }
            else { r = 90; centre = new Point(world.Width * .62, world.Height * .45); }
            swirl.Start(centre, r, k); world.Raise("heart-swirl", centre);
        }
        swirl.NextAt = world.Time + stage.Range(60, 120);
    }

    void Toggled(string key, bool on)
    {
        if (syncing || !entered) return;
        var layer = layers.First(l => l.Effect == key);
        layer.Enabled = on; Rebudget();
        context.Update(s => { var active = PinkOptions.ActiveEffects(s.PinkEffects); if (active.Contains(key) != on) active = PinkOptions.Toggle(active, key); s.PinkEffects = active; });
        context.Atmosphere.Refresh();
        if (!Running) Present();
    }
    // Particle budget: one or two effects get the full counts; with more, each effect gets fewer (all seven: ~58 %).
    void Rebudget()
    {
        int n = 0; foreach (var l in layers) if (l.Enabled) n++;
        stage.Load = n <= 2 ? 1 : n == 3 ? .85 : n == 4 ? .75 : n == 5 ? .66 : .58;
        stage.SparklesOn = sparkles.Enabled;
        sakura.Rebalance(); flowers.Rebalance(); hearts.Rebalance(); sparkles.Rebalance(); butterflies.Rebalance(); ribbons.Rebalance();
    }

    // Background: only the selected painting is decoded, at the width the window needs (UniformToFill, max 1672 px).
    void ChooseBackground(string key)
    {
        if (!entered || key == background) return;
        LoadBackground(key);
        context.Update(s => s.PinkBackground = key);
    }
    void LoadBackground(string key)
    {
        background = key; image = null; decodedWidth = 0;
        if (key == PinkOptions.SimplyPink) { backdrop.Show(null, simply: true); swatch.Background = SimplySwatch; wash.Strength = .55; RefreshLayoutSoon(); return; }
        var clock = Stopwatch.StartNew(); int width = NeededWidth();
        try { image = Decode(key, width); decodedWidth = width; }
        catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException) { DiagnosticLog.Error("pink-mode", "Background " + key + " could not be decoded", e); }
        decodeMs = clock.Elapsed.TotalMilliseconds;
        backdrop.Show(image, simply: image == null);
        if (image != null) { var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill }; brush.Freeze(); swatch.Background = brush; } else swatch.Background = SimplySwatch;
        wash.Strength = 1; RefreshLayoutSoon();
    }
    static readonly Brush SimplySwatch = PinkArt.SimplyPinkBrush();
    int NeededWidth()
    {
        var window = context.Window; double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        double w = Math.Max(window.ActualWidth, window.ActualHeight * ImageWidth / ImageHeight) * scale;
        return Math.Clamp((int)Math.Ceiling(w / 64) * 64, 640, ImageWidth);
    }
    static BitmapImage Decode(string key, int width)
    {
        var b = new BitmapImage(); b.BeginInit();
        b.UriSource = new Uri("pack://application:,,,/Assets/pink/" + key + ".png"); b.DecodePixelWidth = width;
        b.CacheOption = BitmapCacheOption.OnLoad; b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        b.EndInit(); b.Freeze(); return b;
    }
    void CheckDecode()
    {
        if (decodePending || image == null || decodedWidth >= ImageWidth || NeededWidth() <= decodedWidth * 1.15) return;
        decodePending = true;
        context.Window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { decodePending = false; if (entered && background != PinkOptions.SimplyPink) LoadBackground(background); });
    }
    void OpenPicker()
    {
        picker.Visibility = Visibility.Visible; LoadThumbnails();
        context.Window.PreviewMouseDown += OutsideClick; RefreshLayoutSoon();
    }
    void ClosePicker() { picker.Visibility = Visibility.Collapsed; context.Window.PreviewMouseDown -= OutsideClick; RefreshLayoutSoon(); }
    void OutsideClick(object sender, MouseButtonEventArgs e) { if (!(e.OriginalSource is Visual v && tools.IsAncestorOf(v))) pickerButton.IsChecked = false; }
    // Thumbnails: decoded small (160 px) off the UI thread the first time the picker opens; dropped in Leave().
    void LoadThumbnails()
    {
        if (thumbsLoaded) return; thumbsLoaded = true;
        bool now = args.Contains("--pink-picker-open");
        foreach (var (button, thumb, _) in choices)
        {
            string key = (string)button.Tag;
            if (key == PinkOptions.SimplyPink) { thumb.Background = SimplySwatch; continue; }
            void Set(BitmapImage source) { if (!thumbsLoaded) return; var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill }; brush.Freeze(); thumb.Background = brush; }
            if (now) { Set(Decode(key, 160)); continue; }
            var dispatcher = context.Window.Dispatcher;
            Task.Run(() => { try { var source = Decode(key, 160); dispatcher.BeginInvoke(() => Set(source)); } catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException) { DiagnosticLog.Error("pink-mode", "Thumbnail " + key + " could not be decoded", e); } });
        }
    }
    void ReleaseThumbnails() { thumbsLoaded = false; foreach (var (_, thumb, _) in choices) thumb.Background = PinkArt.Solid(0xFFF7CFE1); }

    void PointerMoved(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(context.Window); long now = Stopwatch.GetTimestamp();
        if (world.Pointer is Point last && lastMove > 0)
        {
            double dt = (now - lastMove) / (double)Stopwatch.Frequency;
            if (dt > 1e-3 && dt < .2) stage.PointerVelocity = stage.PointerVelocity * .5 + (p - last) / dt * .5;
        }
        world.Pointer = p; lastMove = now;
    }
    void PointerLeft(object sender, MouseEventArgs e) { world.Pointer = null; stage.PointerVelocity = default; }

    void Sized()
    {
        world.Width = root.ActualWidth; world.Height = root.ActualHeight;
        if (world.Width <= 0) return;
        if (world.GroundY <= 0) world.GroundY = world.Height * .84;
        double area = world.Width * world.Height / (1280 * 800);
        if (Math.Abs(area - stage.Area) > .12 * stage.Area) { stage.Area = area; Rebudget(); }
        if (!entered) return;
        CheckDecode();
        RefreshLayout(); // at once (this layout pass is complete), so the first frame at the new size is already right
        if (!Running) Present();
    }
    void RefreshLayoutSoon()
    {
        if (layoutPending) return; layoutPending = true;
        context.Window.Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshLayout);
    }
    // Free areas from FRAME's visible UI (recomputed on size/page/mode changes, never per frame): the ground line (top of the
    // bottom bar), the content column (dimmed particles + wash), the free area right of it (butterflies, swirl, secrets).
    void RefreshLayout()
    {
        layoutPending = false;
        if (!entered || world.Width <= 0) return;
        var rects = Rects(); world.Keepout = rects;
        double w = world.Width, h = world.Height;
        if (capturePage)
        {
            Rect? bar = null; foreach (var r in rects) if (r.Top > h * .55 && r.Width > w * .4 && r.Height < h * .3 && (bar == null || r.Width > bar.Value.Width)) bar = r;
            double ground = bar?.Top - 1 ?? h * .84, right = Sidebar + 200;
            foreach (var r in rects) if (r.Left >= Sidebar - 2 && r.Top > 56 && r.Bottom < ground - 2 && r.Left < w * .55 && r.Width < w * .8) right = Math.Max(right, r.Right);
            world.GroundY = ground;
            stage.Free = new Rect(right + 28, 84, Math.Max(0, w - 30 - (right + 28)), Math.Max(0, ground - 14 - 84));
            stage.Sky = new Rect(stage.Free.X, stage.Free.Y, stage.Free.Width, stage.Free.Height * .42);
            stage.Calm = new Rect(Sidebar, 36, right + 30 - Sidebar, h - 36);
            wash.Update(new Size(w, h), Sidebar, right, ground);
            PlaceSecrets(rects, right, ground);
        }
        if (!Running) Present();
        if (!bench && Arg("--pink-report") is string report) WriteReport(report);
    }
    // ModeContext.UiRects stops at the first Control, so a ScrollViewer (the Capture layout's body) counts as one block up to
    // the window's right side. Such blocks are replaced by what they actually show (same rules), so the free space right of
    // the content stays usable for secrets and resting characters.
    Rect[] Rects()
    {
        var found = new List<Rect>(context.UiRects());
        var window = context.Window;
        Rect? Bounds(FrameworkElement e) { try { return e.TransformToAncestor(window).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); } catch (InvalidOperationException) { return null; } }
        static bool Near(Rect a, Rect b) => Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;
        void Collect(DependencyObject node)
        {
            if (node is UIElement { IsVisible: false }) return;
            if (node is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } e && (e is Control or TextBlock or Image or Shape || e is Border { Background: not null } b && b.Background != Brushes.Transparent))
            {
                if (e is ScrollViewer { Content: DependencyObject inner }) { Collect(inner); return; }
                if (Bounds(e) is Rect r && (r.Width < window.ActualWidth * .95 || r.Height < window.ActualHeight * .95)) found.Add(r);
                if (e is Control) return;
            }
            for (int k = 0; k < VisualTreeHelper.GetChildrenCount(node); k++) Collect(VisualTreeHelper.GetChild(node, k));
        }
        void Scan(DependencyObject node)
        {
            if (node == root || node is UIElement { IsVisible: false }) return;
            if (node is ScrollViewer { ActualWidth: > 0, Content: DependencyObject content } sv && Bounds(sv) is Rect block && found.RemoveAll(x => Near(x, block)) > 0) { Collect(content); return; }
            for (int k = 0; k < VisualTreeHelper.GetChildrenCount(node); k++) Scan(VisualTreeHelper.GetChild(node, k));
        }
        if (window.Content is DependencyObject top) Scan(top);
        return [.. found];
    }
    // A secret must be clear of FRAME's visible UI and actually reachable by the mouse: some layout parts take input over
    // empty space (the Capture layout's ScrollViewer is hit-testable over its whole area), so the centre and four points
    // around it are input-hit-tested; only the window's own background or the scene may answer.
    bool Reachable(Point p, double rx = 7, double ry = 7)
    {
        var window = context.Window;
        foreach (var (dx, dy) in new (double, double)[] { (0, 0), (-rx, 0), (rx, 0), (0, -ry), (0, ry) })
            if (window.InputHitTest(new Point(p.X + dx, p.Y + dy)) is DependencyObject hit && hit != window.Content && hit != root && !root.IsAncestorOf(hit)) return false;
        return true;
    }
    void PlaceSecrets(Rect[] rects, double right, double ground)
    {
        bool Clear(Point p) { var r = new Rect(p.X - 18, p.Y - 18, 36, 36); foreach (var k in rects) if (k.IntersectsWith(r)) return false; return p.X > 8 && p.Y > 44 && p.X < world.Width - 8 && p.Y < world.Height - 8 && Reachable(p); }
        // Heart: the largest empty stretch of the sidebar column (between Library and Settings).
        Point? heartAt = null;
        var side = rects.Where(r => r.Left < Sidebar - 4 && r.Right <= Sidebar + 4 && r.Top > 40).OrderBy(r => r.Top).ToList();
        double from = 120, best = 0, bestTop = 0;
        foreach (var r in side) { if (r.Top - from > best) { best = r.Top - from; bestTop = from; } from = Math.Max(from, r.Bottom); }
        if (world.Height - 40 - from > best) { best = world.Height - 40 - from; bestTop = from; }
        if (best >= 110) { var p = new Point(Sidebar * .63, bestTop + best * .44); if (Clear(p)) heartAt = p; }
        // Cookie: resting on the ground line, near the right end of the free area.
        Point? treatAt = null;
        for (double x = world.Width - 70; x > right + 40 && treatAt == null; x -= 36) { var p = new Point(x, ground - 7); var r = new Rect(p.X - 12, p.Y - 8, 24, 15); if (rects.All(k => !k.IntersectsWith(r)) && Reachable(p, 7, 4)) treatAt = p; }
        // Star: high in the free sky, else in the strip along the window's right edge (below the tools).
        Point? starAt = null; var f = stage.Free; double w = world.Width;
        var stars = new List<Point>();
        if (f.Width > 80 && f.Height > 120) stars.AddRange([new Point(f.X + f.Width * .72, f.Y + 46), new Point(f.X + f.Width * .45, f.Y + 70), new Point(f.X + f.Width * .85, f.Y + 110)]);
        for (double y = 128; y < Math.Min(ground - 60, 330); y += 34) stars.Add(new Point(w - 29, y));
        foreach (var p in stars) if (Clear(p) && (treatAt is not Point tp || (tp - p).Length > 80)) { starAt = p; break; }
        secrets.Place(heartAt, treatAt, starAt);
    }

    // --pink-fake-dog (diagnostics): a stand-in "dog" thing trotting along the ground line, jumping for low hearts
    // ("heart-caught") and barking now and then, so the interactions can be reviewed without the real dogs.
    void StartFakeDog()
    {
        fakeDog = new PinkThing { Kind = "dog", Owner = this, Size = 22 }; world.Things.Add(fakeDog);
        var mark = new Path { Data = PinkArt.Icons[6], Fill = PinkArt.Plum, Opacity = .85, Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, RenderTransform = fakeDogPose };
        fakeDogMark = mark; root.Children.Insert(root.Children.IndexOf(dogs.Visual) + 1, mark);
    }
    void FakeDog(double dt)
    {
        var f = stage.Free; double left = f.IsEmpty ? world.Width * .5 : f.Left, rightEnd = f.IsEmpty ? world.Width - 40 : f.Right, ground = world.GroundY;
        if (dogX < 0) dogX = (left + rightEnd) / 2;
        PinkThing? target = null; double best = 420;
        foreach (var t in world.Things) if (t.Kind == "heart" && t.Position.Y > ground - 230 && t.Position.Y < ground + 30 && t.Position.X > left - 60 && t.Position.X < rightEnd + 60 && Math.Abs(t.Position.X - dogX) < best) { best = Math.Abs(t.Position.X - dogX); target = t; }
        if (target != null) dogDir = Math.Sign(target.Position.X - dogX + 1e-6);
        dogX = Math.Clamp(dogX + dogDir * (target != null ? 150 : 85) * dt, left - 60, rightEnd + 60); if (dogX >= rightEnd + 59) dogDir = -1; if (dogX <= left - 59) dogDir = 1;
        if (target != null && Math.Abs(target.Position.X - dogX) < 45 && target.Position.Y > ground - 200 && dogJump <= 0) dogJump = .7;
        double lift = dogJump > 0 ? 120 * Math.Sin(Math.PI * (1 - dogJump / .7)) : 0; dogJump = Math.Max(0, dogJump - dt);
        var head = new Point(dogX, ground - 18 - lift); fakeDog!.Position = head; fakeDog.Velocity = new Vector(dogDir * 85, 0);
        foreach (var t in world.Things) if (t.Kind == "heart" && (t.Position - head).Length < 30) { world.Raise("heart-caught", t.Position, t); break; }
        if ((dogBark -= dt) <= 0) { dogBark = 9; world.Raise("bark", head); }
        fakeDogPose.Matrix = new Matrix(2.4, 0, 0, 2.4, dogX - 19, ground - 36 - lift);
    }

    // --pink-report <json>: layout, secrets (with a real hit test at each spot), tools (names, state, no tooltips).
    void WriteReport(string path)
    {
        var window = context.Window; window.UpdateLayout(); // the spots were just (re)placed
        object Spot((string Name, Point At, bool Placed) s)
        {
            bool hit = false;
            if (s.Placed && capturePage && secrets.SpotElement(s.Name) is Visual spot) hit = window.InputHitTest(s.At) is DependencyObject v && (v == spot || spot.IsAncestorOf(v));
            bool overUi = world.Keepout.Any(k => k.Contains(s.At));
            var chain = new List<string>(); for (DependencyObject? v = window.InputHitTest(s.At) as DependencyObject; v != null && chain.Count < 8; v = VisualTreeHelper.GetParent(v)) chain.Add(v.GetType().Name + (v is FrameworkElement { Name.Length: > 0 } n ? "#" + n.Name : ""));
            return new { s.Name, x = Math.Round(s.At.X, 1), y = Math.Round(s.At.Y, 1), s.Placed, hitTestReachesSecret = hit, overFrameUi = overUi, hit = string.Join(" < ", chain) };
        }
        bool NoTooltips(DependencyObject node) { if (node is FrameworkElement fe && fe.ToolTip != null) return false; for (int k = 0; k < VisualTreeHelper.GetChildrenCount(node); k++) if (!NoTooltips(VisualTreeHelper.GetChild(node, k))) return false; return true; }
        var data = new
        {
            background, decodedWidth, decodeMs = Math.Round(decodeMs, 1), windowWidth = window.ActualWidth, windowHeight = window.ActualHeight, dpi = VisualTreeHelper.GetDpi(window).DpiScaleX,
            effects = layers.Where(l => l.Enabled).Select(l => l.Effect).ToArray(), load = stage.Load, keepoutRects = world.Keepout.Length, ground = Math.Round(world.GroundY, 1),
            free = R(stage.Free), sky = R(stage.Sky), calm = R(stage.Calm), capturePage, secrets = secrets.Spots.Select(Spot).ToArray(),
            tools = effectButtons.Select(e => new { name = AutomationProperties.GetName(e.Button), on = e.Button.IsChecked == true, tooltip = e.Button.ToolTip?.ToString() }).ToArray(),
            picker = new { name = AutomationProperties.GetName(pickerButton), open = pickerButton.IsChecked == true, choices = choices.Select(c => new { name = AutomationProperties.GetName(c.Button), selected = c.Button.IsChecked == true }).ToArray() },
            noTooltipsInTools = NoTooltips(tools), things = world.Things.GroupBy(t => t.Kind).ToDictionary(g => g.Key, g => g.Count())
        };
        try { File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); } catch (IOException) { }
    }
    static object R(Rect r) => r.IsEmpty ? "empty" : new { x = Math.Round(r.X), y = Math.Round(r.Y), w = Math.Round(r.Width), h = Math.Round(r.Height) };
    // --pink-trace (with --pink-stats): per particle step the gust at mid-window and the mean horizontal speed of each kind,
    // so wind coherence can be checked numerically (all kinds speed up together as a gust passes).
    void Trace()
    {
        double Mean(string kind) { double sum = 0; int n = 0; foreach (var t in world.Things) if (t.Kind == kind) { sum += t.Velocity.X; n++; } return n == 0 ? double.NaN : Math.Round(sum / n, 1); }
        trace!.Add(new { t = Math.Round(world.Time, 3), gust = Math.Round(world.Gust, 3), petal = Mean("petal"), flower = Mean("flower"), heart = Mean("heart"), ribbon = Mean("ribbon"), swirl = stage.Swirl.Active });
    }
    void WriteStats(string path)
    {
        if (trace != null && Arg("--pink-trace") is string tracePath) try { File.WriteAllText(tracePath, JsonSerializer.Serialize(trace, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals })); } catch (IOException) { }
        var data = new { advanceCalls, advanceMsTotal = Math.Round(advanceTicks * 1000d / Stopwatch.Frequency, 1), advanceMsAverage = Math.Round(advanceTicks * 1000d / Stopwatch.Frequency / Math.Max(1, advanceCalls), 4), effects = layers.Where(l => l.Enabled).Select(l => l.Effect).ToArray(), things = world.Things.Count, sceneSeconds = Math.Round(world.Time, 2) };
        try { File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); } catch (IOException) { }
    }
    // --pink-bench <seconds> <json> (with --render; diagnostics only): a separate STA thread builds its own off-screen window
    // (1280 x 800 at x = -20000, never activated, no taskbar entry) with a fresh PinkScene driven by a real CaptureAtmosphere
    // clock and real WPF composition. It keeps the process alive after the render has finished and measures once FRAME's own
    // window has shut down: process CPU (all threads, incl. WPF's render thread), clock callbacks, the scene's step time.
    static bool benchStarted;
    static void StartBench(double seconds, string path, int? seed)
    {
        if (benchStarted) return; benchStarted = true;
        var main = Application.Current.Dispatcher;
        var thread = new Thread(() =>
        {
            var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Width = 1280, Height = 800, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Background = PinkArt.Solid(0xFFFBE3EE) };
            var atmosphere = new CaptureAtmosphere(window, new SandDriftLayer(), new DuneFlowLayer()) { Desert = false };
            var settings = new Settings();
            var context = new ModeContext(window, atmosphere, null!, () => settings, _ => { }) { Seed = seed };
            var scene = new PinkScene(context) { bench = true };
            var grid = new Grid(); grid.Children.Add(scene.Root);
            grid.Children.Add(new ContentControl { Content = scene.Tools, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 38, 14, 0), Focusable = false });
            window.Content = grid; window.Show();
            scene.Enter(); atmosphere.Attach(scene); atmosphere.Enable(true); scene.PageChanged(true);
            using var self = Process.GetCurrentProcess();
            var wall = Stopwatch.StartNew(); double cpu = 0, start = 0; long ticks0 = 0, calls0 = 0, bytes0 = 0; int frames0 = 0, gc0 = 0; bool measuring = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                if (!measuring && wall.Elapsed.TotalSeconds >= 3 && main.HasShutdownFinished) { measuring = true; self.Refresh(); cpu = self.TotalProcessorTime.TotalSeconds; start = wall.Elapsed.TotalSeconds; ticks0 = scene.advanceTicks; calls0 = scene.advanceCalls; frames0 = atmosphere.Frames; bytes0 = GC.GetTotalAllocatedBytes(); gc0 = GC.CollectionCount(0); }
                if (!measuring || wall.Elapsed.TotalSeconds < start + seconds) return;
                timer.Stop(); self.Refresh(); double elapsed = wall.Elapsed.TotalSeconds - start, used = self.TotalProcessorTime.TotalSeconds - cpu;
                var data = new
                {
                    seconds = Math.Round(elapsed, 2), cpuPercentOfOneCore = Math.Round(used / elapsed * 100, 2), cpuPercentOfMachine = Math.Round(used / elapsed / Environment.ProcessorCount * 100, 3), processors = Environment.ProcessorCount,
                    clockRunning = atmosphere.Running, renderCallbacksPerSecond = Math.Round((atmosphere.Frames - frames0) / elapsed, 1), sceneAdvancesPerSecond = Math.Round((scene.advanceCalls - calls0) / elapsed, 1),
                    sceneStepMsPerSecond = Math.Round((scene.advanceTicks - ticks0) * 1000d / Stopwatch.Frequency / elapsed, 2), allocatedKBPerSecond = Math.Round((GC.GetTotalAllocatedBytes() - bytes0) / 1024d / elapsed, 1), gen0Collections = GC.CollectionCount(0) - gc0,
                    stepAllocatedKBPerLayer = scene.layers.Select((l, i) => (l.Effect, KB: Math.Round(scene.layerBytes[i] / 1024d / wall.Elapsed.TotalSeconds, 1))).ToDictionary(x => x.Effect, x => x.KB),
                    effects = scene.layers.Where(l => l.Enabled).Select(l => l.Effect).ToArray(), load = scene.stage.Load, background = scene.background, things = scene.world.Things.GroupBy(t => t.Kind).ToDictionary(g => g.Key, g => g.Count()), window = new { window.ActualWidth, window.ActualHeight }
                };
                try { File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); } catch (IOException) { }
                atmosphere.Detach(scene); scene.Leave(); window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };
            timer.Start(); Dispatcher.Run();
        }) { IsBackground = false, Name = "Pink bench" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
}

// The background painting (UniformToFill: centred, cropped to the window's aspect, high-quality scaling) or Simply Pink.
// Redrawn only when the image or the size changes.
sealed class PinkBackdrop : FrameworkElement
{
    BitmapSource? image; bool simply = true;
    public PinkBackdrop() { IsHitTestVisible = false; ClipToBounds = true; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); }
    public void Show(BitmapSource? source, bool simply) { image = source; this.simply = simply; InvalidateVisual(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize; if (size.Width <= 0 || size.Height <= 0) return;
        if (simply || image == null) { PinkArt.DrawSimplyPink(dc, size); return; }
        double scale = Math.Max(size.Width / image.PixelWidth, size.Height / image.PixelHeight), w = image.PixelWidth * scale, h = image.PixelHeight * scale;
        dc.DrawImage(image, new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h));
    }
}

// Readability wash (static; redrawn on layout changes): a soft blush veil behind the content column fading out to the
// right of the content, a band rising above the bottom bar, and a light band behind the title row. Strength is lower on
// Simply Pink (already calm).
sealed class PinkWash : FrameworkElement
{
    Brush? column, bottom, top; Rect columnRect, bottomRect, topRect;
    double strength = 1;
    public double Strength { get => strength; set { if (strength == value) return; strength = value; InvalidateVisual(); } }
    public PinkWash() => IsHitTestVisible = false;
    static Color Veil(double alpha) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), 0xFF, 0xF5, 0xF9);
    static Brush Gradient(Point a, Point b, params (double At, double Alpha)[] stops)
    {
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = a, EndPoint = b };
        foreach (var (at, alpha) in stops) brush.GradientStops.Add(new GradientStop(Veil(alpha), at));
        brush.Freeze(); return brush;
    }
    public void Update(Size size, double left, double contentRight, double ground)
    {
        double end = contentRight + 220, fadeFrom = (contentRight + 20 - left) / Math.Max(1, end - left);
        column = Gradient(new(left, 0), new(end, 0), (0, .58), (Math.Clamp(fadeFrom, 0, 1), .48), (1, 0)); columnRect = new Rect(left, 0, Math.Max(0, end - left), size.Height);
        bottom = Gradient(new(0, ground - 90), new(0, size.Height), (0, 0), (.55, .32), (1, .45)); bottomRect = new Rect(left, ground - 90, Math.Max(0, size.Width - left), Math.Max(0, size.Height - ground + 90));
        top = Gradient(new(0, 0), new(0, 52), (0, .55), (1, 0)); topRect = new Rect(0, 0, size.Width, 52);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (column == null) return;
        dc.PushOpacity(strength);
        dc.DrawRectangle(column, null, columnRect); dc.DrawRectangle(bottom, null, bottomRect); dc.DrawRectangle(top, null, topRect);
        dc.Pop();
    }
}

// The heart swirl's outline: a soft glowing heart (wide pink glow, white core line) drawn once per swirl and then only
// posed (beat scale) and faded with the swirl's pull, so the figure reads clearly over a busy painting.
sealed class SwirlGlow : FrameworkElement
{
    readonly DrawingVisual line = new() { Opacity = 0 };
    readonly MatrixTransform pose = new();
    int drawn = -1;
    static readonly Pen Wide = Frozen(new Pen(PinkArt.Solid(0x38FF6FB5), 14) { LineJoin = PenLineJoin.Round }), Mid = Frozen(new Pen(PinkArt.Solid(0x66FF8CC6), 5) { LineJoin = PenLineJoin.Round }), Core = Frozen(new Pen(PinkArt.Solid(0xD9FFFFFF), 1.6) { LineJoin = PenLineJoin.Round });
    static Pen Frozen(Pen p) { p.Freeze(); return p; }
    public SwirlGlow() { IsHitTestVisible = false; line.Transform = pose; AddVisualChild(line); }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => line;
    public void Show(PinkSwirl swirl)
    {
        if (!swirl.Active) { if (line.Opacity != 0) line.Opacity = 0; return; }
        if (drawn != swirl.Version)
        {
            drawn = swirl.Version; var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure((Point)(PinkSwirl.Curve(0) * swirl.Radius), false, true);
                for (int i = 1; i < 120; i++) c.LineTo((Point)(PinkSwirl.Curve(2 * Math.PI * i / 120) * swirl.Radius), true, true);
            }
            g.Freeze();
            using var dc = line.RenderOpen(); dc.DrawGeometry(null, Wide, g); dc.DrawGeometry(null, Mid, g); dc.DrawGeometry(null, Core, g);
        }
        double k = swirl.Scale, pull = swirl.Pull;
        pose.Matrix = new Matrix(k, 0, 0, k, swirl.Centre.X, swirl.Centre.Y);
        line.Opacity = Math.Round(.9 * pull * pull, 2);
    }
}
