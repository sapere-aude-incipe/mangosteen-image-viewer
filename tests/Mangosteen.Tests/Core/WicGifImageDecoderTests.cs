using Mangosteen.Core;
using Mangosteen.Decoding;
using ImageMagick;
using SkiaSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class WicGifImageDecoderTests
{
    [TestMethod]
    public void CreatePremultipliedSkiaImage_Removes_Hidden_Color_From_Transparent_Pixels()
    {
        byte[] pixels =
        [
            0, 0, 255, 0,
            255, 0, 0, 255
        ];
        var source = BitmapSource.Create(
            2,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8);

        using var image = WicGifImageDecoder.CreatePremultipliedSkiaImage(source, maxDecodedBytes: null);
        using var bitmap = SKBitmap.FromImage(image);
        var transparentPixel = bitmap.GetPixel(0, 0);

        Assert.AreEqual(0, transparentPixel.Red);
        Assert.AreEqual(0, transparentPixel.Green);
        Assert.AreEqual(0, transparentPixel.Blue);
        Assert.AreEqual(0, transparentPixel.Alpha);
    }

    [TestMethod]
    public async Task DecodeAsync_Matches_Full_Frames_When_Optimized_Transparency_Is_Scaled()
    {
        var path = CreateTempPath();
        var expectedFrames = new List<SKImage>();
        try
        {
            WriteOptimizedTransparencyGif(path);
            using (var expectedCollection = new MagickImageCollection(path))
            {
                expectedCollection.Coalesce();
                foreach (var frame in expectedCollection)
                {
                    expectedFrames.Add(SKImage.FromEncodedData(frame.ToByteArray(MagickFormat.Png)));
                }
            }

            var decoder = new WicGifImageDecoder();

            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(32, 32), FullResolution: false),
                CancellationToken.None);

            Assert.AreEqual(expectedFrames.Count, decoded.FrameCount);
            for (var index = 0; index < expectedFrames.Count; index++)
            {
                using var expected = ScaleImage(expectedFrames[index], 32, 32);
                AssertImagesSimilar(expected, decoded.Frames[index].Image, maximumMeanChannelDelta: 10);
            }
        }
        finally
        {
            foreach (var frame in expectedFrames)
            {
                frame.Dispose();
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public async Task DecodeAsync_Preserves_Animation_Frames_Timing_And_Preview_Size()
    {
        var path = CreateTempPath();
        try
        {
            WriteTwoFrameGif(path);
            var decoder = new WicGifImageDecoder();

            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(4, 4), FullResolution: false),
                CancellationToken.None);

            Assert.AreEqual("Windows GIF", decoded.Metadata.DecoderName);
            Assert.AreEqual(2, decoded.FrameCount);
            Assert.AreEqual(2, decoded.Metadata.FrameCount);
            Assert.AreEqual(4, decoded.Frames[0].Image.Width);
            Assert.AreEqual(4, decoded.Frames[0].Image.Height);
            Assert.AreEqual(TimeSpan.FromMilliseconds(50), decoded.Frames[0].Delay);
            Assert.AreEqual(TimeSpan.FromMilliseconds(120), decoded.Frames[1].Delay);
            AssertFrameColor(decoded.Frames[0].Image, SKColors.Red);
            AssertFrameColor(decoded.Frames[1].Image, SKColors.Blue);
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
    public async Task DecodeAsync_Rejects_Animation_When_All_Frames_Exceed_Memory_Limit()
    {
        var path = CreateTempPath();
        try
        {
            WriteTwoFrameGif(path);
            var decoder = new WicGifImageDecoder();

            var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => decoder.DecodeAsync(
                    new ImageDecodeRequest(path, FullResolution: true, MaxDecodedBytes: 8 * 8 * 4L),
                    CancellationToken.None));

            StringAssert.Contains(exception.Message, "exceeds the decode limit");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteTwoFrameGif(string path)
    {
        using var images = new MagickImageCollection();
        images.Add(new MagickImage(MagickColors.Red, 8, 8)
        {
            AnimationDelay = 5
        });
        images.Add(new MagickImage(MagickColors.Blue, 8, 8)
        {
            AnimationDelay = 12
        });
        images.Write(path);
    }

    private static void WriteOptimizedTransparencyGif(string path)
    {
        using var images = new MagickImageCollection();
        for (var index = 0; index < 4; index++)
        {
            using var image = new MagickImage(MagickColors.DarkSlateGray, 64, 64);
            using var square = new MagickImage(index % 2 == 0 ? MagickColors.OrangeRed : MagickColors.DeepSkyBlue, 20, 20);
            image.Composite(square, 3 + (index * 11), 7 + (index * 8), CompositeOperator.Over);
            image.AnimationDelay = 5;

            images.Add(image.Clone());
        }

        images.OptimizeTransparency();
        images.Write(path);
    }

    private static SKImage ScaleImage(SKImage source, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(
            source,
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static void AssertImagesSimilar(SKImage expected, SKImage actual, double maximumMeanChannelDelta)
    {
        using var expectedBitmap = SKBitmap.FromImage(expected);
        using var actualBitmap = SKBitmap.FromImage(actual);
        Assert.AreEqual(expectedBitmap.Width, actualBitmap.Width);
        Assert.AreEqual(expectedBitmap.Height, actualBitmap.Height);

        long totalChannelDelta = 0;
        for (var y = 0; y < expectedBitmap.Height; y++)
        {
            for (var x = 0; x < expectedBitmap.Width; x++)
            {
                var expectedPixel = expectedBitmap.GetPixel(x, y);
                var actualPixel = actualBitmap.GetPixel(x, y);
                totalChannelDelta += Math.Abs(expectedPixel.Red - actualPixel.Red);
                totalChannelDelta += Math.Abs(expectedPixel.Green - actualPixel.Green);
                totalChannelDelta += Math.Abs(expectedPixel.Blue - actualPixel.Blue);
                totalChannelDelta += Math.Abs(expectedPixel.Alpha - actualPixel.Alpha);
            }
        }

        var meanChannelDelta = totalChannelDelta / (double)(expectedBitmap.Width * expectedBitmap.Height * 4);
        Assert.IsLessThanOrEqualTo(maximumMeanChannelDelta, meanChannelDelta);
    }

    private static void AssertFrameColor(SKImage image, SKColor expected)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var actual = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.AreEqual(expected.Red, actual.Red);
        Assert.AreEqual(expected.Green, actual.Green);
        Assert.AreEqual(expected.Blue, actual.Blue);
    }

    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gif");
    }
}
