using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Mangosteen.Navigation;
using Mangosteen.Localization;
using Mangosteen.Rendering.Advanced;

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
}
