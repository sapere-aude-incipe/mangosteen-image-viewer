using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Mangosteen.Localization;
using Mangosteen.Updates;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // WPF WindowChrome uses process-wide property-descriptor caches.
public sealed class UpdateOverlayTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task Image_Status_Changes_Do_Not_Hide_Or_Reset_Download_Progress(bool knownTotal) => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        var progress = new UpdateDownloadProgress(2 * 1024 * 1024, knownTotal ? 8 * 1024 * 1024 : null);
        BeginDownload(window, progress);

        for (var i = 0; i < 3; i++)
        {
            Invoke(window, "ShowStatus", LocalizedText.Get(LocalizedText.Loading));
            AssertDownloadVisible(window, progress);
            Invoke(window, "HideStatus");
            // Assert before another progress callback could mask a navigation regression.
            AssertDownloadVisible(window, progress);
        }

        progress = progress with { BytesDownloaded = 4 * 1024 * 1024 };
        Invoke(window, "ShowUpdateDownloadProgress", progress);
        AssertDownloadVisible(window, progress);
        Assert.IsTrue(Element<Button>(window, "CancelUpdateButton").IsEnabled);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Navigation_During_Cancellation_Does_Not_Reenable_Cancel_Or_Hide_Progress() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        using var cancellation = new CancellationTokenSource();
        typeof(MainWindow).GetField("_updateCheckCts", PrivateInstance)!.SetValue(window, cancellation);
        var progress = new UpdateDownloadProgress(256, 1024);
        BeginDownload(window, progress);

        Invoke(window, "CancelUpdateButton_Click", Element<Button>(window, "CancelUpdateButton"), new RoutedEventArgs());
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Invoke(window, "ShowStatus", LocalizedText.Get(LocalizedText.Loading));
        Invoke(window, "HideStatus");
        Invoke(window, "ShowUpdateDownloadProgress", progress);
        AssertDownloadVisible(window, progress);
        Assert.IsFalse(Element<Button>(window, "CancelUpdateButton").IsEnabled);

        Invoke(window, "RestoreStatusAfterUpdateCheck");
        Assert.AreEqual(Visibility.Collapsed, Element<Border>(window, "StatusOverlay").Visibility);
        AssertProgressCleared(window);
        return Task.CompletedTask;
    });

    [TestMethod]
    [DataRow("ready")]
    [DataRow("loading")]
    [DataRow("error")]
    [DataRow("empty")]
    public Task Ending_Update_Restores_The_Latest_Image_Status(string state) => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        Invoke(window, "ShowStatus", "Previous image error");
        BeginDownload(window, default);
        var latestText = state switch
        {
            "loading" => LocalizedText.Get(LocalizedText.Loading),
            "error" => "Current image could not be opened",
            "empty" => LocalizedText.Get(LocalizedText.NoImage),
            _ => null
        };
        if (latestText is null)
        {
            Invoke(window, "HideStatus");
        }
        else
        {
            Invoke(window, "ShowStatus", latestText);
        }

        AssertDownloadVisible(window, default);
        Assert.IsFalse((bool)Invoke(window, "CanOpenFromStatusOverlay")!);
        Invoke(window, "RestoreStatusAfterUpdateCheck");
        AssertProgressCleared(window);
        Assert.AreEqual(latestText is null ? Visibility.Collapsed : Visibility.Visible,
            Element<Border>(window, "StatusOverlay").Visibility);
        if (latestText is not null)
        {
            Assert.AreEqual(latestText, Element<TextBlock>(window, "StatusMessageText").Text);
            Assert.AreEqual(state == "empty" ? Visibility.Visible : Visibility.Collapsed,
                Element<Grid>(window, "EmptyStatePanel").Visibility);
            Assert.AreEqual(state == "empty", (bool)Invoke(window, "CanOpenFromStatusOverlay")!);
        }

        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Update_Phases_Retain_Priority_But_Only_Downloading_Shows_Progress() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        var checking = LocalizedText.Get(LocalizedText.CheckingForUpdates);
        Invoke(window, "ShowUpdateStatus", checking);
        Invoke(window, "HideStatus");
        Assert.AreEqual(Visibility.Visible, Element<Border>(window, "StatusOverlay").Visibility);
        Assert.AreEqual(checking, Element<TextBlock>(window, "StatusMessageText").Text);
        AssertProgressCleared(window);

        BeginDownload(window, new UpdateDownloadProgress(1024, 1024));
        var starting = LocalizedText.Get(LocalizedText.StartingInstaller);
        Invoke(window, "ShowUpdateStatus", starting);
        Invoke(window, "ShowStatus", LocalizedText.Get(LocalizedText.Loading));
        Assert.AreEqual(starting, Element<TextBlock>(window, "StatusMessageText").Text);
        Assert.AreEqual(Visibility.Visible, Element<Border>(window, "StatusOverlay").Visibility);
        AssertProgressCleared(window);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Late_Progress_Cannot_Resurrect_A_Finished_Update() => StaTest.RunAsync(() =>
    {
        var window = CreateWindow();
        BeginDownload(window, default);
        Invoke(window, "RestoreStatusAfterUpdateCheck");
        Invoke(window, "ShowUpdateDownloadProgress", new UpdateDownloadProgress(1024, 2048));
        AssertProgressCleared(window);
        Assert.AreEqual(Visibility.Visible, Element<Grid>(window, "EmptyStatePanel").Visibility);
        Assert.IsTrue((bool)Invoke(window, "CanOpenFromStatusOverlay")!);
        return Task.CompletedTask;
    });

    private static void BeginDownload(MainWindow window, UpdateDownloadProgress progress)
    {
        Invoke(window, "ShowUpdateStatus", LocalizedText.Get(LocalizedText.DownloadingUpdate));
        Invoke(window, "ShowUpdateDownloadProgress", progress);
    }

    private static void AssertDownloadVisible(MainWindow window, UpdateDownloadProgress progress)
    {
        Assert.AreEqual(Visibility.Visible, Element<Border>(window, "StatusOverlay").Visibility);
        Assert.AreEqual(Visibility.Visible, Element<Border>(window, "StatusMessagePanel").Visibility);
        Assert.AreEqual(Visibility.Visible, Element<StackPanel>(window, "UpdateProgressPanel").Visibility);
        Assert.AreEqual(Visibility.Collapsed, Element<Grid>(window, "EmptyStatePanel").Visibility);
        Assert.AreEqual(LocalizedText.Get(LocalizedText.DownloadingUpdate), Element<TextBlock>(window, "StatusMessageText").Text);
        Assert.AreEqual(MainWindow.FormatUpdateProgressDetails(progress, CultureInfo.CurrentCulture),
            Element<TextBlock>(window, "UpdateProgressDetailsText").Text);
        var bar = Element<ProgressBar>(window, "UpdateProgressBar");
        Assert.AreEqual(progress.TotalBytes is not > 0, bar.IsIndeterminate);
        Assert.AreEqual(progress.TotalBytes is > 0 ? (double)progress.TotalBytes.Value : 1.0, bar.Maximum);
        Assert.AreEqual(progress.TotalBytes is > 0 ? (double)progress.BytesDownloaded : 0.0, bar.Value);
    }

    private static void AssertProgressCleared(MainWindow window)
    {
        Assert.AreEqual(Visibility.Collapsed, Element<StackPanel>(window, "UpdateProgressPanel").Visibility);
        Assert.IsFalse(Element<ProgressBar>(window, "UpdateProgressBar").IsIndeterminate);
        Assert.AreEqual(0.0, Element<ProgressBar>(window, "UpdateProgressBar").Value);
        Assert.AreEqual(string.Empty, Element<TextBlock>(window, "UpdateProgressDetailsText").Text);
    }

    private static T Element<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);

    private static object? Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(window, args);

    private static MainWindow CreateWindow() => new(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = false });
}
