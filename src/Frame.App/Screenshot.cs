using D = System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Frame.App;

public static class Screenshot
{
    public static D.Bitmap Grab(Box bounds)
    {
        var bmp = new D.Bitmap(bounds.Width, bounds.Height, D.Imaging.PixelFormat.Format32bppArgb);
        try { using var g = D.Graphics.FromImage(bmp); g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new D.Size(bounds.Width, bounds.Height), D.CopyPixelOperation.SourceCopy); return bmp; }
        catch { bmp.Dispose(); throw; }
    }
    public static BitmapSource Source(D.Bitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, D.Imaging.ImageFormat.Png); stream.Position = 0;
        var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze(); return source;
    }
    public static string Save(Box bounds, string directory)
    {
        using var bmp = Grab(bounds); string path = Files.Reserve(directory, "Screenshot", "png");
        try { using var stream = File.Open(path, FileMode.Truncate); bmp.Save(stream, D.Imaging.ImageFormat.Png); stream.Flush(true); }
        catch { File.Delete(path); throw; }
        System.Windows.Clipboard.SetImage(Source(bmp)); return path;
    }
}

public sealed class AnnotationWindow : Window
{
    readonly InkCanvas canvas;
    readonly BitmapImage original;
    readonly string path;
    public AnnotationWindow(string path)
    {
        this.path = path; Title = "FRAME · Annotate"; Width = 1000; Height = 720; Background = (Brush)Application.Current.Resources["Background"];
        original = new(); original.BeginInit(); original.CacheOption = BitmapCacheOption.OnLoad; original.UriSource = new Uri(path); original.EndInit();
        var root = new DockPanel { Margin = new Thickness(20) }; var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        canvas = new InkCanvas { Width = original.PixelWidth, Height = original.PixelHeight, Background = new ImageBrush(original), DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Cyan, Width = 5, Height = 5 } };
        void Button(string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(0, 0, 10, 0) }; b.Click += (_, _) => action(); actions.Children.Add(b); }
        Button("Pen", () => { canvas.EditingMode = InkCanvasEditingMode.Ink; canvas.DefaultDrawingAttributes.IsHighlighter = false; canvas.DefaultDrawingAttributes.Width = canvas.DefaultDrawingAttributes.Height = 5; }); Button("Highlighter", () => { canvas.EditingMode = InkCanvasEditingMode.Ink; canvas.DefaultDrawingAttributes.IsHighlighter = true; canvas.DefaultDrawingAttributes.Width = canvas.DefaultDrawingAttributes.Height = 22; });
        Button("Eraser", () => canvas.EditingMode = InkCanvasEditingMode.EraseByStroke); Button("Undo", () => { if (canvas.Strokes.Count > 0) canvas.Strokes.RemoveAt(canvas.Strokes.Count - 1); });
        Button("Save a copy", Save); Button("Copy", () => Clipboard.SetImage(Render()));
        root.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform }); Content = root;
    }
    RenderTargetBitmap Render() { var result = new RenderTargetBitmap(original.PixelWidth, original.PixelHeight, 96, 96, PixelFormats.Pbgra32); result.Render(canvas); return result; }
    void Save() { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render())); var output = Files.Reserve(Path.GetDirectoryName(path)!, "Annotated", "png"); using var f = File.Open(output, FileMode.Truncate); encoder.Save(f); Title = "FRAME · Copy saved"; }
}
