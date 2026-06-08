using Mangosteen.Decoding;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ImageFileExtensionsTests
{
    [TestMethod]
    public void BuildFolderScanExtensions_Does_Not_Add_Explicit_Custom_Extension()
    {
        var extensions = ImageFileExtensions.BuildFolderScanExtensions([".jpg"], "sample.custom");

        CollectionAssert.Contains(extensions.ToList(), ".jpg");
        CollectionAssert.DoesNotContain(extensions.ToList(), ".custom");
    }

    [TestMethod]
    public void BuildFolderScanExtensions_Does_Not_Add_Empty_Extension_For_Extensionless_File()
    {
        var extensions = ImageFileExtensions.BuildFolderScanExtensions([".jpg"], "sample");

        CollectionAssert.Contains(extensions.ToList(), ".jpg");
        CollectionAssert.DoesNotContain(extensions.ToList(), string.Empty);
    }

    [TestMethod]
    public void BuildFolderScanExtensions_Normalizes_Dotless_Supported_Extensions()
    {
        var extensions = ImageFileExtensions.BuildFolderScanExtensions(["jpg", ".PNG"], "current.custom");
        var list = extensions.ToList();

        CollectionAssert.Contains(list, ".jpg");
        CollectionAssert.Contains(list, ".png");
        CollectionAssert.DoesNotContain(list, ".custom");
        CollectionAssert.DoesNotContain(list, "jpg");
    }
}
