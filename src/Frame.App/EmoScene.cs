using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shape = System.Windows.Shapes.Shape;
using System.Windows.Threading;

namespace Frame.App;

// Emo mode scene (0.6.0): the illustrated background (EmoArt, one of four), the curated effects (EmoEffects: dark rose
// petals, rain, bats, slow drifting fog; any combination), the compact tools under the mode buttons (four thumbnails, four
// effect toggles) and the rotating quote (EmoQuote, the Hero in the Capture layout). Choices apply at once and are saved
// through ModeContext.Update (Settings.EmoBackground / EmoEffects; first entry: Dead rose garden with petals).
// Cost: the illustration is rasterized once per size/background (BitmapCache) and only composited afterwards; effects
// advance only from CaptureAtmosphere's clock (on screen, Capture page shown, at least one effect on), ~30 updates/s;
// Library/Settings keep the scene as a still background under the scrim. Leave() stops the quote timer and every effect and
// drops the illustration, effect bitmaps and thumbnails.
sealed class EmoScene : IModeScene
{
    readonly ModeContext context;
    readonly Grid root = new() { IsHitTestVisible = false, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(10, 8, 9)) };
    readonly Grid art = new() { IsHitTestVisible = false };
    readonly ScaleTransform stretch = new();
    readonly EmoEffect[] effects; // EmoOptions.Effects order: petals, rain, bats, fog
    readonly StackPanel tools = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
    readonly RadioButton[] pickers;
    readonly ToggleButton[] toggles;
    readonly EmoQuoteView quote = new();
    readonly EmoQuoteRotation rotation;
    readonly DispatcherTimer rebuild = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
    readonly Random random;
    readonly bool renderDiagnostics;
    string background = EmoOptions.FirstBackground;
    Size built;
    long builtAt;
    bool entered, capturePage = true, anyEffect, syncing, diagnosed;
    long stepTicks; int steps; // diagnostics: UI-thread cost of the effect updates
    public FrameworkElement Root => root;
    public FrameworkElement? Tools => tools;
    public FrameworkElement? Hero => quote;
    public bool WantsFrames => entered && capturePage && anyEffect;
    public bool FullRate => false;

    public EmoScene(ModeContext context, bool diagnostics = true)
    {
        this.context = context; renderDiagnostics = diagnostics && Environment.GetCommandLineArgs().Contains("--render");
        random = context.Seed is int seed ? new Random(seed) : new Random();
        root.Background.Freeze();
        art.CacheMode = new BitmapCache { RenderAtScale = 1, EnableClearType = false, SnapsToDevicePixels = false };
        art.RenderTransform = stretch; root.Children.Add(art);
        var petals = new EmoPetals(); var rain = new EmoRain(); var bats = new EmoBats(); var fog = new EmoFog();
        effects = [petals, rain, bats, fog];
        foreach (var layer in new EmoEffect[] { bats, fog, rain, petals }) root.Children.Add(layer); // depth: bats far, petals near
        root.SizeChanged += (_, e) => { Resized(e.NewSize); quote.Size = 20 + 6 * Math.Clamp((e.NewSize.Height - 690) / 110, 0, 1); };
        rebuild.Tick += (_, _) => BuildArt();
        rotation = new EmoQuoteRotation(context, quote, random);

        // Tools: four thumbnails (exactly one background) and four effect toggles; instant, saved, no hover tooltips.
        AutomationProperties.SetName(tools, "Emo mode background and effects");
        var pickTemplate = (ControlTemplate)XamlReader.Parse(PickerTemplate); var toggleTemplate = (ControlTemplate)XamlReader.Parse(ToggleTemplate);
        var focus = Application.Current is { } app && app.Dispatcher.CheckAccess() ? app.TryFindResource("QuietFocus") as Style : null;
        pickers = EmoOptions.Backgrounds.Select(key =>
        {
            var b = new RadioButton { Template = pickTemplate, GroupName = "EmoBackground", Tag = key, Cursor = System.Windows.Input.Cursors.Hand, FocusVisualStyle = focus };
            AutomationProperties.SetName(b, EmoArt.Name(key) + " background");
            b.Checked += (_, _) => { if (!syncing) SetBackground(key); };
            tools.Children.Add(b); return b;
        }).ToArray();
        var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(9, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = .8 };
        divider.SetResourceReference(Border.BackgroundProperty, "Line"); tools.Children.Add(divider);
        string[] names = ["Dark rose petals", "Rain", "Bats", "Slow drifting fog"];
        toggles = EmoOptions.Effects.Select((key, i) =>
        {
            var t = new ToggleButton { Template = toggleTemplate, Content = Icon(i), Tag = key, Cursor = System.Windows.Input.Cursors.Hand, FocusVisualStyle = focus };
            AutomationProperties.SetName(t, names[i]);
            t.Click += (_, _) => ToggleEffect(key);
            tools.Children.Add(t); return t;
        }).ToArray();
    }

    const string PickerTemplate =
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
        "<Grid Background='Transparent' Margin='3,0,3,0'>" +
        "<Border x:Name='Edge' Width='40' Height='25' CornerRadius='2' BorderThickness='1' BorderBrush='{DynamicResource Line}' Background='{TemplateBinding Background}' Opacity='.68'/>" +
        "<Rectangle x:Name='Mark' Width='14' Height='1.5' Margin='0,0,0,-5' VerticalAlignment='Bottom' HorizontalAlignment='Center' Fill='{DynamicResource Accent}' Visibility='Hidden'/>" +
        "</Grid><ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Edge' Property='Opacity' Value='.85'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Edge' Property='Opacity' Value='1'/><Setter TargetName='Edge' Property='BorderBrush' Value='{DynamicResource Muted}'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>";
    const string ToggleTemplate =
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'>" +
        "<Grid Background='Transparent' Width='28' Height='25'>" +
        "<ContentPresenter x:Name='Icon' HorizontalAlignment='Center' VerticalAlignment='Center' Opacity='.36'/>" +
        "<Ellipse x:Name='Dot' Width='3' Height='3' Margin='0,0,0,-5' VerticalAlignment='Bottom' HorizontalAlignment='Center' Fill='{DynamicResource Accent}' Visibility='Hidden'/>" +
        "</Grid><ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Icon' Property='Opacity' Value='.7'/></Trigger>" +
        "<Trigger Property='IsChecked' Value='True'><Setter TargetName='Icon' Property='Opacity' Value='1'/><Setter TargetName='Dot' Property='Visibility' Value='Visible'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate>";
    // 16 × 16 icons: a petal, slanted rain, a bat, drifting fog.
    static FrameworkElement Icon(int i)
    {
        var path = new System.Windows.Shapes.Path { Width = 16, Height = 16, Stretch = Stretch.None, StrokeThickness = 1.35, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.Data = Geometry.Parse(i switch
        {
            0 => "M 8,14.5 C 4.2,13.2 2.4,8.8 3.4,5.2 C 4.1,2.8 6.4,2.2 8,3.7 C 9.5,2.1 12,2.7 12.7,5 C 13.8,8.8 11.9,13.2 8,14.5 Z",
            1 => "M 5.2,2 L 3.6,7.4 M 10.2,1.6 L 8.6,7 M 13.6,6.4 L 12,11.8 M 7.4,9 L 5.8,14.4",
            2 => "M 8,11 L 7,10 C 6,8.6 5,8.8 4.4,9.4 C 4,8.4 3,8 2,8.6 C 1.8,7.4 1,6.4 0,6 C 2,5.4 5,5.6 6.6,7.2 L 7.2,6 L 7.7,7 L 8.3,7 L 8.8,6 L 9.4,7.2 C 11,5.6 14,5.4 16,6 C 15,6.4 14.2,7.4 14,8.6 C 13,8 12,8.4 11.6,9.4 C 11,8.8 10,8.6 9,10 Z",
            _ => "M 1.5,4.6 C 4,3 6,6.2 8.5,4.6 C 11,3 13,6.2 14.5,4.6 M 1.5,8.6 C 4,7 6,10.2 8.5,8.6 C 11,7 13,10.2 14.5,8.6 M 3.5,12.6 C 6,11 8,14.2 10.5,12.6",
        });
        if (i is 0 or 2) path.SetResourceReference(Shape.FillProperty, "Text"); else path.SetResourceReference(Shape.StrokeProperty, "Text");
        return path;
    }

    // ---- lifecycle ----
    public void Enter()
    {
        if (entered) return;
        entered = true;
        var s = context.Settings;
        background = EmoOptions.Background(s.EmoBackground);
        Thumbnails(); SyncTools();
        if (root.ActualWidth > 0) BuildArt();
        ApplyEffects(EmoOptions.ActiveEffects(s.EmoEffects));
        rotation.Enter();
    }
    public void Leave()
    {
        if (!entered) return;
        entered = false;
        rotation.Leave(); rebuild.Stop();
        foreach (var e in effects) e.Hide();
        anyEffect = false;
        art.Children.Clear(); built = default; stretch.ScaleX = stretch.ScaleY = 1;
        foreach (var p in pickers) p.Background = null;
    }
    public void PageChanged(bool capture)
    {
        capturePage = capture; context.Atmosphere.Refresh();
        if (!diagnosed && entered && capture && renderDiagnostics) { diagnosed = true; Diagnostics(); }
    }
    public void Advance(double dt)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < effects.Length; i++) if (effects[i].On) effects[i].Advance(dt);
        stepTicks += Stopwatch.GetTimestamp() - start; steps++;
    }
    public void Present() { for (int i = 0; i < effects.Length; i++) if (effects[i].On) effects[i].Settle(); }

    // ---- background ----
    void SetBackground(string key)
    {
        if (key == background) return;
        background = key;
        context.Update(s => s.EmoBackground = key);
        if (entered) BuildArt();
    }
    void Resized(Size size)
    {
        if (!entered || size.Width < 1 || size.Height < 1) return;
        // First size, a single change (maximize, restore) or a diagnostics render: rebuild now. While the edge is being dragged
        // the last illustration is stretched and rebuilt once the size has settled.
        bool dragging = Stopwatch.GetElapsedTime(builtAt).TotalMilliseconds < 400;
        if (built.Width < 1 || !dragging || renderDiagnostics) { BuildArt(); return; }
        stretch.ScaleX = size.Width / built.Width; stretch.ScaleY = size.Height / built.Height;
        rebuild.Stop(); rebuild.Start();
    }
    void BuildArt()
    {
        rebuild.Stop();
        double w = root.ActualWidth, h = root.ActualHeight;
        if (!entered || w < 1 || h < 1) return;
        art.Children.Clear(); stretch.ScaleX = stretch.ScaleY = 1;
        foreach (var layer in EmoArt.Build(background, w, h))
        {
            var view = new ArtLayer(layer.Drawing);
            if (layer.Blur > 0) view.Effect = new BlurEffect { Radius = layer.Blur * h / 800, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
            art.Children.Add(view);
        }
        built = new Size(w, h); builtAt = Stopwatch.GetTimestamp();
    }
    sealed class ArtLayer : FrameworkElement
    {
        readonly Drawing drawing;
        public ArtLayer(Drawing drawing) { this.drawing = drawing; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(drawing);
    }
    // Thumbnails: the right part of each illustration (its subject) at 40 × 25 DIP, rendered once per entry at the screen's DPI.
    void Thumbnails()
    {
        double dpi = VisualTreeHelper.GetDpi(context.Window).DpiScaleX;
        Rect[] crops = [new(836, 150, 440, 275), new(950, 250, 320, 200), new(880, 70, 400, 250), new(840, 90, 420, 262)]; // each scene's subject
        for (int i = 0; i < pickers.Length; i++)
        {
            var v = new DrawingVisual(); var crop = crops[i]; double scale = 40 * dpi / crop.Width;
            using (var dc = v.RenderOpen())
            {
                dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, -crop.X * scale, -crop.Y * scale));
                foreach (var layer in EmoArt.Build(EmoOptions.Backgrounds[i], 1280, 800)[..^1]) dc.DrawDrawing(layer.Drawing);
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap((int)Math.Round(40 * dpi), (int)Math.Round(25 * dpi), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(v); bmp.Freeze();
            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill }; brush.Freeze();
            pickers[i].Background = brush;
        }
    }
    void SyncTools()
    {
        syncing = true;
        try
        {
            foreach (var p in pickers) p.IsChecked = (string)p.Tag == background;
            var on = EmoOptions.ActiveEffects(context.Settings.EmoEffects);
            foreach (var t in toggles) t.IsChecked = on.Contains((string)t.Tag);
        }
        finally { syncing = false; }
    }

    // ---- effects ----
    void ToggleEffect(string key)
    {
        var next = EmoOptions.Toggle(EmoOptions.ActiveEffects(context.Settings.EmoEffects), key);
        context.Update(s => s.EmoEffects = next);
        ApplyEffects(next);
    }
    void ApplyEffects(string[] on)
    {
        anyEffect = false;
        foreach (var e in effects)
        {
            if (on.Contains(e.Key)) { e.Show(random); anyEffect = true; } else e.Hide();
        }
        SyncTools();
        context.Atmosphere.Refresh(); // clock on/off; a stopped clock presents the still state (Present)
        if (!context.Atmosphere.Running) { root.UpdateLayout(); Present(); }
    }

    // ---- diagnostics (only with --render in an isolated --data folder) ----
    // --emo-quote-test <json>: rotation on a short interval (use --emo-quote-seconds 2): entry, interval changes, a simulated
    //   hidden period (no change, timer stopped), resume (exactly one change after the kept remaining time, never sooner than
    //   the resume minimum), re-entry, 30 re-entries without immediate repeats, and the reserved height for every quote.
    // --emo-perf <json>: process CPU, clock updates, effect step cost and managed allocation per effect combination while the
    //   real clock runs (window off-screen at -20000 but rendered normally).
    // Both run in nested dispatcher frames on the first Capture page of a --render run, before the PNG is written.
    void Diagnostics()
    {
        var args = Environment.GetCommandLineArgs();
        string? Arg(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (Arg("--emo-quote-test") is string q) QuoteTest(q);
        if (Arg("--emo-perf") is string p) PerfTest(p);
    }
    static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; }; t.Start(); Dispatcher.PushFrame(frame);
    }
    void QuoteTest(string path)
    {
        var clock = Stopwatch.StartNew(); var events = new List<Dictionary<string, object>>(); string phase = "";
        double interval = rotation.Interval.TotalSeconds;
        void Shown(int index, string reason) => events.Add(new() { ["t"] = Math.Round(clock.Elapsed.TotalSeconds, 2), ["phase"] = phase, ["index"] = index, ["quote"] = EmoQuotes.All[index], ["reason"] = reason, ["fading"] = quote.HasAnimatedProperties, ["visible"] = quote.IsVisible });
        // The layout shows its hero on its first Update (a moment after this first page change); show it now, as it will be.
        if (quote.Parent is ContentControl heroHost) heroHost.Visibility = Visibility.Visible;
        context.Window.UpdateLayout(); Pump(TimeSpan.FromMilliseconds(300));
        rotation.Shown += Shown;
        int startupIndex = rotation.Index;
        phase = "re-enter"; rotation.Leave(); rotation.SimulateOnScreen(true); rotation.Enter();
        phase = "on screen"; Pump(TimeSpan.FromSeconds(interval * 2.5));
        phase = "hidden"; double remainingAtHide = rotation.Remaining.TotalSeconds; rotation.SimulateOnScreen(false); bool runningHidden = rotation.Running;
        Pump(TimeSpan.FromSeconds(interval * 3));
        phase = "shown again"; double resumedAt = clock.Elapsed.TotalSeconds; rotation.SimulateOnScreen(true); Pump(TimeSpan.FromSeconds(interval * .75));
        Pump(TimeSpan.FromSeconds(Math.Max(.05, rotation.Remaining.TotalSeconds - .25)));
        phase = "hidden briefly"; double remainingShort = rotation.Remaining.TotalSeconds; rotation.SimulateOnScreen(false); Pump(TimeSpan.FromSeconds(interval));
        phase = "shown again (short remainder)"; double resumedShortAt = clock.Elapsed.TotalSeconds; rotation.SimulateOnScreen(true); Pump(TimeSpan.FromSeconds(interval * .9));
        phase = "re-enter"; rotation.Leave(); rotation.Enter();
        var reentries = new List<int> { rotation.Index };
        rotation.Shown -= Shown;
        for (int n = 0; n < 30; n++) { rotation.Leave(); rotation.Enter(); reentries.Add(rotation.Index); }
        rotation.SimulateOnScreen(null);
        // Reserved height: every quote in the real layout; the controls below must not move.
        var heights = new List<object>(); var heightValues = new List<double>();
        FrameworkElement? below = null;
        if (quote.Parent is ContentControl host && host.Parent is Panel body)
            below = body.Children.Count > body.Children.IndexOf(host) + 1 ? body.Children[body.Children.IndexOf(host) + 1] as FrameworkElement : null;
        foreach (var text in EmoQuotes.All)
        {
            quote.Show(text, false); context.Window.UpdateLayout();
            heightValues.Add(Math.Round(quote.ActualHeight, 2));
            heights.Add(new { quote = text, height = Math.Round(quote.ActualHeight, 2), width = Math.Round(quote.ActualWidth, 2), controlsTop = below == null ? -1 : Math.Round(below.TranslatePoint(new Point(), context.Window).Y, 2) });
        }
        quote.Show(EmoQuotes.All[rotation.Index], false);
        var timed = events.Where(e => (string)e["reason"] == "interval").ToList();
        var sequence = events.Select(e => (int)e["index"]).ToList();
        var report = new
        {
            intervalSeconds = interval, startupIndex, events, remainingAtHideSeconds = Math.Round(remainingAtHide, 2), timerRunningWhileHidden = runningHidden,
            changesWhileHidden = events.Count(e => ((string)e["phase"]).StartsWith("hidden")),
            firstChangeAfterResumeSeconds = timed.Where(e => (double)e["t"] > resumedAt).Select(e => Math.Round((double)e["t"] - resumedAt, 2)).FirstOrDefault(-1),
            changesInFirstResume = timed.Count(e => (string)e["phase"] == "shown again"),
            remainingAtShortHideSeconds = Math.Round(remainingShort, 2),
            firstChangeAfterShortResumeSeconds = timed.Where(e => (double)e["t"] > resumedShortAt).Select(e => Math.Round((double)e["t"] - resumedShortAt, 2)).FirstOrDefault(-1),
            changesInShortResume = timed.Count(e => (string)e["phase"] == "shown again (short remainder)"),
            immediateRepeats = sequence.Zip(sequence.Skip(1)).Count(p => p.First == p.Second) + reentries.Zip(reentries.Skip(1)).Count(p => p.First == p.Second),
            reentries, reentryRoundsComplete = reentries.Count >= 10,
            heights, distinctHeights = heightValues.Distinct().Count(), fontSize = quote.UsedSize, reservedHeight = Math.Round(quote.Reserved, 2), fitAtMinimumWindow = Fit(620, 20), fitAtDefaultWindow = Fit(640, 26),
            longest = EmoQuotes.Longest,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
    object Fit(double width, double size) { var (used, height) = quote.Fit(width, size); return new { width, requested = size, used, height = Math.Round(height, 2) }; }
    // Runs a diagnostic on its own STA thread and dispatcher (own off-screen window: rendering, CompositionTarget.Rendering and
    // timers run normally there) while this thread waits. The first page change happens inside WPF's render pass, where nested
    // frames cannot render, so a measurement of the real clock cannot run on this thread.
    static void OnOwnThread(string path, Func<Task> body)
    {
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception e) { File.WriteAllText(path + ".error.txt", e.ToString()); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); thread.Join();
    }
    // A complete EmoScene (Alley background and its quote) in a 1280 × 800 off-screen window that never activates, driven by
    // its own CaptureAtmosphere exactly as in FRAME. Per effect combination: process CPU (the main thread waits, so this is
    // the scene, the clock and WPF's render thread), clock updates, effect step cost, managed allocation. Then Library page
    // (still), the window hidden as in the tray (clock and quote timer must stop) and shown again (no catch-up step), and a
    // quote change sampled to show the fade. Settings stay in memory (nothing is written).
    static void PerfTest(string path) => OnOwnThread(path, async () =>
    {
        var settings = new Settings { EmoBackground = "rainy-neon-alley", EmoEffects = [] };
        var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0, Width = 1280, Height = 800, Title = "FRAME Emo diagnostics" };
        using var atmosphere = new CaptureAtmosphere(window, new SandDriftLayer(), new DuneFlowLayer()) { Desert = false };
        var context = new ModeContext(window, atmosphere, null!, () => settings, change => change(settings)) { Seed = 7 };
        var scene = new EmoScene(context, diagnostics: false);
        var grid = new Grid(); grid.Children.Add(scene.Root);
        grid.Children.Add(new ContentControl { Content = scene.Hero, Width = 640, Margin = new Thickness(254, 66, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        window.Content = grid; window.Show();
        scene.Enter(); atmosphere.Attach(scene); atmosphere.Enable(true); scene.PageChanged(true);
        await Task.Delay(1500);
        using var self = Process.GetCurrentProcess();
        var phases = new List<object>();
        async Task Measure(string name, double seconds)
        {
            self.Refresh(); double cpu = self.TotalProcessorTime.TotalSeconds; int updates = atmosphere.ClientUpdates, frames = atmosphere.Frames, gc0 = GC.CollectionCount(0);
            long bytes = GC.GetTotalAllocatedBytes(); scene.stepTicks = 0; scene.steps = 0; atmosphere.ResetLargestStep(); var start = DateTime.UtcNow; var watch = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            self.Refresh(); double wall = watch.Elapsed.TotalSeconds;
            phases.Add(new
            {
                phase = name, startUtc = start.ToString("O"), endUtc = DateTime.UtcNow.ToString("O"), seconds = Math.Round(wall, 2),
                cpuPercentOfOneCore = Math.Round((self.TotalProcessorTime.TotalSeconds - cpu) / wall * 100, 2), cpuPercentOfMachine = Math.Round((self.TotalProcessorTime.TotalSeconds - cpu) / wall / Environment.ProcessorCount * 100, 3),
                windowVisible = window.IsVisible, onScreen = atmosphere.IsOnScreen, clockRunning = atmosphere.Running, quoteTimerRunning = scene.rotation.Running,
                sceneUpdatesPerSecond = Math.Round((atmosphere.ClientUpdates - updates) / wall, 1), renderCallbacksPerSecond = Math.Round((atmosphere.Frames - frames) / wall, 1),
                effectStepMsAverage = scene.steps == 0 ? 0 : Math.Round(Stopwatch.GetElapsedTime(0, scene.stepTicks).TotalMilliseconds / scene.steps, 4), largestClockStepSeconds = Math.Round(atmosphere.LargestStep, 4),
                allocatedKBPerSecond = Math.Round((GC.GetTotalAllocatedBytes() - bytes) / 1024d / wall, 1), gen0Collections = GC.CollectionCount(0) - gc0,
            });
        }
        string[][] sets = [[], ["petals"], ["rain"], ["bats"], ["fog"], EmoOptions.Effects];
        foreach (var set in sets) { scene.ApplyEffects(set); await Task.Delay(1500); await Measure(set.Length == 0 ? "no effects" : string.Join("+", set), 10); }
        // Library/Settings: a still background under the scrim; the effects stop.
        scene.PageChanged(false); await Task.Delay(800); await Measure("all effects, Library page (still)", 6); scene.PageChanged(true);
        // Hidden as in the tray: the clock and the quote timer stop; shown again: they resume without a catch-up step.
        double remainingBefore = scene.rotation.Remaining.TotalSeconds;
        window.Hide(); await Task.Delay(800); await Measure("all effects, window hidden (tray)", 8);
        double remainingHidden = scene.rotation.Remaining.TotalSeconds;
        window.Show(); await Measure("all effects, shown again (first 4 s)", 4);
        // Fade of a quote change (window visible): opacity and text sampled every 50 ms.
        var samples = new List<object>(); var clock = Stopwatch.StartNew(); scene.rotation.AdvanceNow();
        while (clock.Elapsed.TotalSeconds < 1.1) { samples.Add(new { t = Math.Round(clock.Elapsed.TotalSeconds, 2), opacity = Math.Round(scene.quote.Opacity, 3), quote = scene.quote.Current }); await Task.Delay(50); }
        scene.Leave(); atmosphere.Detach(scene); bool afterLeave = atmosphere.Running;
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            window = new { window.ActualWidth, window.ActualHeight, dpi = VisualTreeHelper.GetDpi(window).DpiScaleX }, settings.EmoBackground, reducedMotion = atmosphere.ReducedMotion, processorCount = Environment.ProcessorCount,
            phases, quoteRemainingBeforeHideSeconds = Math.Round(remainingBefore, 2), quoteRemainingAfterHiddenPhaseSeconds = Math.Round(remainingHidden, 2), clockRunningAfterLeave = afterLeave, fade = samples,
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    });

}
