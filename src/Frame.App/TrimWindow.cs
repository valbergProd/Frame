using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Frame.App;

// Clip editor: preview (WPF MediaElement), timeline with thumbnails, trim handles, save and share.
// Created only when the user chooses Trim; every media resource is released on close. Export goes through
// ShareExport/ShareExportJob with the selected TimeRange, so there is one export pipeline.
public sealed class TrimWindow : Window
{
    readonly string source, ffmpeg, cache;
    readonly Func<bool> captureBusy;
    readonly Action<string> saved;
    readonly Func<string, TimeRange?, Window, ShareWindow?> share;
    readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true, Stretch = Stretch.Uniform, Volume = .8 };
    readonly TrimTimeline timeline = new() { Margin = new Thickness(0, 18, 0, 0) };
    readonly DispatcherTimer clock = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer seekThrottle = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(60) };
    readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.Light, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock time = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    readonly TextBlock clipLength = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock previewNote = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(24) };
    readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MinHeight = 18 };
    readonly TextBox startBox = new() { Width = 104, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(8, 5, 8, 5) }, endBox = new() { Width = 104, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(8, 5, 8, 5) };
    readonly Button play = Small("Play"), toStart = Small("⇤ Start"), playSelection = Small("Play selection"), setIn = Small("Set start · I"), setOut = Small("Set end · O");
    readonly CheckBox loop = new() { Content = "Loop", FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Button save = new() { Content = "Save clip", MinWidth = 130 }, saveTo = Small("Save to…"), shareButton = new() { Content = "Crop for social", MinWidth = 100 }, cancel = Small("Cancel");
    readonly ProgressBar bar = new() { Height = 2, Minimum = 0, Maximum = 1, BorderThickness = new Thickness(0), Visibility = Visibility.Hidden, Margin = new Thickness(0, 14, 0, 10) };
    readonly WrapPanel after = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    MediaInfo? media;
    TrimSelection? selection;
    CancellationTokenSource? thumbnailWork, export;
    bool playing, selectionOnly, mediaReady, closed;
    double pendingSeek = -1;
    string? clip;
    const int ThumbnailCount = 14;

    public TrimWindow(Window owner, string source, string ffmpeg, string cache, Func<bool> captureBusy, Action<string> saved, Func<string, TimeRange?, Window, ShareWindow?> share)
    {
        this.source = source; this.ffmpeg = ffmpeg; this.cache = cache; this.captureBusy = captureBusy; this.saved = saved; this.share = share;
        Owner = owner; Title = "FRAME · Trim"; Width = 1100; Height = 760; MinWidth = 820; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Background");
        Content = FrameChrome.Wrap(this, Build(), "Trim"); // FRAME title bar and caption buttons, like the main window and Crop for social
        clock.Tick += ClockTick; seekThrottle.Tick += SeekTick;
        player.MediaOpened += Opened; player.MediaFailed += Failed; player.MediaEnded += Ended;
        timeline.Seek += SeekRequested; timeline.Edited += SyncFields;
        Loaded += (_, _) => Ready = Prepare();
        IsVisibleChanged += (_, _) => Reconcile(); StateChanged += (_, _) => Reconcile();
        PreviewKeyDown += Keys;
        Closing += (_, e) => { if (export != null) { e.Cancel = true; status.Text = "Cancel the export before closing."; } };
        Closed += (_, _) => Release();
    }

    static Button Small(string text) => new() { Content = text, FontSize = 12, Padding = new Thickness(12, 6, 12, 6), MinHeight = 30, Margin = new Thickness(0, 0, 8, 0) };
    static TextBlock Muted(string text) { var t = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; t.SetResourceReference(ForegroundProperty, "Muted"); return t; }

    UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(32, 24, 32, 22) };
        foreach (var h in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = h });
        var head = new StackPanel(); var eyebrow = new TextBlock { Text = "T R I M" }; eyebrow.SetResourceReference(StyleProperty, "Eyebrow"); head.Children.Add(eyebrow);
        title.Text = Path.GetFileNameWithoutExtension(source); head.Children.Add(title); root.Children.Add(head);

        var stage = new Grid { Margin = new Thickness(0, 16, 0, 0), Background = Brushes.Black, ClipToBounds = true };
        stage.Children.Add(player); previewNote.Foreground = new SolidColorBrush(Color.FromRgb(199, 184, 170)); previewNote.Text = "Opening preview…"; stage.Children.Add(previewNote);
        stage.MouseLeftButtonUp += (_, _) => TogglePlay();
        Grid.SetRow(stage, 1); root.Children.Add(stage);

        var controls = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal }; left.Children.Add(play); left.Children.Add(toStart); left.Children.Add(playSelection); left.Children.Add(loop); left.Children.Add(time);
        play.MinWidth = 76;
        clipLength.SetResourceReference(ForegroundProperty, "Accent"); DockPanel.SetDock(clipLength, Dock.Right); controls.Children.Add(clipLength); controls.Children.Add(left);
        TooltipPolish.Describe(play, "Play or pause · Space"); TooltipPolish.Describe(toStart, "Jump to the selection start");
        TooltipPolish.Describe(playSelection, "Play only the selected part"); TooltipPolish.Describe(loop, "Repeat the selection while previewing");
        play.Click += (_, _) => TogglePlay(); toStart.Click += (_, _) => { if (selection != null) SeekTo(selection.Start, true); };
        playSelection.Click += (_, _) => PlaySelection();
        Grid.SetRow(controls, 2); root.Children.Add(controls);

        Grid.SetRow(timeline, 3); root.Children.Add(timeline);
        TooltipPolish.Describe(timeline, "Drag the handles to trim; click to seek");

        var precision = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var fields = new StackPanel { Orientation = Orientation.Horizontal };
        fields.Children.Add(Muted("Start")); startBox.Margin = new Thickness(8, 0, 8, 0); fields.Children.Add(startBox); fields.Children.Add(setIn);
        var gap = Muted("End"); gap.Margin = new Thickness(10, 0, 0, 0); fields.Children.Add(gap); endBox.Margin = new Thickness(8, 0, 8, 0); fields.Children.Add(endBox); fields.Children.Add(setOut);
        var hint = Muted("←/→ one frame · Shift ±1 s"); DockPanel.SetDock(hint, Dock.Right); precision.Children.Add(hint); precision.Children.Add(fields);
        TooltipPolish.Describe(startBox, "Exact start, e.g. 1:05.250 · Enter applies"); TooltipPolish.Describe(endBox, "Exact end, e.g. 1:25.250 · Enter applies");
        TooltipPolish.Describe(setIn, "Start the clip at the playhead"); TooltipPolish.Describe(setOut, "End the clip at the playhead");
        foreach (var box in new[] { startBox, endBox }) { box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFields(); e.Handled = true; } }; box.LostKeyboardFocus += (_, _) => ApplyFields(); }
        setIn.Click += (_, _) => Mark(true); setOut.Click += (_, _) => Mark(false);
        Grid.SetRow(precision, 4); root.Children.Add(precision);

        var foot = new StackPanel();
        bar.SetResourceReference(ForegroundProperty, "Accent"); bar.SetResourceReference(BackgroundProperty, "Line"); foot.Children.Add(bar);
        var line = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right);
        cancel.Visibility = Visibility.Collapsed; actions.Children.Add(cancel); actions.Children.Add(saveTo); shareButton.Margin = new Thickness(0, 0, 10, 0); actions.Children.Add(shareButton); actions.Children.Add(save);
        save.SetResourceReference(BorderBrushProperty, "Accent");
        TooltipPolish.Describe(save, "Save the selection to the Trimmed Clips folder"); TooltipPolish.Describe(saveTo, "Choose a name and folder for the clip");
        TooltipPolish.Describe(shareButton, "Format the selection for a social platform; nothing is uploaded"); TooltipPolish.Describe(cancel, "Stop and remove the unfinished clip");
        save.Click += async (_, _) => await Save(null); saveTo.Click += async (_, _) => await SaveTo(); shareButton.Click += (_, _) => Share(); cancel.Click += (_, _) => export?.Cancel();
        line.Children.Add(actions); status.SetResourceReference(ForegroundProperty, "Muted"); line.Children.Add(status);
        foot.Children.Add(line); foot.Children.Add(after);
        Grid.SetRow(foot, 5); root.Children.Add(foot);
        Enable(false);
        var surface = new Border { Child = root }; surface.SetResourceReference(Border.BackgroundProperty, "Background");
        return surface;
    }

    // Diagnostics hooks for --ui-test.
    internal Task Ready { get; private set; } = Task.CompletedTask;
    internal Task Thumbs { get; private set; } = Task.CompletedTask;
    internal TrimSelection? Selection => selection;
    internal TrimTimeline Timeline => timeline;
    internal bool Playing => playing;
    internal bool ClockRunning => clock.IsEnabled;
    internal bool MediaReady => mediaReady;
    internal string Status => status.Text;
    internal void Press(Key key, ModifierKeys modifiers = ModifierKeys.None) => HandleKey(key, modifiers);

    void Enable(bool on) { foreach (var b in new UIElement[] { play, toStart, playSelection, setIn, setOut, save, saveTo, shareButton, startBox, endBox, loop, timeline }) b.IsEnabled = on; }

    async Task Prepare()
    {
        status.Text = "Reading recording…";
        try
        {
            media = await ShareExportJob.Probe(ffmpeg, source);
            if (closed) return;
            selection = new TrimSelection(media.Duration, media.Fps);
            selection.Changed += SyncFields;
            timeline.Attach(selection, ThumbnailCount);
            Enable(true); SyncFields();
            status.Text = $"{media.Width} × {media.Height} · {media.Fps:0.##} FPS · {TimeText.Format(media.Duration)}";
            player.Source = new Uri(source); player.Pause(); // opens the decoder for this window only
            Reconcile();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            status.Text = ShareExportJob.Friendly(e); previewNote.Text = "This recording cannot be edited."; Enable(false);
        }
    }

    void Opened(object? sender, RoutedEventArgs e) { mediaReady = true; previewNote.Visibility = Visibility.Collapsed; SeekTo(timeline.Position, true); }
    void Failed(object? sender, ExceptionRoutedEventArgs e)
    {
        mediaReady = false; previewNote.Visibility = Visibility.Visible;
        previewNote.Text = "Preview isn't available for this video on this PC.\nTrimming still works: use the timeline and exact times.";
        play.IsEnabled = playSelection.IsEnabled = loop.IsEnabled = false;
    }
    void Ended(object? sender, RoutedEventArgs e) { Pause(); if (media != null) SeekTo(media.Duration, true); }

    // Hidden or minimized: pause playback and stop the clock and thumbnail work; resume thumbnails when shown again.
    void Reconcile()
    {
        if (closed) return;
        bool visible = IsVisible && WindowState != WindowState.Minimized;
        if (!visible) { if (playing) Pause(); thumbnailWork?.Cancel(); return; }
        if (selection != null && media != null && timeline.Thumbnails < ThumbnailCount && (thumbnailWork == null || thumbnailWork.IsCancellationRequested)) StartThumbnails();
    }
    void StartThumbnails()
    {
        if (captureBusy()) { previewStatus("Timeline previews wait until recording stops."); return; }
        thumbnailWork?.Dispose(); thumbnailWork = new CancellationTokenSource(); var token = thumbnailWork.Token;
        Thumbs = Run();
        async Task Run()
        {
            try
            {
                await Task.Run(() => ShareExportJob.Thumbnails(ffmpeg, source, media!.Duration, ThumbnailCount, 72, cache, (i, path) =>
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelHeight = 72; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
                    Dispatcher.InvokeAsync(() => { if (!token.IsCancellationRequested) timeline.SetThumbnail(i, image); });
                }, token), token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { previewStatus("Timeline previews are unavailable; trimming still works."); }
        }
    }
    void previewStatus(string text) { if (!closed && status.Text.Length < 90) status.Text = text; }

    void TogglePlay() { if (playing) Pause(); else Play(false); }
    void Play(bool onlySelection)
    {
        if (!mediaReady || selection == null) return;
        selectionOnly = onlySelection;
        double at = player.Position.TotalSeconds;
        if (onlySelection || at >= media!.Duration - .05) SeekTo(onlySelection ? selection.Start : 0, true);
        player.Play(); playing = true; clock.Start(); play.Content = "Pause";
    }
    void PlaySelection() { if (selection != null) Play(true); }
    void Pause() { player.Pause(); playing = false; selectionOnly = false; clock.Stop(); play.Content = "Play"; ClockTick(null, EventArgs.Empty); }
    void ClockTick(object? sender, EventArgs e)
    {
        if (selection == null) return;
        double t = player.Position.TotalSeconds;
        if (playing && selectionOnly && t >= selection.End)
        {
            if (loop.IsChecked == true) { SeekTo(selection.Start, true); return; }
            Pause(); SeekTo(selection.End, true); return;
        }
        timeline.Position = t; UpdateTime(t);
    }
    void UpdateTime(double t) { if (media != null) time.Text = $"{TimeText.Format(t)} / {TimeText.Format(media.Duration)}"; }

    // Scrubbing: the latest requested time wins; MediaElement seeks at most every 60 ms.
    void SeekRequested(double t, bool final) { if (playing && !final) Pause(); SeekTo(t, final); }
    void SeekTo(double t, bool now)
    {
        if (media == null) return;
        t = Math.Clamp(t, 0, media.Duration); timeline.Position = t; UpdateTime(t);
        if (!mediaReady) return;
        if (now) { seekThrottle.Stop(); pendingSeek = -1; player.Position = TimeSpan.FromSeconds(t); }
        else { pendingSeek = t; if (!seekThrottle.IsEnabled) { player.Position = TimeSpan.FromSeconds(t); pendingSeek = -1; seekThrottle.Start(); } }
    }
    void SeekTick(object? sender, EventArgs e) { if (pendingSeek >= 0) { player.Position = TimeSpan.FromSeconds(pendingSeek); pendingSeek = -1; } else seekThrottle.Stop(); }

    void Mark(bool start) { if (selection == null) return; if (start) selection.SetStart(timeline.Position); else selection.SetEnd(timeline.Position); }
    void SyncFields()
    {
        if (selection == null) return;
        if (!startBox.IsKeyboardFocused) startBox.Text = TimeText.Format(selection.Start);
        if (!endBox.IsKeyboardFocused) endBox.Text = TimeText.Format(selection.End);
        clipLength.Text = "Clip " + TimeText.Format(selection.Length);
    }
    void ApplyFields()
    {
        if (selection == null) return;
        if (TimeText.TryParse(startBox.Text, out double s) && TimeText.TryParse(endBox.Text, out double e))
        {
            try { selection.Set(Math.Min(s, media!.Duration), Math.Min(e, media.Duration)); status.Text = ""; }
            catch (ArgumentException x) { status.Text = x.Message; }
        }
        else status.Text = "Use times like 1:05.250.";
        startBox.Text = TimeText.Format(selection.Start); endBox.Text = TimeText.Format(selection.End); clipLength.Text = "Clip " + TimeText.Format(selection.Length);
    }

    void Keys(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (export == null) Close(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) return;
        e.Handled = HandleKey(e.Key, Keyboard.Modifiers);
    }
    bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (selection == null || export != null) return false;
        double step = modifiers.HasFlag(ModifierKeys.Shift) ? 1 : selection.FrameSeconds;
        switch (key)
        {
            case Key.Space: TogglePlay(); return true;
            case Key.Left: if (playing) Pause(); SeekTo(selection.Snap(timeline.Position - step), true); return true;
            case Key.Right: if (playing) Pause(); SeekTo(selection.Snap(timeline.Position + step), true); return true;
            case Key.I: Mark(true); return true;
            case Key.O: Mark(false); return true;
            case Key.Home: SeekTo(selection.Start, true); return true;
            case Key.End: SeekTo(selection.End, true); return true;
        }
        return false;
    }

    // Native Save As: shows folders and files, lets the user name the clip. Starts in the last Save to… folder that still
    // exists, else the Trimmed Clips folder. Cancel changes nothing. The remembered folder never affects Save clip.
    async Task SaveTo()
    {
        if (media == null || selection == null || export != null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save clip as", InitialDirectory = SaveToStart(), FileName = SuggestedName(), DefaultExt = ".mp4", AddExtension = true,
            Filter = "MP4 video (*.mp4)|*.mp4|All files (*.*)|*.*", OverwritePrompt = true, CheckPathExists = true, ValidateNames = true, AddToRecent = false
        };
        if (dialog.ShowDialog(this) != true) return;
        string chosen = dialog.FileName;
        if (!chosen.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) chosen += ".mp4"; // the export is always MP4
        await Save(Path.GetDirectoryName(chosen)!, chosen, File.Exists(chosen));
    }
    internal string SaveToStart()
    {
        string? last = LastSaveToFolder?.Invoke();
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last)) return last;
        string? trimmed = TrimmedFolder?.Invoke();
        return trimmed != null && Directory.Exists(trimmed) ? trimmed : Path.GetDirectoryName(source)!;
    }
    // Readable default for Save to…: source name without the FRAME prefix, plus "clip".
    internal string SuggestedName() => Path.GetFileNameWithoutExtension(source).Replace("FRAME ", "").Trim() + " clip.mp4";

    // Save clip: automatic destination <capture folder>\Trimmed Clips (created when needed), unique name, original untouched.
    // A partial selection is re-encoded for exact boundaries (see ExportPlanner.Plan). While Shadow or manual capture runs,
    // the export uses CPU x264 at Idle priority with two threads (user decision 0.5.4) so the capture encoder keeps the GPU.
    async Task Save(string? folder, string? target = null, bool replace = false)
    {
        if (media == null || selection == null || export != null) return;
        if (!File.Exists(source)) { status.Text = "The recording is no longer available."; return; }
        if (playing) Pause();
        export = new CancellationTokenSource(); Enable(false); cancel.Visibility = Visibility.Visible; bar.Visibility = Visibility.Visible; bar.Value = 0; after.Visibility = Visibility.Collapsed;
        status.Text = "Saving clip…";
        try
        {
            var range = selection.IsWhole ? null : selection.Range;
            folder ??= TrimmedFolder?.Invoke() ?? Path.GetDirectoryName(source)!;
            var request = new ExportRequest(source, media, 0, 0, 0, Framing.Original, .5, .5, ExportQuality.High, 0, true, folder, range);
            var plan = ExportPlanner.Plan(request);
            bool lowImpact = !plan.StreamCopy && captureBusy();
            var encoder = lowImpact ? new Frame.Core.Encoder("libx264", "CPU · H.264", false, false) : ShareEncoderFor();
            LowImpactUsed = lowImpact;
            string mode = lowImpact ? " · low-impact while recording" : "";
            var progress = new Progress<(ExportStage Stage, double Fraction)>(p => { if (closed) return; bar.Value = p.Fraction; status.Text = p.Stage switch { ExportStage.Preparing => "Saving clip · preparing…", ExportStage.Encoding => $"Saving clip · {p.Fraction:P0}{mode}", ExportStage.Finalizing => "Checking the clip…", _ => status.Text }; });
            clip = await ShareExportJob.Run(ffmpeg, request, encoder, Path.GetFileNameWithoutExtension(source).Replace("FRAME ", "") + " · clip", progress, export.Token, target, replace, lowImpact);
            if (target != null) RememberSaveTo?.Invoke(Path.GetDirectoryName(clip)!);
            string where = target == null && TrimmedFolder != null ? " · in " + ReplayFolders.TrimmedFolder : "";
            if (!closed) { status.Text = "Saved · " + Path.GetFileName(clip) + where + (plan.StreamCopy ? " · copied without re-encoding" : ""); ShowAfter(); }
            Feedback?.Invoke("Clip saved", Path.GetFileName(clip), true);
            saved(clip);
        }
        catch (Exception e)
        {
            // Never silent: every failure is shown in the editor, logged, and reported in the top-right note.
            bool cancelled = e is OperationCanceledException;
            string reason = ShareExportJob.Friendly(e);
            if (!closed) status.Text = cancelled ? "Clip cancelled. The unfinished file was removed." : "Clip not saved · " + reason;
            if (!cancelled) { DiagnosticLog.Error("trim", "Clip not saved: " + e.Message, e); Feedback?.Invoke("Clip not saved", reason, false); }
        }
        finally { export?.Dispose(); export = null; if (!closed) { Enable(true); cancel.Visibility = Visibility.Collapsed; bar.Visibility = Visibility.Hidden; } }
    }
    internal Func<Frame.Core.Encoder>? EncoderSource { get; init; }
    internal Func<string>? TrimmedFolder { get; init; }
    internal Func<string?>? LastSaveToFolder { get; init; }
    internal Action<string>? RememberSaveTo { get; init; }
    internal Action<string, string, bool>? Feedback { get; init; }
    internal bool LowImpactUsed { get; private set; }
    Frame.Core.Encoder ShareEncoderFor() => EncoderSource?.Invoke() ?? new("libx264", "CPU · H.264", false, false);
    internal Task SaveForTest() => Save(null);
    internal Task SaveAsForTest(string target, bool replace = false) => Save(Path.GetDirectoryName(target)!, target, replace);
    internal string? SavedClip => clip;

    void ShowAfter()
    {
        after.Children.Clear();
        void Add(string text, string tip, Action action) { var b = Small(text); TooltipPolish.Describe(b, tip); b.Click += (_, _) => { try { action(); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = "That action failed."; } }; after.Children.Add(b); }
        Add("Open clip", "Play the saved clip", () => Process.Start(new ProcessStartInfo(clip!) { UseShellExecute = true }));
        Add("Show in folder", "Select the clip in File Explorer", () => Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + clip + "\"", UseShellExecute = true }));
        Add("Crop for social", "Format the saved clip for a social platform", () => { if (clip != null) share(clip, null, this); });
        after.Visibility = Visibility.Visible;
    }

    // Shares the selection directly: the Share window receives the original file plus the range, so there is no intermediate file.
    void Share()
    {
        if (selection == null) return;
        if (playing) Pause();
        share(source, selection.Range, this);
    }

    void Release()
    {
        closed = true;
        thumbnailWork?.Cancel(); thumbnailWork?.Dispose(); thumbnailWork = null;
        clock.Stop(); seekThrottle.Stop(); clock.Tick -= ClockTick; seekThrottle.Tick -= SeekTick;
        player.MediaOpened -= Opened; player.MediaFailed -= Failed; player.MediaEnded -= Ended;
        try { player.Stop(); player.Close(); } catch (InvalidOperationException) { }
        player.Source = null;
        if (selection != null) selection.Changed -= SyncFields;
        timeline.Detach(); PreviewKeyDown -= Keys;
    }
    internal bool Released => closed && player.Source == null && !clock.IsEnabled && !seekThrottle.IsEnabled && thumbnailWork == null;
}
