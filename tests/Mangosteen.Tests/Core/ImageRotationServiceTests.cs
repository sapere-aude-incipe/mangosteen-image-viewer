using ImageMagick;
using Mangosteen.Decoding;
using Mangosteen.Editing;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ImageRotationServiceTests
{
    [TestMethod]
    public async Task RotateAsync_Replaces_Png_With_Clockwise_Rotation()
    {
        var path = CreateTempPath(".png");
        try
        {
            WriteAsymmetricPng(path);
            var service = new ImageRotationService();

            var result = await service.RotateAsync(
                path,
                clockwiseQuarterTurns: 1,
                RotationSaveMode.ReplaceOriginal,
                CancellationToken.None);

            Assert.AreEqual(path, result);
            var decoder = new SkiaImageDecoder();
            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);
            Assert.AreEqual(3, decoded.Width);
            Assert.AreEqual(2, decoded.Height);

            using var bitmap = new SKBitmap(3, 2);
            Assert.IsTrue(decoded.Frames[0].Image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes));
            Assert.AreEqual(SKColors.Cyan, bitmap.GetPixel(0, 0));
            Assert.AreEqual(SKColors.Blue, bitmap.GetPixel(1, 0));
            Assert.AreEqual(SKColors.Red, bitmap.GetPixel(2, 0));
            Assert.AreEqual(SKColors.Magenta, bitmap.GetPixel(0, 1));
            Assert.AreEqual(SKColors.Yellow, bitmap.GetPixel(1, 1));
            Assert.AreEqual(SKColors.Green, bitmap.GetPixel(2, 1));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [TestMethod]
    public async Task RotateAsync_Saves_Exact_Rotated_Png_Copy_Without_Changing_Source()
    {
        var sourcePath = CreateTempPath(".jpg");
        var copyPath = ImageRotationService.GetDefaultPngCopyPath(sourcePath);
        try
        {
            using (var source = new MagickImage(MagickColors.CornflowerBlue, 8, 4))
            {
                source.Write(sourcePath, MagickFormat.Jpeg);
            }

            var originalBytes = await File.ReadAllBytesAsync(sourcePath);
            var service = new ImageRotationService();
            var result = await service.RotateAsync(
                sourcePath,
                clockwiseQuarterTurns: 3,
                RotationSaveMode.PngCopy,
                CancellationToken.None);

            Assert.AreEqual(copyPath, result);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(sourcePath));
            using var copy = new MagickImage(copyPath);
            Assert.AreEqual(4u, copy.Width);
            Assert.AreEqual(8u, copy.Height);
            Assert.AreEqual(MagickFormat.Png, copy.Format);
        }
        finally
        {
            DeleteIfExists(sourcePath);
            DeleteIfExists(copyPath);
        }
    }

    [TestMethod]
    public async Task GetWriteRiskAsync_Recognizes_Lossy_And_CopyOnly_Formats()
    {
        var service = new ImageRotationService();

        Assert.AreEqual(
            RotationWriteRisk.Lossy,
            await service.GetWriteRiskAsync("example.jpg", CancellationToken.None));
        Assert.AreEqual(
            RotationWriteRisk.PngCopyOnly,
            await service.GetWriteRiskAsync("camera.raf", CancellationToken.None));
    }

    [TestMethod]
    public async Task GetWriteRiskAsync_Detects_Jpeg_Compression_Inside_Tiff()
    {
        var path = CreateTempPath(".tiff");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 8, 8))
            {
                source.Settings.Compression = CompressionMethod.JPEG;
                source.Write(path, MagickFormat.Tiff);
            }

            var service = new ImageRotationService();
            Assert.AreEqual(
                RotationWriteRisk.Lossy,
                await service.GetWriteRiskAsync(path, CancellationToken.None));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [TestMethod]
    public async Task RotateAsync_Rotates_Every_Animated_Gif_Frame()
    {
        var path = CreateTempPath(".gif");
        try
        {
            using (var source = new MagickImageCollection())
            {
                source.Add(new MagickImage(MagickColors.Red, 8, 4) { AnimationDelay = 5 });
                source.Add(new MagickImage(MagickColors.Blue, 8, 4) { AnimationDelay = 7 });
                source.Write(path, MagickFormat.Gif);
            }

            var service = new ImageRotationService();
            await service.RotateAsync(
                path,
                clockwiseQuarterTurns: 1,
                RotationSaveMode.ReplaceOriginal,
                CancellationToken.None);

            using var rotated = new MagickImageCollection(path);
            Assert.AreEqual(2, rotated.Count);
            Assert.IsTrue(rotated.All(static frame => frame.Width == 4 && frame.Height == 8));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [TestMethod]
    public async Task RotateAsync_Canceled_Before_Start_Does_Not_Change_Source()
    {
        var path = CreateTempPath(".png");
        try
        {
            WriteAsymmetricPng(path);
            var originalBytes = await File.ReadAllBytesAsync(path);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var service = new ImageRotationService();
            try
            {
                await service.RotateAsync(
                    path,
                    clockwiseQuarterTurns: 1,
                    RotationSaveMode.ReplaceOriginal,
                    cancellation.Token);
                Assert.Fail("A pre-canceled rotation should not start.");
            }
            catch (OperationCanceledException)
            {
            }

            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    private static void WriteAsymmetricPng(string path)
    {
        using var bitmap = new SKBitmap(2, 3);
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Green);
        bitmap.SetPixel(0, 1, SKColors.Blue);
        bitmap.SetPixel(1, 1, SKColors.Yellow);
        bitmap.SetPixel(0, 2, SKColors.Cyan);
        bitmap.SetPixel(1, 2, SKColors.Magenta);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static string CreateTempPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"mangosteen-rotation-{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
