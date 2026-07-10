using Mangosteen;

namespace Mangosteen.Tests.Core;

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

    [TestMethod]
    public void Concurrent_Releases_Dispose_When_The_Last_Reference_Completes()
    {
        const int additionalReferences = 64;
        var session = new LoadSession(1);
        for (var index = 0; index < additionalReferences; index++)
        {
            Assert.IsTrue(session.TryAddReference());
        }

        Parallel.For(0, additionalReferences + 1, _ => session.Release());

        Assert.IsTrue(session.IsDisposed);
        Assert.IsFalse(session.TryAddReference());
    }

    [TestMethod]
    public async Task Cancellation_And_Reference_Release_Can_Race_Safely()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var session = new LoadSession(iteration);
            var token = session.Token;
            Assert.IsTrue(session.TryAddReference());
            using var start = new ManualResetEventSlim(initialState: false);

            var cancelTask = Task.Run(() =>
            {
                start.Wait();
                session.CancelAndDisposeWhenInactive();
            });
            var releaseTask = Task.Run(() =>
            {
                start.Wait();
                session.Release();
            });

            start.Set();
            await Task.WhenAll(cancelTask, releaseTask);
            session.Release();

            Assert.IsTrue(token.IsCancellationRequested);
            Assert.IsTrue(session.IsDisposed);
            Assert.IsFalse(session.TryAddReference());
        }
    }
}
