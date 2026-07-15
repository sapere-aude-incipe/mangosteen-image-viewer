using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class AppInstanceCoordinatorTests
{
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
