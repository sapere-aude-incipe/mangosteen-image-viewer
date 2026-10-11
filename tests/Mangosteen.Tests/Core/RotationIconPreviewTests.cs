using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mangosteen.Icons;
using Mangosteen.Navigation;
using IconPath = System.Windows.Shapes.Path;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RotationIconPreviewTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task Render_Rotation_Alternatives_In_The_Real_Toolbar(bool dark) => StaTest.RunAsync(() =>
    {
        var directory = Environment.GetEnvironmentVariable("MANGOSTEEN_TEST_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return Task.CompletedTask;
        var window = new MainWindow(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = false, IsDarkMode = dark });
        var root = (Grid)window.Content;
        root.Measure(new Size(520, 600));
        root.Arrange(new Rect(0, 0, 520, 600));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var navigator = (ImageNavigator)typeof(MainWindow).GetField("_navigator", flags)!.GetValue(window)!;
        navigator.LoadSingle(@"C:\Photo.png");
        typeof(MainWindow).GetMethod("UpdateImagePositionText", flags)!.Invoke(window, null);
        foreach (var name in new[] { "ShowInFolderButton", "ZoomPopupButton", "ActualPixelsButton", "PreviousButton", "NextButton", "RotateLeftButton", "RotateRightButton", "DeleteButton" })
            ((Button)window.FindName(name)).IsEnabled = true;
        var left = (Button)window.FindName("RotateLeftButton");
        var right = (Button)window.FindName("RotateRightButton");
        var toolbar = (FrameworkElement)window.FindName("ToolbarHost");
        var drawing = new DrawingVisual();
        var brush = left.Foreground;
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, 520, 276));
            for (var variant = 0; variant < 3; variant++)
            {
                var label = variant switch { 0 => "A  Smaller circle", 1 => "B  Rounded square", _ => "C  Open circular arrow" };
                if (variant == 0)
                {
                    var geometry = Geometry.Parse(FluentCircle);
                    left.Content = Glyph(geometry, brush, true, 20, 18);
                    var mirrored = geometry.Clone();
                    mirrored.Transform = new ScaleTransform(-1, 1, 10, 10);
                    right.Content = Glyph(mirrored, brush, true, 20, 18);
                }
                else if (variant == 1)
                {
                    left.Content = ToolbarIcon.Create(ToolbarIconKind.RotateLeft, brush);
                    right.Content = ToolbarIcon.Create(ToolbarIconKind.RotateRight, brush);
                }
                else
                {
                    left.Content = Glyph(Geometry.Parse("M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5"), brush, false, 24, 17);
                    right.Content = Glyph(Geometry.Parse("M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8 M21 3v5h-5"), brush, false, 24, 17);
                }
                root.UpdateLayout();
                Assert.AreEqual(394, ((Border)window.FindName("NavigationDock")).ActualWidth, 0.01);
                context.DrawText(new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12, brush, 2), new Point(63, 8 + variant * 88));
                var bitmap = new RenderTargetBitmap(1040, 128, 192, 192, PixelFormats.Pbgra32);
                var snapshot = new DrawingVisual();
                using (var snapshotContext = snapshot.RenderOpen())
                {
                    snapshotContext.DrawRectangle(window.Background, null, new Rect(0, 0, 520, 64));
                    snapshotContext.DrawRectangle(new VisualBrush(toolbar), null, new Rect(0, 0, 520, 64));
                }
                bitmap.Render(snapshot);
                var pixels = new byte[1040 * 128 * 4];
                bitmap.CopyPixels(pixels, 1040 * 4, 0);
                var brightPixels = 0;
                for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                    if (dark ? pixels[pixel] > 160 && pixels[pixel + 1] > 160 && pixels[pixel + 2] > 160
                        : pixels[pixel] < 100 && pixels[pixel + 1] < 100 && pixels[pixel + 2] < 100)
                        brightPixels++;
                Assert.IsGreaterThan(100, brightPixels, "Toolbar glyphs must be visible in each preview.");
                context.DrawImage(bitmap, new Rect(0, 28 + variant * 88, 520, 64));
            }
        }
        var comparison = new RenderTargetBitmap(1040, 552, 192, 192, PixelFormats.Pbgra32);
        comparison.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(comparison));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, dark ? "rotation-options-dark.png" : "rotation-options-light.png"));
        encoder.Save(stream);
        return Task.CompletedTask;
    });

    private static Viewbox Glyph(Geometry geometry, Brush brush, bool filled, double canvasSize, double size) => new()
    {
        Width = size, Height = size,
        Child = new Canvas
        {
            Width = canvasSize, Height = canvasSize,
            Children = { new IconPath { Data = geometry, Fill = filled ? brush : Brushes.Transparent,
                Stroke = filled ? null : brush, StrokeThickness = 2.1, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round } }
        }
    };

    private const string FluentCircle = "M16 10C16 6.68629 13.3137 4 10 4C8.47869 4 7.08928 4.56592 6.03111 5.5H7.25C7.66421 5.5 8 5.83579 8 6.25C8 6.66421 7.66421 7 7.25 7H4.25C3.83579 7 3.5 6.66421 3.5 6.25V3.25C3.5 2.83579 3.83579 2.5 4.25 2.5C4.66421 2.5 5 2.83579 5 3.25V4.40987C6.32643 3.22274 8.07867 2.5 10 2.5C14.1421 2.5 17.5 5.85786 17.5 10C17.5 14.1421 14.1421 17.5 10 17.5C5.85786 17.5 2.5 14.1421 2.5 10C2.5 9.90715 2.50169 9.81467 2.50505 9.72258C2.52012 9.30864 2.86791 8.9853 3.28185 9.00038C3.69579 9.01546 4.01913 9.36324 4.00405 9.77718C4.00136 9.85109 4 9.92537 4 10C4 13.3137 6.68629 16 10 16C13.3137 16 16 13.3137 16 10Z";
}
