using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class BackgroundLifetimeTests
{
    [TestMethod]
    public void Close_Hides_WhenBackgroundReadinessIsEnabled()
    {
        Assert.IsTrue(MainWindow.ShouldHideOnClose(
            keepReadyInBackground: true,
            exitRequested: false,
            dispatcherIsShuttingDown: false));
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    public void Close_Exits_WhenBackgroundReadinessDoesNotApply(
        bool keepReadyInBackground,
        bool exitRequested,
        bool dispatcherIsShuttingDown)
    {
        Assert.IsFalse(MainWindow.ShouldHideOnClose(
            keepReadyInBackground,
            exitRequested,
            dispatcherIsShuttingDown));
    }
}
