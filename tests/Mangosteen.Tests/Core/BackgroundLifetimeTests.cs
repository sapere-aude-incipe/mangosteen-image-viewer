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

    [TestMethod]
    public void DeletedImage_IsRemovedFromNavigation_OnlyIfStillCurrent()
    {
        const string deleted = @"C:\Pictures\a.png";

        Assert.IsTrue(MainWindow.IsDeletedImageStillCurrent(deleted, 3, @"c:\pictures\A.PNG", 3, isClosing: false));
        // An activation opened another image while the shell's confirmation dialog was up.
        Assert.IsFalse(MainWindow.IsDeletedImageStillCurrent(deleted, 3, @"C:\Other\b.png", 4, isClosing: false));
        // Reopening the same path is still a new open the delete must not tear down.
        Assert.IsFalse(MainWindow.IsDeletedImageStillCurrent(deleted, 3, deleted, 4, isClosing: false));
        Assert.IsFalse(MainWindow.IsDeletedImageStillCurrent(deleted, 3, @"C:\Pictures\b.png", 3, isClosing: false));
        Assert.IsFalse(MainWindow.IsDeletedImageStillCurrent(deleted, 3, deleted, 3, isClosing: true));
    }
}
