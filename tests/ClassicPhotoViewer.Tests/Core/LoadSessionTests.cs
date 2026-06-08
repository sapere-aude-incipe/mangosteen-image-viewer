using ClassicPhotoViewer;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class LoadSessionTests
{
    [TestMethod]
    public void Release_Disposes_Session_When_Last_Reference_Completes()
    {
        var session = new LoadSession(1);

        session.Release();

        Assert.IsTrue(session.IsDisposed);
        Assert.IsFalse(session.TryAddReference());
    }

    [TestMethod]
    public void CancelAndDisposeWhenInactive_Cancels_Immediately_And_Disposes_After_References_Release()
    {
        var session = new LoadSession(1);
        var token = session.Token;
        Assert.IsTrue(session.TryAddReference());

        session.CancelAndDisposeWhenInactive();

        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(session.IsDisposed);
        Assert.IsFalse(session.TryAddReference());

        session.Release();
        Assert.IsFalse(session.IsDisposed);

        session.Release();
        Assert.IsTrue(session.IsDisposed);
    }
}
