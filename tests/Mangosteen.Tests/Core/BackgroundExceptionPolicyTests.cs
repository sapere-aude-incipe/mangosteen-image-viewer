namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class BackgroundExceptionPolicyTests
{
    [TestMethod]
    public void IsExpectedShutdownOrCancellation_Suppresses_OperationCanceled()
    {
        var suppress = BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(
            new OperationCanceledException(),
            isClosing: false);

        Assert.IsTrue(suppress);
    }

    [TestMethod]
    public void IsExpectedShutdownOrCancellation_Suppresses_ObjectDisposed_During_Close()
    {
        var suppress = BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(
            new ObjectDisposedException("decoder"),
            isClosing: true);

        Assert.IsTrue(suppress);
    }

    [TestMethod]
    public void IsExpectedShutdownOrCancellation_Suppresses_ObjectDisposed_For_Canceled_Token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var suppress = BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(
            new ObjectDisposedException("decoder"),
            isClosing: false,
            cts.Token);

        Assert.IsTrue(suppress);
    }

    [TestMethod]
    public void IsExpectedShutdownOrCancellation_Does_Not_Suppress_Unexpected_ObjectDisposed()
    {
        var suppress = BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(
            new ObjectDisposedException("decoder"),
            isClosing: false);

        Assert.IsFalse(suppress);
    }

    [TestMethod]
    public void IsExpectedShutdownOrCancellation_Does_Not_Suppress_Unexpected_Exception()
    {
        var suppress = BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(
            new InvalidOperationException("boom"),
            isClosing: true);

        Assert.IsFalse(suppress);
    }
}
