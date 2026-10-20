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
        static bool Exists(string _) => true;
        Assert.IsFalse(StartupRegistration.MayReplaceEntry(installed, portable, false, Exists));
        Assert.IsTrue(StartupRegistration.MayReplaceEntry(installed, portable, true, Exists));
        Assert.IsTrue(StartupRegistration.MayReplaceEntry(null, portable, false, Exists));
        Assert.IsTrue(StartupRegistration.MayReplaceEntry(installed, installed, false, Exists));
    }

    [TestMethod]
    public void Entry_For_A_Moved_Or_Deleted_Executable_Is_Repointed()
    {
        var oldFolder = StartupRegistration.BuildCommandLine(@"C:\Old\Mangosteen\Mangosteen.exe");
        var newFolder = StartupRegistration.BuildCommandLine(@"C:\New\Mangosteen\Mangosteen.exe");
        var checkedPaths = new List<string>();

        Assert.IsTrue(StartupRegistration.MayReplaceEntry(oldFolder, newFolder, false, path =>
        {
            checkedPaths.Add(path);
            return false;
        }));
        CollectionAssert.AreEqual(new[] { @"C:\Old\Mangosteen\Mangosteen.exe" }, checkedPaths);
        // An entry that cannot be understood is left to the user to change explicitly.
        Assert.IsFalse(StartupRegistration.MayReplaceEntry("not a path", newFolder, false, _ => false));
    }

    [TestMethod]
    [DataRow("\"C:\\Program Files\\Mangosteen\\Mangosteen.exe\" --background", @"C:\Program Files\Mangosteen\Mangosteen.exe")]
    [DataRow(@"C:\Program Files\Mangosteen\Mangosteen.exe --background", @"C:\Program Files\Mangosteen\Mangosteen.exe")]
    [DataRow(@"C:\Tools\viewer.cmd", @"C:\Tools\viewer.cmd")]
    [DataRow("relative.exe --background", null)]
    [DataRow("\"unterminated", null)]
    public void GetRegisteredExecutablePath_ParsesRunEntries(string commandLine, string? expected)
    {
        Assert.AreEqual(expected, StartupRegistration.GetRegisteredExecutablePath(commandLine));
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
