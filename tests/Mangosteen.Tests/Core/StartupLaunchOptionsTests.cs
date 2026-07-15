using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class StartupLaunchOptionsTests
{
    [TestMethod]
    public void Parse_DefaultLaunch_ActivatesExistingInstance()
    {
        var options = StartupLaunchOptions.Parse([]);

        Assert.IsFalse(options.IsBackgroundLaunch);
        Assert.IsFalse(options.RequestShutdown);
        Assert.IsNull(options.FilePath);
        Assert.IsTrue(options.ShouldActivate);
    }

    [TestMethod]
    public void Parse_BackgroundLaunch_StaysHidden()
    {
        var options = StartupLaunchOptions.Parse(["--background"]);

        Assert.IsTrue(options.IsBackgroundLaunch);
        Assert.IsFalse(options.ShouldActivate);
    }

    [TestMethod]
    public void Parse_BackgroundLaunchWithImage_ActivatesAndForwardsImage()
    {
        var options = StartupLaunchOptions.Parse(["--background", @"C:\Pictures\sample.png"]);

        Assert.IsTrue(options.IsBackgroundLaunch);
        Assert.AreEqual(@"C:\Pictures\sample.png", options.FilePath);
        Assert.IsTrue(options.ShouldActivate);
    }

    [TestMethod]
    public void Parse_ShutdownRequest_DoesNotActivate()
    {
        var options = StartupLaunchOptions.Parse(["--shutdown"]);
        var request = options.ToActivationRequest();

        Assert.IsTrue(options.RequestShutdown);
        Assert.IsFalse(options.ShouldActivate);
        Assert.IsTrue(request.RequestShutdown);
        Assert.IsFalse(request.Activate);
    }
}
