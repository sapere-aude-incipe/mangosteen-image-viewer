using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class StartupRegistrationTests
{
    [TestMethod]
    public void Starting_Another_Installation_Does_Not_Steal_Startup_Registration()
    {
        var installed = StartupRegistration.BuildCommandLine(@"C:\Installed\Mangosteen.exe");
        var portable = StartupRegistration.BuildCommandLine(@"C:\Portable\Mangosteen.exe");
        Assert.IsFalse(StartupRegistration.MayReplaceEntry(installed, portable, false));
        Assert.IsTrue(StartupRegistration.MayReplaceEntry(installed, portable, true));
        Assert.IsTrue(StartupRegistration.MayReplaceEntry(null, portable, false));
        Assert.IsTrue(StartupRegistration.MayReplaceEntry(installed, installed, false));
    }

    [TestMethod]
    public void BuildCommandLine_QuotesExecutableAndUsesBackgroundSwitch()
    {
        var command = StartupRegistration.BuildCommandLine(@"C:\Program Files\Mangosteen\Mangosteen.exe");

        Assert.AreEqual(
            "\"C:\\Program Files\\Mangosteen\\Mangosteen.exe\" --background",
            command);
    }
}
