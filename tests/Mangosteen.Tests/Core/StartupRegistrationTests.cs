using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class StartupRegistrationTests
{
    [TestMethod]
    public void BuildCommandLine_QuotesExecutableAndUsesBackgroundSwitch()
    {
        var command = StartupRegistration.BuildCommandLine(@"C:\Program Files\Mangosteen\Mangosteen.exe");

        Assert.AreEqual(
            "\"C:\\Program Files\\Mangosteen\\Mangosteen.exe\" --background",
            command);
    }
}
