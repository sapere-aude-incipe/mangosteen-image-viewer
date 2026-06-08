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
}
