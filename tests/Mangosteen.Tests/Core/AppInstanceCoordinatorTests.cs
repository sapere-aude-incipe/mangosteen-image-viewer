using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class AppInstanceCoordinatorTests
{
    [TestMethod]
    public void InstanceIdentity_Separates_Installations_And_Users_But_Normalizes_Paths()
    {
        var installed = AppInstanceCoordinator.GetInstanceName(@"C:\Apps\Mangosteen", "user-a");
        Assert.AreEqual(installed, AppInstanceCoordinator.GetInstanceName(@"c:\apps\Mangosteen\.\", "user-a"));
        Assert.AreNotEqual(installed, AppInstanceCoordinator.GetInstanceName(@"C:\Portable\Mangosteen", "user-a"));
        Assert.AreNotEqual(installed, AppInstanceCoordinator.GetInstanceName(@"C:\Apps\Mangosteen", "user-b"));
    }

    [TestMethod]
    public async Task SecondaryInstance_ForwardsActivationRequest_ToPrimaryInstance()
    {
        var instanceName = $"Mangosteen.Tests.{Guid.NewGuid():N}";
        using var primary = new AppInstanceCoordinator(instanceName);
        using var secondary = new AppInstanceCoordinator(instanceName);
        var received = new TaskCompletionSource<AppActivationRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.IsTrue(primary.IsPrimaryInstance);
        Assert.IsFalse(secondary.IsPrimaryInstance);
        primary.StartServer(request =>
        {
            received.TrySetResult(request);
            return Task.FromResult(true);
        });

        var expected = new AppActivationRequest(@"C:\Pictures\sample.png", Activate: true, RequestShutdown: false);
        var sent = await secondary.TrySendAsync(expected);
        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ActivationSendResult.Delivered, sent);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task RefusedActivation_IsReportedAsRejected_NotDelivered()
    {
        var instanceName = $"Mangosteen.Tests.{Guid.NewGuid():N}";
        using var primary = new AppInstanceCoordinator(instanceName);
        using var secondary = new AppInstanceCoordinator(instanceName);
        var handled = 0;
        primary.StartServer(_ =>
        {
            Interlocked.Increment(ref handled);
            return Task.FromResult(false);
        });

        var sent = await secondary.TrySendAsync(
            new AppActivationRequest(@"C:\Pictures\sample.png", Activate: true, RequestShutdown: false));

        Assert.AreEqual(ActivationSendResult.Rejected, sent);
        Assert.AreEqual(1, Volatile.Read(ref handled));
    }

    [TestMethod]
    public void ReleasePrimary_LetsTheNextLaunchBecomePrimary_WhileOtherHandlesRemainOpen()
    {
        var instanceName = $"Mangosteen.Tests.{Guid.NewGuid():N}";
        using var primary = new AppInstanceCoordinator(instanceName);
        // A secondary still talking to the exiting primary keeps the named mutex
        // alive, so the next launch cannot rely on creating it afresh.
        using var secondary = new AppInstanceCoordinator(instanceName);
        Assert.IsTrue(primary.IsPrimaryInstance);
        Assert.IsFalse(secondary.IsPrimaryInstance);

        // Mutex ownership is per thread, so later launches are modelled on other threads.
        Assert.IsFalse(IsPrimaryOnAnotherThread(instanceName));
        primary.ReleasePrimary();
        Assert.IsTrue(IsPrimaryOnAnotherThread(instanceName));
    }

    private static bool IsPrimaryOnAnotherThread(string instanceName)
    {
        var isPrimary = false;
        var thread = new Thread(() =>
        {
            using var coordinator = new AppInstanceCoordinator(instanceName);
            isPrimary = coordinator.IsPrimaryInstance;
        });
        thread.Start();
        thread.Join();
        return isPrimary;
    }
}
