using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Mangosteen.Core;
using Mangosteen.Decoding;
using Mangosteen.Navigation;
using Mangosteen.Localization;
using Mangosteen.Rendering;
using Mangosteen.Rendering.Advanced;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // WPF WindowChrome uses process-wide property-descriptor caches.
public sealed class ViewerToolbarTests
{
    [TestMethod]
    [DataRow("1456%", "en-US", 14.56)]
    [DataRow(" 125.5 % ", "en-US", 1.255)]
    [DataRow("125,5%", "nb-NO", 1.255)]
    [DataRow("100", "en-US", 1.0)]
    public void Zoom_Percentage_Uses_The_Current_Culture(string text, string culture, double expected)
    {
        Assert.IsTrue(MainWindow.TryParseZoomPercentage(text, CultureInfo.GetCultureInfo(culture), out var zoom));
        Assert.AreEqual(expected, zoom, 0.00001);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("1e999")]
    [DataRow("0%")]
    [DataRow("-100%")]
    [DataRow("100%%")]
    [DataRow("abc")]
    public void Invalid_Zoom_Text_Is_Rejected(string text)
    {
        Assert.IsFalse(MainWindow.TryParseZoomPercentage(text, CultureInfo.InvariantCulture, out _));
    }

    [TestMethod]
    [DataRow(0.1267, "13%")]
    [DataRow(1.254, "125%")]
    [DataRow(14.5678, "1457%")]
    public Task Zoom_Display_Rounds_Without_Changing_The_Image_Scale(double zoom, string expected) => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        using var bitmap = new SKBitmap(100, 100);
        using var image = new DecodedImage(new ImageMetadata(@"C:\test.png", 100, 100, 1, "test"),
            [new DecodedFrame(SKImage.FromBitmap(bitmap), TimeSpan.Zero)], true);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_image", flags)!.SetValue(window, image);
        var navigator = (ImageNavigator)typeof(MainWindow).GetField("_navigator", flags)!.GetValue(window)!;
        navigator.LoadSingle(image.Metadata.Path);
        var state = (ViewerState)typeof(MainWindow).GetField("_viewerState", flags)!.GetValue(window)!;
        state.SetViewport(new PixelSize(10, 10));
        state.SetImage(100, 100, fitToWindow: true);
        state.ZoomAt(zoom / state.Zoom, default);

