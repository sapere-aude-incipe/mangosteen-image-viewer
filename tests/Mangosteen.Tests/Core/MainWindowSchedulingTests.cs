using Mangosteen;
using Mangosteen.Rendering;
using Mangosteen.Updates;
using System.Globalization;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class MainWindowSchedulingTests
{
    [TestMethod]
    public void ShouldSchedulePreloadsAfterFullDecode_Returns_True_For_Current_Load()
    {
        var shouldSchedule = MainWindow.ShouldSchedulePreloadsAfterFullDecode(
            CancellationToken.None,
            () => true);

        Assert.IsTrue(shouldSchedule);
    }

    [TestMethod]
    public void ShouldSchedulePreloadsAfterFullDecode_Returns_False_For_Canceled_Load()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var shouldSchedule = MainWindow.ShouldSchedulePreloadsAfterFullDecode(
            cts.Token,
            () => true);

        Assert.IsFalse(shouldSchedule);
    }

    [TestMethod]
    public void ShouldSchedulePreloadsAfterFullDecode_Returns_False_For_Stale_Load()
    {
        var shouldSchedule = MainWindow.ShouldSchedulePreloadsAfterFullDecode(
            CancellationToken.None,
            () => false);

        Assert.IsFalse(shouldSchedule);
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Enabled_For_Small_Image_Zoomed_Past_One_To_One()
    {
        Assert.IsTrue(MainWindow.CanToggleActualPixelsForState(
            hasImage: true,
            isFullResolution: true,
            fitsAtActualPixels: true,
            zoom: 14.56,
            mode: ViewerFitMode.Custom));
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Disabled_For_Small_Image_Already_At_One_To_One()
    {
        Assert.IsFalse(MainWindow.CanToggleActualPixelsForState(
            hasImage: true,
            isFullResolution: true,
            fitsAtActualPixels: true,
            zoom: 1.0,
            mode: ViewerFitMode.Fit));
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Enabled_For_Large_Image_Fit_Below_One_To_One()
    {
        Assert.IsTrue(MainWindow.CanToggleActualPixelsForState(
            hasImage: true,
            isFullResolution: true,
            fitsAtActualPixels: false,
            zoom: 0.72,
            mode: ViewerFitMode.Fit));
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Enabled_For_Large_Image_Already_At_One_To_One()
    {
        Assert.IsTrue(MainWindow.CanToggleActualPixelsForState(
            hasImage: true,
            isFullResolution: true,
            fitsAtActualPixels: false,
            zoom: 1.0,
            mode: ViewerFitMode.ActualPixels));
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Enabled_For_Preview_Only_Image_At_One_To_One()
    {
        Assert.IsTrue(MainWindow.CanToggleActualPixelsForState(
            hasImage: true,
            isFullResolution: false,
            fitsAtActualPixels: true,
            zoom: 1.0,
            mode: ViewerFitMode.Fit));
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Disabled_When_No_Image_Is_Loaded()
    {
        Assert.IsFalse(MainWindow.CanToggleActualPixelsForState(
            hasImage: false,
            isFullResolution: false,
            fitsAtActualPixels: false,
            zoom: 1.0,
            mode: ViewerFitMode.Fit));
    }

    [TestMethod]
    public void Mouse_Thumb_Buttons_Map_To_Previous_And_Next_Image()
    {
        Assert.AreEqual(-1, MainWindow.GetNavigationDeltaForMouseButton(System.Windows.Input.MouseButton.XButton1));
        Assert.AreEqual(1, MainWindow.GetNavigationDeltaForMouseButton(System.Windows.Input.MouseButton.XButton2));
        Assert.AreEqual(0, MainWindow.GetNavigationDeltaForMouseButton(System.Windows.Input.MouseButton.Left));
    }

    [TestMethod]
    public void Initial_Window_Placement_Fills_Most_Of_Work_Area_With_Even_Margins()
    {
        var placement = MainWindow.CalculateInitialWindowPlacement(
            workAreaLeft: 0,
            workAreaTop: 0,
            workAreaWidth: 1920,
            workAreaHeight: 1040,
            minWidth: 520,
            minHeight: 360,
            fallbackWidth: 1080,
            fallbackHeight: 720);

        Assert.AreEqual(1651, placement.Width);
        Assert.AreEqual(894, placement.Height);
        Assert.AreEqual(135, placement.Left);
        Assert.AreEqual(73, placement.Top);
    }

    [TestMethod]
    public void Initial_Window_Placement_Uses_Work_Area_Origin_For_Secondary_Monitor()
    {
        var placement = MainWindow.CalculateInitialWindowPlacement(
            workAreaLeft: 1920,
            workAreaTop: 40,
            workAreaWidth: 2560,
            workAreaHeight: 1400,
            minWidth: 520,
            minHeight: 360,
            fallbackWidth: 1080,
            fallbackHeight: 720);

        Assert.AreEqual(2202, placement.Width);
        Assert.AreEqual(1204, placement.Height);
        Assert.AreEqual(2099, placement.Left);
        Assert.AreEqual(138, placement.Top);
    }

    [TestMethod]
    public void Initial_Window_Placement_Respects_Minimum_Size_On_Small_Work_Area()
    {
        var placement = MainWindow.CalculateInitialWindowPlacement(
            workAreaLeft: 0,
            workAreaTop: 0,
            workAreaWidth: 560,
            workAreaHeight: 400,
            minWidth: 520,
            minHeight: 360,
            fallbackWidth: 1080,
            fallbackHeight: 720);

        Assert.AreEqual(520, placement.Width);
        Assert.AreEqual(360, placement.Height);
        Assert.AreEqual(20, placement.Left);
        Assert.AreEqual(20, placement.Top);
    }

    [TestMethod]
    public void Update_Progress_Details_Include_Downloaded_And_Total_Megabytes()
    {
        var text = MainWindow.FormatUpdateProgressDetails(
            new UpdateDownloadProgress(5L * 1024 * 1024, 20L * 1024 * 1024),
            CultureInfo.InvariantCulture);

        Assert.AreEqual("5.0 MB / 20.0 MB", text);
    }

    [TestMethod]
    public void Update_Progress_Details_Work_When_Total_Size_Is_Unknown()
    {
        var text = MainWindow.FormatUpdateProgressDetails(
            new UpdateDownloadProgress(3L * 1024 * 1024, null),
            CultureInfo.InvariantCulture);

        Assert.AreEqual("3.0 MB", text);
    }
}
