using ClassicPhotoViewer.Core;
using ClassicPhotoViewer.Decoding;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class ImageDecodeSizingTests
{
    [TestMethod]
    public void GetTargetSize_Scales_To_Fit_While_Preserving_Aspect_Ratio()
    {
        var target = ImageDecodeSizing.GetTargetSize(
            width: 4000,
            height: 2000,
            previewSize: new PixelSize(1000, 1000),
            fullResolution: false);

        Assert.AreEqual(new PixelSize(1000, 500), target);
    }

    [TestMethod]
    public void GetTargetSize_Does_Not_Upscale_Previews()
    {
        var target = ImageDecodeSizing.GetTargetSize(
            width: 400,
            height: 200,
            previewSize: new PixelSize(1000, 1000),
            fullResolution: false);

        Assert.AreEqual(new PixelSize(400, 200), target);
    }

    [TestMethod]
    public void IsFullResolution_Treats_Animated_Previews_As_Not_FullResolution()
    {
        var metadata = new ImageMetadata("animated.gif", 400, 200, 2, "test");
        var request = new ImageDecodeRequest("animated.gif", new PixelSize(1000, 1000), FullResolution: false);
        var target = new PixelSize(400, 200);

        var isFull = ImageDecodeSizing.IsFullResolution(request, target, metadata, animated: true);

        Assert.IsFalse(isFull);
    }

    [TestMethod]
    public void IsFullResolution_Treats_NonAnimated_Unscaled_Previews_As_FullResolution()
    {
        var metadata = new ImageMetadata("photo.png", 400, 200, 1, "test");
        var request = new ImageDecodeRequest("photo.png", new PixelSize(1000, 1000), FullResolution: false);
        var target = new PixelSize(400, 200);

        var isFull = ImageDecodeSizing.IsFullResolution(request, target, metadata, animated: false);

        Assert.IsTrue(isFull);
    }

    [TestMethod]
    public void ImageDecodeRequest_Rejects_Invalid_Values()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ImageDecodeRequest(""));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ImageDecodeRequest("photo.png", MaxDecodedBytes: -1));
    }

    [TestMethod]
    public void ImageDecodeRequest_WithExpression_Rejects_Invalid_Values()
    {
        var request = new ImageDecodeRequest("photo.png", MaxDecodedBytes: 10);

        Assert.ThrowsExactly<ArgumentException>(() => request with { Path = " " });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => request with { MaxDecodedBytes = -1 });
    }
}
