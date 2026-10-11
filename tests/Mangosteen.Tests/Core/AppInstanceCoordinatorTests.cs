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
            return Task.CompletedTask;
        });

        var expected = new AppActivationRequest(@"C:\Pictures\sample.png", Activate: true, RequestShutdown: false);
        var sent = await secondary.TrySendAsync(expected);
        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(sent);
        Assert.AreEqual(expected, actual);
    }
}
