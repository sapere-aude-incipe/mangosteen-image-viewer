using ClassicPhotoViewer.Decoding;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class ImageDialogFilterTests
{
    [TestMethod]
    public void Build_Uses_All_Registered_Image_Extensions()
    {
        var filter = ImageDialogFilter.Build(ImageFileExtensions.BroadImageExtensions);

        StringAssert.StartsWith(filter, "Image files|");
        StringAssert.Contains(filter, "*.3fr");
        StringAssert.Contains(filter, "*.dng");
        StringAssert.Contains(filter, "*.heic");
        StringAssert.Contains(filter, "*.jxl");
        StringAssert.EndsWith(filter, "|All files|*.*");
    }

    [TestMethod]
    public void Build_Covers_Default_Decoder_Registry_Extensions()
    {
        using var registry = DecoderRegistry.CreateDefault();

        var filter = ImageDialogFilter.Build(registry.SupportedExtensions);

        StringAssert.Contains(filter, "*.jxl");
        StringAssert.Contains(filter, "*.jxr");
        StringAssert.Contains(filter, "*.wdp");
    }

    [TestMethod]
    public void Build_Normalizes_And_Deduplicates_Extensions()
    {
        var filter = ImageDialogFilter.Build([" jpg ", ".JPG", " .PNG "]);

        Assert.AreEqual("Image files|*.jpg;*.png|All files|*.*", filter);
    }

    [TestMethod]
    public void Build_Uses_Custom_Filter_Labels()
    {
        var filter = ImageDialogFilter.Build([".jpg"], "Bildefiler", "Alle filer");

        Assert.AreEqual("Bildefiler|*.jpg|Alle filer|*.*", filter);
    }

    [TestMethod]
    public void Build_Sanitizes_Filter_Label_Separators()
    {
        var filter = ImageDialogFilter.Build([".jpg"], "Image|files", "All|files");

        Assert.AreEqual("Image files|*.jpg|All files|*.*", filter);
    }

    [TestMethod]
    public void Build_Rejects_Null_Extension_Source()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ImageDialogFilter.Build(null!));
    }

    [TestMethod]
    public void Build_Uses_AllFiles_Filter_When_No_Image_Extensions_Are_Available()
    {
        var filter = ImageDialogFilter.Build(["", "   "]);

        Assert.AreEqual("All files|*.*", filter);
    }
}
