using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;

namespace Frame.App;

public partial class MainWindow
{
    sealed record CaptureFile(string Path, string Name, long Bytes, DateTime Date, bool Image);
    async void SearchChanged(object sender, TextChangedEventArgs e) { if (loaded) { libraryLimit = 50; await RefreshLibrary(); } }
    async void LibraryChanged(object sender, SelectionChangedEventArgs e) { if (loaded) { libraryLimit = 50; await RefreshLibrary(); } }
    async void MoreClick(object sender, RoutedEventArgs e) { libraryLimit += 50; await RefreshLibrary(); }
    async Task RefreshLibrary()
    {
        int version = ++libraryVersion; string search = SearchBox.Text; string directory = settings.OutputDirectory; int filter = LibraryFilter.SelectedIndex, sort = Math.Max(0, LibrarySort.SelectedIndex);
        try
        {
            // File-system listing only (no decoding); duration sort reads already-cached probe results, unknown last.
            var (files, durations) = await Task.Run(() =>
            {
                var list = ReplayFolders.LibraryFiles(directory).Where(p => !p.Contains(".partial.") && (Path.GetExtension(p).Equals(".mp4", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase)))
                    .Select(p => new FileInfo(p)).Where(f => f.Exists && f.Length > 0).Select(f => new CaptureFile(f.FullName, f.Name, f.Length, f.LastWriteTime, f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))).OrderByDescending(f => f.Date).ToArray();
                var known = new Dictionary<string, double>();
                if (sort == 3) foreach (var f in list) if (!f.Image && CachedDuration(f) is double d) known[f.Path] = d;
                return (list, known);
            });
            if (version != libraryVersion) return;
            IEnumerable<CaptureFile> view = files.Where(f => f.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && (filter == 0 || (filter == 1 ? !f.Image : f.Image)));
            view = sort switch
            {
                1 => view.OrderBy(f => f.Date),
                2 => view.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
                3 => view.OrderByDescending(f => durations.TryGetValue(f.Path, out var d) ? d : -1),
                4 => view.OrderByDescending(f => f.Bytes),
                _ => view
            };
            var filtered = view.ToArray();
            // Rows (and their previews) exist only while Library is shown; a new listing replaces the pending preview work.
            StopPreviews(); LibraryList.Children.Clear(); if (LibraryPage.Visibility == Visibility.Visible) foreach (var f in filtered.Take(libraryLimit)) LibraryList.Children.Add(FileRow(f));
            if (filtered.Length == 0) LibraryList.Children.Add(Themed(new TextBlock { Text = files.Length == 0 ? "Saved recordings and clips will appear here." : search.Length > 0 ? $"Nothing matches “{search}”." : "No captures of this type yet.", Margin = new Thickness(0, 20, 0, 20) }, TextBlock.ForegroundProperty, "Muted"));
            MoreButton.Visibility = filtered.Length > libraryLimit ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e) { Notify("Library could not be read: " + e.Message); DiagnosticLog.Error("library", "Library could not be read", e); }
    }
    string MetaPath(CaptureFile file) => Path.Combine(data, "Thumbnails", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Path + file.Date.Ticks)))[..24] + ".json");
    double? CachedDuration(CaptureFile file)
    {
        try
        {
            var path = MetaPath(file); if (!File.Exists(path)) return null;
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentNullException) { return null; }
    }
    // Actions check the file first, so a moved or deleted capture gives clear feedback instead of an error dialog.
    bool Available(CaptureFile file) { if (File.Exists(file.Path)) return true; Notify("That capture is no longer available. The list was refreshed."); _ = RefreshLibrary(); return false; }
    void Play(CaptureFile file) { if (Available(file)) try { Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { Notify("No app is set up to open this file."); } }
    // Library row (the Capture page's former recent list was never shown and is no longer built, so no preview work happens
    // outside Library). Brushes are resource references, so a theme change or preview restyles existing rows.
    FrameworkElement FileRow(CaptureFile file)
    {
        const double thumbW = 128, thumbH = 72;
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(thumbW + 16) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var card = new Border { Child = row, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(3), Background = Brushes.Transparent };
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        var image = new Image { Width = thumbW, Height = thumbH, Stretch = Stretch.UniformToFill };
        // Placeholder under the image until the preview is ready ("No preview" when it cannot be made; the file still opens).
        var placeholder = Themed(new TextBlock { Text = file.Image ? "" : "Preparing preview…", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Muted");
        var face = new Grid(); face.Children.Add(placeholder); face.Children.Add(image);
        var thumb = Themed(new Border { CornerRadius = new CornerRadius(4), Width = thumbW, Height = thumbH, ClipToBounds = true, Child = face, HorizontalAlignment = HorizontalAlignment.Left }, Border.BackgroundProperty, "Raised"); row.Children.Add(thumb);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        info.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(file.Name), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 });
        var details = Themed(new TextBlock { Text = $"{(file.Image ? "PNG" : "VIDEO")}   ·   {file.Bytes / 1048576d:0.0} MB   ·   {file.Date:g}", FontSize = 10, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }, TextBlock.ForegroundProperty, "Muted");
        info.Children.Add(details);
        // Replays routed into a game folder by ReplayFolders show that game; no new detection.
        string? folder = Path.GetDirectoryName(file.Path);
        if (folder != null && !string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(settings.OutputDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            info.Children.Add(Themed(new TextBlock { Text = Path.GetFileName(folder).ToUpperInvariant(), FontSize = 9, Margin = new Thickness(0, 5, 0, 0) }, TextBlock.ForegroundProperty, "Accent"));
        Grid.SetColumn(info, 1); row.Children.Add(info);
        QueuePreview(new(file, image, placeholder, details, card));
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (file.Image) actionsRow.Children.Add(ActionButton("Open", () => Play(file)));
        else { actionsRow.Children.Add(ActionButton("Trim", () => { if (Available(file)) OpenTrim(file.Path); })); actionsRow.Children.Add(ActionButton("Crop for social", () => { if (Available(file)) OpenShare(file.Path); })); }
        {
            var more = new Button { Content = "•••", Padding = new Thickness(10) }; TooltipPolish.Describe(more, file.Image ? "Show, rename, copy or remove" : "Play, show, rename, copy or remove"); var menu = new ContextMenu();
            void Item(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => { if (File.Exists(file.Path) || name == "Copy file path") action(); else Available(file); }; menu.Items.Add(item); }
            if (!file.Image) Item("Play clip", () => Play(file));
            Item("Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + file.Path + "\"", UseShellExecute = true }));
            Item("Copy file path", () => Clipboard.SetText(file.Path)); Item("Rename", () => Rename(file));
            Item("Copy file", () => Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { file.Path }));
            menu.Items.Add(new Separator());
            Item(file.Image ? "Delete image" : "Delete clip", () => ConfirmDelete(file));
            more.ContextMenu = menu; more.Click += (_, _) => { menu.PlacementTarget = more; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; }; card.ContextMenu = menu; actionsRow.Children.Add(more);
            menu.Opened += (_, _) => (menu.Items.OfType<MenuItem>().FirstOrDefault())?.Focus(); // keyboard users land on the first action
        }
        Grid.SetColumn(actionsRow, 2); row.Children.Add(actionsRow);
        card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Play(file); e.Handled = true; } };
        Point origin = default; card.PreviewMouseLeftButtonDown += (_, e) => origin = e.GetPosition(card); card.MouseMove += (_, e) => { var point = e.GetPosition(card); if (e.LeftButton == MouseButtonState.Pressed && (Math.Abs(point.X - origin.X) > 12 || Math.Abs(point.Y - origin.Y) > 12) && File.Exists(file.Path)) DragDrop.DoDragDrop(card, new DataObject(DataFormats.FileDrop, new[] { file.Path }), DragDropEffects.Copy); };
        return card;
    }
    static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement { element.SetResourceReference(property, key); return element; }
    // Library previews (0.5.5): on demand only. Each row queues its thumbnail and details; a pump runs only while the Library
    // page is shown and FRAME is visible (not minimized or in the tray), takes rows inside the visible part of the list first
    // (then top to bottom) and runs at most two FFmpeg/ffprobe processes at below-normal priority. It also runs while recording:
    // before 0.5.5 no preview was generated while Shadow Recording ran, so newer clips kept an empty frame. Leaving Library,
    // hiding FRAME or a new listing cancels what has not started; nothing scans, polls or generates in the background.
    // Cache entries are checked when loaded: an empty or undecodable PNG/JSON is deleted and made again. New previews are
    // written to a temporary name and renamed, so an interrupted run never leaves a broken cache entry.
    sealed record PreviewRequest(CaptureFile File, Image Image, TextBlock Placeholder, TextBlock Details, FrameworkElement Row);
    readonly List<PreviewRequest> previewQueue = [];
    readonly HashSet<string> previewFailuresLogged = [];
    CancellationTokenSource? previewRun;
    int previewWorkers; bool previewHooks;
    const int PreviewWorkers = 2;
    internal int PreviewsStarted { get; private set; } // diagnostics: preview jobs started (unchanged while Library is closed)
    internal int PreviewsPending => previewQueue.Count + previewWorkers;
    bool PreviewsAllowed => LibraryPage.Visibility == Visibility.Visible && IsVisible && WindowState != WindowState.Minimized;
    void QueuePreview(PreviewRequest request)
    {
        if (!previewHooks)
        {
            previewHooks = true;
            PageScroll.ScrollChanged += (_, e) => { if (e.VerticalChange != 0) PumpPreviews(); };
            IsVisibleChanged += (_, _) => { if (IsVisible) PumpPreviews(); else PausePreviews(); };
            StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) PausePreviews(); else PumpPreviews(); };
        }
        previewQueue.Add(request); PumpPreviews();
    }
    void PumpPreviews()
    {
        if (!PreviewsAllowed) return;
        previewRun ??= new();
        while (previewWorkers < PreviewWorkers && NextPreview() is { } next) { previewWorkers++; PreviewsStarted++; previewStartedNames.Add(next.File.Name); if (previewStartedNames.Count > 500) previewStartedNames.RemoveRange(0, 250); _ = RunPreview(next, previewRun.Token); }
    }
    // Rows inside the visible part of the list first (in list order), otherwise the next row from the top.
    PreviewRequest? NextPreview()
    {
        if (previewQueue.Count == 0) return null;
        int pick = previewQueue.FindIndex(r => InView(r.Row)); if (pick < 0) pick = 0;
        var next = previewQueue[pick]; previewQueue.RemoveAt(pick); return next;
    }
    bool InView(FrameworkElement row)
    {
        if (!row.IsVisible || row.ActualHeight <= 0) return false;
        try { double top = row.TranslatePoint(new Point(), PageScroll).Y; return top + row.ActualHeight > 0 && top < PageScroll.ActualHeight; }
        catch (InvalidOperationException) { return false; }
    }
    void PausePreviews() { previewRun?.Cancel(); previewRun = null; } // the queue stays; work resumes when Library is visible again
    void StopPreviews() { PausePreviews(); previewQueue.Clear(); }
    async Task RunPreview(PreviewRequest request, CancellationToken cancel)
    {
        try
        {
            if (cancel.IsCancellationRequested) { Requeue(request); return; }
            if (!request.File.Image && !ready) { request.Placeholder.Text = "Preview needs FFmpeg"; return; } // Probe refreshes Library once FFmpeg is ready
            if (await LoadThumbnail(request, cancel)) await LoadDetails(request.File, request.Details);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or ArgumentException) { PreviewFailed(request, e.Message); }
        finally { previewWorkers--; if (!cancel.IsCancellationRequested) PumpPreviews(); }
    }
    void Requeue(PreviewRequest request) { if (LibraryList.Children.Contains(request.Row)) previewQueue.Insert(0, request); }
    void PreviewFailed(PreviewRequest request, string reason)
    {
        request.Placeholder.Text = "No preview";
        if (previewFailuresLogged.Add(request.File.Path)) DiagnosticLog.Warning("library-preview", $"No preview for {request.File.Name}: {reason.Trim()}");
    }
    static string PreviewKey(CaptureFile file) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Path + file.Date.Ticks)))[..24];
    // Returns true when the row has its preview (or it is an image row), false when it was postponed or failed.
    async Task<bool> LoadThumbnail(PreviewRequest request, CancellationToken cancel)
    {
        var file = request.File;
        if (file.Image) { if (await Decode(file.Path, false) is { } picture) { request.Image.Source = picture; return true; } PreviewFailed(request, "the image could not be decoded"); return false; }
        var cache = Path.Combine(data, "Thumbnails"); Directory.CreateDirectory(cache);
        string path = Path.Combine(cache, PreviewKey(file) + ".png");
        if (await Decode(path, true) is { } cached) { Show(cached); return true; }
        if (cancel.IsCancellationRequested) { Requeue(request); return false; }
        string temp = Path.Combine(cache, PreviewKey(file) + "-" + Guid.NewGuid().ToString("N")[..8] + ".tmp.png");
        try
        {
            var r = await Backend.Run(Backend.Resolve(settings.FfmpegPath), ["-v", "error", "-y", "-i", file.Path, "-frames:v", "1", "-vf", "scale=240:-1", temp], 30, ProcessPriorityClass.BelowNormal);
            if (r.Code != 0 || !File.Exists(temp) || new FileInfo(temp).Length == 0) { PreviewFailed(request, "FFmpeg could not read a frame (exit " + r.Code + "): " + r.Text); return false; }
            File.Move(temp, path, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        if (await Decode(path, true) is { } made) { Show(made); return true; }
        PreviewFailed(request, "the generated preview could not be decoded"); return false;
        void Show(BitmapSource image) { request.Image.Source = image; request.Placeholder.Text = ""; }
    }
    // Decodes a picture off the UI thread, from memory (a failed decode from a file path can keep the file open, so a broken
    // cache entry could not be replaced). A cache file that is empty or cannot be decoded is deleted (it is made again);
    // a user's own image is never touched.
    static Task<BitmapImage?> Decode(string path, bool cacheFile) => Task.Run<BitmapImage?>(() =>
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path); if (bytes.Length == 0) throw new FormatException("empty file");
            var b = new BitmapImage(); b.BeginInit(); b.DecodePixelWidth = 240; b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = new MemoryStream(bytes); b.EndInit(); b.Freeze(); return b;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FormatException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (cacheFile) try { File.Delete(path); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            return null;
        }
    });
    async Task LoadDetails(CaptureFile file, TextBlock target)
    {
        try
        {
            string detail;
            if (file.Image) detail = await Task.Run(() => { using var img = System.Drawing.Image.FromFile(file.Path); return $"{img.Width} × {img.Height}"; });
            else
            {
                string path = MetaPath(file); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                (int Width, int Height, double Seconds)? Parse(string json)
                {
                    try { using var document = System.Text.Json.JsonDocument.Parse(json); var stream = document.RootElement.GetProperty("streams")[0]; return (stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32(), double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture)); }
                    catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentNullException or IndexOutOfRangeException) { return null; }
                }
                var known = File.Exists(path) ? Parse(await File.ReadAllTextAsync(path)) : null;
                if (known == null)
                {
                    var r = await Backend.Run(Path.Combine(Path.GetDirectoryName(Backend.Resolve(settings.FfmpegPath))!, "ffprobe.exe"), ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "json", file.Path], 30, ProcessPriorityClass.BelowNormal);
                    if (r.Code != 0 || (known = Parse(r.Text)) == null) return; // keeps size and date; an unreadable cache entry is replaced below
                    await File.WriteAllTextAsync(path, r.Text);
                }
                detail = $"{known.Value.Width} × {known.Value.Height}   ·   {TimeSpan.FromSeconds(known.Value.Seconds):hh\\:mm\\:ss}";
            }
            target.Text = detail + $"   ·   {file.Bytes / 1048576d:0.0} MB   ·   {file.Date:g}";
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or TimeoutException or NotSupportedException or OutOfMemoryException or System.ComponentModel.Win32Exception) { }
    }
    // Delete = move this one file to the Recycle Bin after an explicit confirmation naming it. Success is reported only when
    // the file is really gone; locked or missing files give a clear message and the list is refreshed.
    void ConfirmDelete(CaptureFile file)
    {
        string kind = file.Image ? "image" : "clip";
        if (!QuietDialog.Confirm(this, "Delete " + kind, $"Move this {kind} to the Recycle Bin?\n\n{file.Name}\n{Path.GetDirectoryName(file.Path)}", "Move to Recycle Bin")) return; // FRAME's own dialog: no system sound
        _ = Act(async () => { Notify(await DeleteCapture(file.Path) is { } error ? error : $"Moved to the Recycle Bin · {file.Name}"); await RefreshLibrary(); });
    }
    internal static async Task<string?> DeleteCapture(string path)
    {
        if (!File.Exists(path)) return "That capture is no longer available. The list was refreshed.";
        // A file open in another app would make the shell show its own blocking dialog; detect it first and explain instead.
        try { await Task.Run(() => { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DiagnosticLog.Warning("library-delete", "File not moved to the Recycle Bin", e); return File.Exists(path) ? "Not deleted · the file is open in another app or read-only. Close it and try again." : "That capture is no longer available. The list was refreshed."; }
        try
        {
            await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or System.Security.SecurityException)
        {
            DiagnosticLog.Warning("library-delete", "File not moved to the Recycle Bin", e);
            return e is OperationCanceledException ? "Delete cancelled." : "Not deleted · the file may be open in another app. " + e.Message;
        }
        return File.Exists(path) ? "Not deleted · the file is still there (it may be in use)." : null;
    }
    void Rename(CaptureFile file)
    {
        var dialog = new Window { Title = "FRAME · Rename capture", Width = 500, Height = 190, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(20) }; var text = Field(Path.GetFileNameWithoutExtension(file.Path)); TooltipPolish.Describe(text, "Enter the new capture file name"); panel.Children.Add(text);
        panel.Children.Add(ActionButton("Rename", async () => await Act(async () => { string name = text.Text.Trim(); if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.')) throw new ArgumentException("Enter a valid filename."); File.Move(file.Path, Path.Combine(Path.GetDirectoryName(file.Path)!, name + Path.GetExtension(file.Path))); dialog.Close(); await RefreshLibrary(); }))); dialog.Content = panel; dialog.ShowDialog();
    }
}

public partial class MainWindow
{
    // Opens the local Share / Export modal; ffmpeg is touched only after this explicit request.
    internal ShareWindow? OpenShare(string path, bool modal = true, TimeRange? range = null, Window? owner = null)
    {
        if (!ready) { Notify("Crop for social needs the capture engine. Choose ffmpeg.exe in Settings."); return null; }
        if (!File.Exists(path)) { Notify("This recording is no longer available."); _ = RefreshLibrary(); return null; }
        string exe; try { exe = Backend.Resolve(settings.FfmpegPath); } catch (FileNotFoundException e) { Notify(e.Message); return null; }
        var window = new ShareWindow(owner ?? this, path, exe, Path.Combine(data, "Thumbnails"), CloneSettings, next => { SettingsStore.Save(configPath, next); settings = next; }, _ => ShareEncoder(), () => engine.Busy, range);
        if (modal) { window.ShowDialog(); _ = RefreshLibrary(); } else window.Show();
        return window;
    }
    // Clip editor; created only on request and fully released when closed.
    internal TrimWindow? OpenTrim(string path, bool modal = true)
    {
        if (!ready) { Notify("Trimming needs the capture engine. Choose ffmpeg.exe in Settings."); return null; }
        if (!File.Exists(path)) { Notify("This recording is no longer available."); _ = RefreshLibrary(); return null; }
        string exe; try { exe = Backend.Resolve(settings.FfmpegPath); } catch (FileNotFoundException e) { Notify(e.Message); return null; }
        var window = new TrimWindow(this, path, exe, Path.Combine(data, "Thumbnails"), () => engine.Busy, async clip => await RefreshLibrary(), (file, range, owner) => OpenShare(file, true, range, owner))
        {
            EncoderSource = ShareEncoder,
            TrimmedFolder = () => ReplayFolders.TrimmedClips(settings.OutputDirectory),
            LastSaveToFolder = () => settings.TrimSaveDirectory,
            RememberSaveTo = RememberTrimFolder,
            Feedback = (heading, text, success) => { if (settings.ShowSaveNotifications || !success) SaveOverlay.Present(heading, text, success); }
        };
        if (modal) window.ShowDialog(); else window.Show();
        return window;
    }
    // Persists the folder of the last successful Save to… (separate from the automatic Trimmed Clips destination).
    void RememberTrimFolder(string folder)
    {
        try { var next = CloneSettings(); next.TrimSaveDirectory = Path.GetFullPath(folder); SettingsStore.Save(configPath, next); settings = next; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { DiagnosticLog.Warning("settings", "Save to folder not remembered", e); }
    }
    // Exports favour broadly compatible H.264: probed hardware first, CPU x264 otherwise.
    Frame.Core.Encoder ShareEncoder() => encoders.FirstOrDefault(e => e.Id.StartsWith("h264_") && e.Hardware) ?? new("libx264", "CPU · H.264", false, false);
}
