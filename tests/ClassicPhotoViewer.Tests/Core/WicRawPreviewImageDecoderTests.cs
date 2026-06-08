using ClassicPhotoViewer.Core;
using ClassicPhotoViewer.Decoding;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class WicRawPreviewImageDecoderTests
{
    [TestMethod]
    public void CanDecode_Returns_True_For_Raw_Like_Extension()
    {
        var path = CreateTempFilePath(".raf");
        try
        {
            File.WriteAllBytes(path, []);

            var decoder = new WicRawPreviewImageDecoder();

            Assert.IsTrue(decoder.CanDecode(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void IsEmbeddedRawPreviewUsable_Allows_Large_Embedded_Preview()
    {
        var usable = WicRawPreviewImageDecoder.IsEmbeddedRawPreviewUsable(
            new PixelSize(6000, 4000),
            new PixelSize(1800, 1200));

        Assert.IsTrue(usable);
    }

    [TestMethod]
    public void IsEmbeddedRawPreviewUsable_Rejects_Tiny_Embedded_Preview()
    {
        var usable = WicRawPreviewImageDecoder.IsEmbeddedRawPreviewUsable(
            new PixelSize(160, 120),
            new PixelSize(1800, 1200));

        Assert.IsFalse(usable);
    }

    [TestMethod]
    public void IsEmbeddedRawPreviewUsable_Rejects_Mismatched_Aspect_Ratio()
    {
        var usable = WicRawPreviewImageDecoder.IsEmbeddedRawPreviewUsable(
            new PixelSize(1200, 1200),
            new PixelSize(1800, 1200));

        Assert.IsFalse(usable);
    }

    private static string CreateTempFilePath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
    }
}