        typeof(MainWindow).GetMethod("UpdateZoomText", flags)!.Invoke(window, null);
        Assert.AreEqual(expected, ((TextBox)window.FindName("ZoomText")).Text);
        typeof(MainWindow).GetMethod("CommitZoomText", flags)!.Invoke(window, null);
        Assert.AreEqual(zoom, state.Zoom, 0.0000001);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Toolbar_Disables_Empty_Actions_Consistently_And_Allows_Keyboard_Focus() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        foreach (var name in new[] { "PreviousButton", "NextButton", "ActualPixelsButton", "ShowInFolderButton", "RotateLeftButton", "RotateRightButton", "DeleteButton" })
        {
            var button = (Button)window.FindName(name);
            button.ApplyTemplate();
            Assert.IsFalse(button.IsEnabled, name);
            Assert.IsTrue(button.Focusable, name);
            Assert.IsNotNull(button.FocusVisualStyle, name);
            Assert.IsNotNull(button.ToolTip, name);
            var content = (ContentPresenter)button.Template.FindName("ButtonContent", button);
            Assert.AreEqual(0.42, content.Opacity, 0.001, name);
        }
        Assert.IsTrue(((Button)window.FindName("ZoomPopupButton")).IsEnabled);
        Assert.IsFalse(((Slider)window.FindName("ZoomSlider")).IsEnabled);
        Assert.IsFalse(((TextBox)window.FindName("ZoomText")).IsEnabled);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Counter_Space_Is_Stable_And_Dock_Fits_The_Minimum_Window() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        var counter = (TextBlock)window.FindName("ImagePositionText");
        var root = (Grid)window.Content;
        root.Measure(new Size(520, 360));
        root.Arrange(new Rect(0, 0, 520, 360));
        var dock = (Border)window.FindName("NavigationDock");
        var emptyWidth = dock.ActualWidth;
        Assert.AreEqual(Visibility.Hidden, counter.Visibility);
        Assert.IsTrue(emptyWidth > 0 && emptyWidth <= 520);
        var navigator = (ImageNavigator)typeof(MainWindow).GetField("_navigator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        navigator.LoadSingle(@"C:\one.png");
        typeof(MainWindow).GetMethod("UpdateImagePositionText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        root.UpdateLayout();
        Assert.AreEqual(Visibility.Visible, counter.Visibility);
        Assert.AreEqual(emptyWidth, dock.ActualWidth, 0.01);
        var toolbar = (FrameworkElement)dock.Parent;
        var top = dock.TranslatePoint(new Point(), toolbar).Y;
        var bottom = toolbar.ActualHeight - top - dock.ActualHeight;
        Assert.AreEqual(top, bottom, 0.01);
        Assert.IsGreaterThanOrEqualTo(7, top);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Performance_Options_Are_Grouped_Without_Losing_Their_Handlers() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        var performance = (MenuItem)window.FindName("PerformanceMenuItem");
        foreach (var name in new[] { "PreloadEnabledMenuItem", "PreloadMemoryBudgetMenuItem", "PreloadAggressivenessMenuItem", "KeepReadyInBackgroundMenuItem" })
            Assert.IsTrue(performance.Items.Contains(window.FindName(name)), name);
        Assert.AreEqual(5, ((MenuItem)window.FindName("PreloadMemoryBudgetMenuItem")).Items.Count);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Model_Toolbar_Offers_Reset_And_Zoom_But_Not_Image_Rotation() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        using var host = new NativeGlHost();
        using var renderer = new F3dModelRenderer(host, "unused");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(F3dModelRenderer).GetField("_hasOpenScene", flags)!.SetValue(renderer, true);
        typeof(MainWindow).GetField("_modelRenderer", flags)!.SetValue(window, renderer);
        typeof(MainWindow).GetField("_contentMode", flags)!.SetValue(window, ViewerContentMode.Model);
        typeof(MainWindow).GetMethod("UpdateNavigationButtons", flags)!.Invoke(window, null);
        var reset = (Button)window.FindName("ActualPixelsButton");
        Assert.IsTrue(reset.IsEnabled);
        StringAssert.Contains(reset.ToolTip.ToString()!, LocalizedText.Get(LocalizedText.ResetView));
        Assert.IsTrue(((Slider)window.FindName("ZoomSlider")).IsEnabled);
        Assert.AreEqual("100%", ((TextBox)window.FindName("ZoomText")).Text);
        Assert.IsFalse(((Button)window.FindName("RotateLeftButton")).IsEnabled);
        Assert.IsFalse(((Button)window.FindName("RotateRightButton")).IsEnabled);
        return Task.CompletedTask;
    });

    private static MainWindow CreateWindow() => new(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = false });

    [TestMethod]
    [DataRow(520.0)]
    [DataRow(1280.0)]
    [DataRow(1920.0)]
    public Task Filename_Uses_Available_Title_Space_Without_Overlapping_Controls(double width) => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        var root = (Grid)window.Content;
        var title = (TextBlock)window.FindName("ChromeTitleText");
        foreach (var filename in new[]
        {
            "Photo.jpg",
            "5500-2016-RA-0030_0_010 Working Environment Main Report_Process Overview.png",
            new string('W', 240) + ".png"
        })
        {
            typeof(MainWindow).GetMethod("UpdateWindowTitle", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [filename]);
            root.Measure(new Size(width, 600));
            root.Arrange(new Rect(0, 0, width, 600));
            root.UpdateLayout();
            var naturalTitle = new TextBlock
            {
                Text = filename,
                FontFamily = title.FontFamily,
                FontSize = title.FontSize,
                FontWeight = title.FontWeight
            };
            naturalTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var available = width - title.Margin.Left - title.Margin.Right;
            // Ellipsis layout can leave a partial glyph's worth of unused space.
            var tolerance = naturalTitle.DesiredSize.Width > available ? 16.0 : 2.0;
            Assert.AreEqual(Math.Min(naturalTitle.DesiredSize.Width, available), title.ActualWidth, tolerance);
            var left = title.TranslatePoint(new Point(), root).X;
            Assert.IsGreaterThanOrEqualTo(title.Margin.Left - 1, left);
            Assert.IsLessThanOrEqualTo(width - title.Margin.Right + 1, left + title.ActualWidth);
            Assert.AreEqual(width / 2, left + title.ActualWidth / 2, 1.0);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, title.TextTrimming);
            StringAssert.Contains(title.ToolTip.ToString()!, filename);
        }
        return Task.CompletedTask;
    });
}
