using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class StartupDiagnosticsTests
{
    [TestMethod]
    public void ResolveLogPath_Returns_Null_For_Empty_Value()
    {
        Assert.IsNull(StartupDiagnostics.ResolveLogPath(null));
        Assert.IsNull(StartupDiagnostics.ResolveLogPath(" "));
    }

    [TestMethod]
    public void ResolveLogPath_Uses_Local_Diagnostics_Folder_For_Default_Request()
    {
        var path = StartupDiagnostics.ResolveLogPath("1");

        Assert.IsNotNull(path);
        StringAssert.Contains(path, "Mangosteen Image Viewer");
        StringAssert.Contains(path, "Diagnostics");
        Assert.IsTrue(Path.GetFileName(path).StartsWith("startup-", StringComparison.Ordinal));
        Assert.AreEqual(".log", Path.GetExtension(path));
    }

    [TestMethod]
    public void ResolveLogPath_Rejects_Fully_Qualified_Path()
    {
        var path = StartupDiagnostics.ResolveLogPath(Path.Combine(Path.GetTempPath(), "startup.log"));

        Assert.IsNull(path);
    }

    [TestMethod]
    public void ResolveLogPath_Constrains_File_Name_To_Local_Diagnostics_Folder()
    {
        var path = StartupDiagnostics.ResolveLogPath(@"..\startup-benchmark");

        Assert.IsNotNull(path);
        Assert.AreEqual("startup-benchmark.log", Path.GetFileName(path));
        StringAssert.Contains(path, "Mangosteen Image Viewer");
        StringAssert.Contains(path, "Diagnostics");
    }
}
