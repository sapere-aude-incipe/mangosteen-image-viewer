using Mangosteen;
using System.Reflection;
using System.Text.Json;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ProgramEntryTests
{
    [TestMethod]
    public void EntryPoint_IsProgramMain_OnStaThread()
    {
        var entryPoint = typeof(App).Assembly.EntryPoint;

        Assert.IsNotNull(entryPoint);
        Assert.AreEqual(typeof(Program), entryPoint.DeclaringType);
        Assert.IsNotNull(entryPoint.GetCustomAttribute<STAThreadAttribute>());
        // A generated App.Main would mean App.xaml is an ApplicationDefinition again,
        // and every forwarded launch would pay for WPF start-up.
        Assert.IsNull(typeof(App).GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
    }

    [TestMethod]
    public void Forwarded_Launch_ExitsZero_WithoutReporting()
    {
        var reported = 0;
        var exitCode = SecondaryLaunch.Complete(
            StartupLaunchOptions.Parse([@"C:\Pictures\sample.png"]),
            forwarded: true,
            () => reported++);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(0, reported);
    }

    [TestMethod]
    public void Failed_InteractiveLaunch_ReportsAndExitsOne()
    {
        var reported = 0;
        var exitCode = SecondaryLaunch.Complete(
            StartupLaunchOptions.Parse([@"C:\Pictures\sample.png"]),
            forwarded: false,
            () => reported++);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(1, reported);
    }

    [TestMethod]
    [DataRow("--background")]
    [DataRow("--shutdown")]
    [DataRow("--background", @"C:\Pictures\sample.png")]
    public void Failed_BackgroundOrShutdownLaunch_StaysSilentAndExitsOne(params string[] arguments)
    {
        var reported = 0;
        var exitCode = SecondaryLaunch.Complete(
            StartupLaunchOptions.Parse(arguments),
            forwarded: false,
            () => reported++);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(0, reported);
    }

    [TestMethod]
    [DataRow(null, false, true)]
    [DataRow(@"C:\Pictures\sample.png", true, false)]
    [DataRow(@"C:\Bilder\Ærfugl \""quoted\"" 写真.jpg", true, false)]
    [DataRow(@"\server\share\a<b>&'c'.png", false, false)]
    public void SerializedRequest_MatchesJsonSerializerOutput(string? path, bool activate, bool shutdown)
    {
        var request = new AppActivationRequest(path, activate, shutdown);

        var expected = JsonSerializer.SerializeToUtf8Bytes(request, AppInstanceJsonContext.Default.AppActivationRequest);
        var actual = AppInstanceCoordinator.SerializeRequest(request);

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(request, JsonSerializer.Deserialize(actual, AppInstanceJsonContext.Default.AppActivationRequest));
    }
}
