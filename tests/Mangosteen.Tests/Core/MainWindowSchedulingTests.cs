using Mangosteen;

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
            fitsAtActualPixels: true,
            zoom: 14.56));
    }

    [TestMethod]
    public void ActualPixels_Command_Is_Disabled_For_Small_Image_Already_At_One_To_One()
    {
        Assert.IsFalse(MainWindow.CanToggleActualPixelsForState(
            hasImage: true,
            fitsAtActualPixels: true,
            zoom: 1.0));
    }
}
