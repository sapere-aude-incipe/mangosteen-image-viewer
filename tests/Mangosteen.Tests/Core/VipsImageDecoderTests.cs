using Mangosteen.Core;
using Mangosteen.Decoding;
using ImageMagick;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class VipsImageDecoderTests
{
    [TestMethod]
    public async Task DecodeAsync_Decodes_Preview_To_Target_Size()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 64, 32))
            {
                source.Write(path);
            }

            var decoder = new VipsImageDecoder();
            using var image = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(16, 16)),
                CancellationToken.None);

            Assert.AreEqual("libvips", image.Metadata.DecoderName);
            Assert.IsFalse(image.IsFullResolution);
            Assert.AreEqual(64, image.Width);
            Assert.AreEqual(32, image.Height);
            Assert.AreEqual(16, image.Frames[0].Image.Width);
            Assert.AreEqual(8, image.Frames[0].Image.Height);

            using var bitmap = SKBitmap.FromImage(image.Frames[0].Image);
            var pixel = bitmap.GetPixel(0, 0);
            Assert.IsGreaterThan((byte)240, pixel.Red);
            Assert.IsLessThan((byte)15, pixel.Green);
            Assert.IsLessThan((byte)15, pixel.Blue);
            Assert.IsGreaterThan((byte)240, pixel.Alpha);
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
    public async Task DecodeAsync_Decodes_FullResolution_Image()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.CornflowerBlue, 64, 32))
            {
                source.Write(path);
            }

            var decoder = new VipsImageDecoder();
            using var image = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);

            Assert.IsTrue(image.IsFullResolution);
            Assert.AreEqual(64, image.Width);
            Assert.AreEqual(32, image.Height);
            Assert.AreEqual(64, image.Frames[0].Image.Width);
            Assert.AreEqual(32, image.Frames[0].Image.Height);
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
    public void CanDecode_Returns_False_For_Gif_To_Preserve_Animation_Path()
    {
        var path = CreateTempImagePath(".gif");
        try
        {
            File.WriteAllBytes(path, []);

            var decoder = new VipsImageDecoder();

            Assert.IsFalse(decoder.CanDecode(path));
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
    public void UsesDecoderSidePreview_Returns_True_For_Smaller_Preview_Request()
    {
        var metadata = new ImageMetadata("large.jpg", 8000, 4000, 1, "test");
        var request = new ImageDecodeRequest("large.jpg", new PixelSize(1600, 900), FullResolution: false);

        Assert.IsTrue(VipsImageDecoder.UsesDecoderSidePreview(request, metadata));
    }

    [TestMethod]
    public void UsesDecoderSidePreview_Returns_False_For_Full_Resolution_Request()
    {
        var metadata = new ImageMetadata("large.jpg", 8000, 4000, 1, "test");
        var request = new ImageDecodeRequest("large.jpg", new PixelSize(1600, 900), FullResolution: true);

        Assert.IsFalse(VipsImageDecoder.UsesDecoderSidePreview(request, metadata));
    }

    private static string CreateTempImagePath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
    }
}
