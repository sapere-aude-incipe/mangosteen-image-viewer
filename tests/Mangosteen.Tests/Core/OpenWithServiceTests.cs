using System.IO;
using System.Runtime.InteropServices;
using Mangosteen.Shell;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class OpenWithServiceTests
{
    [TestMethod]
    [DataRow(@"C:\Photos\a picture.jpeg")]
    [DataRow("C:\\Photos\\\u00e6\u00f8\u00e5\\\u753b\u50cf.png")]
    [DataRow("relative-image.webp")]
    public void Chooser_Receives_The_Whole_Path_Owner_And_Execute_Flag(string path)
    {
        var owner = new IntPtr(1234);
        var called = false;
        OpenWithService.Show(path, owner, (IntPtr actualOwner, ref OpenWithService.OpenAsInfo info) =>
        {
            called = true;
            Assert.AreEqual(owner, actualOwner);
            Assert.AreEqual(Path.GetFullPath(path), info.File);
            Assert.IsNull(info.Class);
            Assert.AreEqual(0x00000004u, info.Flags);
            return 0;
        });
        Assert.IsTrue(called);
    }

    [TestMethod]
    [DataRow(unchecked((int)0x800704C7))]
    [DataRow(unchecked((int)0x80004004))]
    [DataRow(0)]
    [DataRow(1)]
    public void Cancellation_And_Success_Are_Not_Errors(int result)
    {
        OpenWithService.Show(@"C:\photo.jpg", IntPtr.Zero,
            (IntPtr owner, ref OpenWithService.OpenAsInfo info) => result);
    }

    [TestMethod]
    public void Native_Failures_Are_Reported_Instead_Of_Silently_Ignored()
    {
        Assert.ThrowsExactly<COMException>(() => OpenWithService.Show(@"C:\photo.jpg", IntPtr.Zero,
            (IntPtr owner, ref OpenWithService.OpenAsInfo info) => unchecked((int)0x80004005)));
    }

    [TestMethod]
    public void Interop_Layout_Matches_Windows_OpenAsInfo()
    {
        Assert.AreEqual(IntPtr.Size * 3, Marshal.SizeOf<OpenWithService.OpenAsInfo>());
        Assert.AreEqual(new IntPtr(IntPtr.Size), Marshal.OffsetOf<OpenWithService.OpenAsInfo>(nameof(OpenWithService.OpenAsInfo.Class)));
        Assert.AreEqual(new IntPtr(IntPtr.Size * 2), Marshal.OffsetOf<OpenWithService.OpenAsInfo>(nameof(OpenWithService.OpenAsInfo.Flags)));
    }
}
