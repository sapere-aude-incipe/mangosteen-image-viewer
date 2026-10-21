using ImageMagick;
using Mangosteen.Core;
using Mangosteen.Decoding;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // libvips and Magick.NET keep process-global caches.
public sealed class DecoderFileReuseTests
{
    [TestMethod]
    public async Task Vips_Decode_Does_Not_Lock_Or_Serve_Stale_Replaced_File()
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-vips-reuse-");
        try
        {
            var path = Path.Combine(directory.FullName, "photo.tif");
            WriteSolidTiff(path, 40, 20, MagickColors.Red);
            var decoder = new VipsImageDecoder();

            foreach (var fullResolution in new[] { false, true })
            {
                using (var first = await decoder.DecodeAsync(new ImageDecodeRequest(path, new PixelSize(400, 400), fullResolution), CancellationToken.None))
                {
                    Assert.AreEqual(40, first.Frames[0].Image.Width);
                }

                // Rotation writes a temp file beside the original and moves it over the original.
                var replacement = Path.Combine(directory.FullName, ".photo.tmp.tif");
                WriteSolidTiff(replacement, 20, 40, MagickColors.Blue);
                File.Move(replacement, path, overwrite: true);

                using var second = await decoder.DecodeAsync(new ImageDecodeRequest(path, new PixelSize(400, 400), fullResolution), CancellationToken.None);
                Assert.AreEqual(20, second.Frames[0].Image.Width, $"Stale decode (fullResolution={fullResolution}).");
                AssertCenterPixel(second.Frames[0].Image, SKColors.Blue);

                WriteSolidTiff(path, 40, 20, MagickColors.Red);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task Magick_Decode_Converts_Cmyk_To_Display_Colors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-cmyk-{Guid.NewGuid():N}.tif");
        try
        {
            using (var image = new MagickImage(MagickColors.Red, 8, 8))
            {
                image.ColorSpace = ColorSpace.CMYK;
                image.Write(path, MagickFormat.Tiff);
            }

            using var decoded = await new MagickImageDecoder().DecodeAsync(new ImageDecodeRequest(path, FullResolution: true), CancellationToken.None);

            AssertCenterPixel(decoded.Frames[0].Image, SKColors.Red);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteSolidTiff(string path, uint width, uint height, MagickColor color)
    {
        using var image = new MagickImage(color, width, height);
        image.Write(path, MagickFormat.Tiff);
    }

    private static void AssertCenterPixel(SKImage image, SKColor expected)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var actual = bitmap.GetPixel(image.Width / 2, image.Height / 2);
        Assert.IsTrue(
            Math.Abs(actual.Red - expected.Red) < 24 &&
            Math.Abs(actual.Green - expected.Green) < 24 &&
            Math.Abs(actual.Blue - expected.Blue) < 24,
            $"Expected {expected}, got {actual}.");
    }
}
