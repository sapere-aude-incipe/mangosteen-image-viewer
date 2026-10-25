using Mangosteen.Core;
using Mangosteen.Decoding;
using ImageMagick;
using SkiaSharp;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class VipsImageDecoderTests
{
    [TestMethod]
    public async Task Embedded_Profile_Has_Consistent_Colors_In_Preview_Full_Image_And_Tiles()
    {
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var fixture = new MagickImage(new MagickColor("#809060"), 128, 128))
            {
                fixture.SetProfile(ColorProfiles.AdobeRGB1998);
                fixture.Depth = 8;
                fixture.Write(path);
            }
            var decoder = new VipsImageDecoder();
            using var full = await decoder.DecodeAsync(new ImageDecodeRequest(path, FullResolution: true), CancellationToken.None);
            using var preview = await decoder.DecodeAsync(new ImageDecodeRequest(path, new PixelSize(64, 64)), CancellationToken.None);
            using var fullPixels = SKBitmap.FromImage(full.Frames[0].Image);
            using var previewPixels = SKBitmap.FromImage(preview.Frames[0].Image);
            var pixel = fullPixels.GetPixel(32, 32);
            Assert.AreEqual(pixel, previewPixels.GetPixel(32, 32));
            Assert.AreNotEqual(new SKColor(128, 144, 96), pixel);
            var tile = await new VipsLargeImageSource().DecodeTileAsync(path, new ImagePyramid(128, 128), new ImageTileKey(0, 0, 0), CancellationToken.None);
            Assert.AreEqual(pixel.Red, tile.Pixels[0]);
            Assert.AreEqual(pixel.Green, tile.Pixels[1]);
            Assert.AreEqual(pixel.Blue, tile.Pixels[2]);
        }
        finally { File.Delete(path); }
    }

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
    public async Task DecodeAsync_Cancelled_During_Evaluation_Throws_And_Does_Not_Poison_Later_Decodes()
    {
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var source = new MagickImage("gradient:red-blue", 2400, 1600))
            {
                source.Depth = 16;
                source.Write(path);
            }

            var decoder = new VipsImageDecoder();
            var request = new ImageDecodeRequest(path, FullResolution: true);
            var reference = await DecodePixelHash(decoder, request);

            // Cancel before, at the start of, and part-way through evaluation; whichever point
            // the cancellation lands at, the cached libvips pipeline must still decode afterwards.
            foreach (var delayMs in new[] { -1, 0, 1, 3, 8, 20 })
            {
                using var cts = new CancellationTokenSource();
                VipsImageDecoder.BeforePixelEvaluation.Value = delayMs < 0 ? cts.Cancel : () => cts.CancelAfter(delayMs);
                try
                {
                    using var image = await decoder.DecodeAsync(request, cts.Token);
                    Assert.IsFalse(cts.IsCancellationRequested && delayMs < 0, "A decode cancelled before evaluation returned pixels.");
                }
                catch (OperationCanceledException)
                {
                    Assert.IsTrue(cts.IsCancellationRequested);
                }
                finally
                {
                    VipsImageDecoder.BeforePixelEvaluation.Value = null;
                }

                Assert.AreEqual(reference, await DecodePixelHash(decoder, request), $"Decode after cancelling at {delayMs} ms differs.");
            }
        }
        finally { File.Delete(path); }
    }

    private static async Task<string> DecodePixelHash(VipsImageDecoder decoder, ImageDecodeRequest request)
    {
        using var image = await decoder.DecodeAsync(request, CancellationToken.None);
        using var bitmap = SKBitmap.FromImage(image.Frames[0].Image);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bitmap.GetPixelSpan()));
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
