using ClassicPhotoViewer.Decoding;
using SkiaSharp;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class ImageOrientationTests
{
    [TestMethod]
    public void ApplyAndDisposeSource_Maps_Pixels_For_All_Exif_Orientations()
    {
        var cases = new (SKEncodedOrigin Origin, SKColor[][] Expected)[]
        {
            (SKEncodedOrigin.TopLeft, [
                [SKColors.Red, SKColors.Green, SKColors.Blue],
                [SKColors.Cyan, SKColors.Magenta, SKColors.Yellow]
            ]),
            (SKEncodedOrigin.TopRight, [
                [SKColors.Blue, SKColors.Green, SKColors.Red],
                [SKColors.Yellow, SKColors.Magenta, SKColors.Cyan]
            ]),
            (SKEncodedOrigin.BottomRight, [
                [SKColors.Yellow, SKColors.Magenta, SKColors.Cyan],
                [SKColors.Blue, SKColors.Green, SKColors.Red]
            ]),
            (SKEncodedOrigin.BottomLeft, [
                [SKColors.Cyan, SKColors.Magenta, SKColors.Yellow],
                [SKColors.Red, SKColors.Green, SKColors.Blue]
            ]),
            (SKEncodedOrigin.LeftTop, [
                [SKColors.Red, SKColors.Cyan],
                [SKColors.Green, SKColors.Magenta],
                [SKColors.Blue, SKColors.Yellow]
            ]),
            (SKEncodedOrigin.RightTop, [
                [SKColors.Cyan, SKColors.Red],
                [SKColors.Magenta, SKColors.Green],
                [SKColors.Yellow, SKColors.Blue]
            ]),
            (SKEncodedOrigin.RightBottom, [
                [SKColors.Yellow, SKColors.Blue],
                [SKColors.Magenta, SKColors.Green],
                [SKColors.Cyan, SKColors.Red]
            ]),
            (SKEncodedOrigin.LeftBottom, [
                [SKColors.Blue, SKColors.Yellow],
                [SKColors.Green, SKColors.Magenta],
                [SKColors.Red, SKColors.Cyan]
            ])
        };

        foreach (var testCase in cases)
        {
            using var image = ImageOrientation.ApplyAndDisposeSource(CreateSourceImage(), testCase.Origin);
            AssertPixels(testCase.Origin.ToString(), image, testCase.Expected);
        }
    }

    [TestMethod]
    public void CreateSurfaceOrThrow_Rejects_Oriented_Surface_Buffers_Too_Large_For_Skia()
    {
        var width = (int)(ImageDecodeGuards.MaxSingleFrameBgraBufferBytes / 4L) + 1;
        var info = new SKImageInfo(width, 1, SKColorType.Bgra8888, SKAlphaType.Premul);

        var ex = Assert.ThrowsExactly<InvalidDataException>(
            () =>
            {
                using var _ = ImageOrientation.CreateSurfaceOrThrow(info);
            });

        StringAssert.Contains(ex.Message, "single-frame buffer limit");
    }

    private static SKImage CreateSourceImage()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Green);
        bitmap.SetPixel(2, 0, SKColors.Blue);
        bitmap.SetPixel(0, 1, SKColors.Cyan);
        bitmap.SetPixel(1, 1, SKColors.Magenta);
        bitmap.SetPixel(2, 1, SKColors.Yellow);
        return SKImage.FromBitmap(bitmap);
    }

    private static void AssertPixels(string name, SKImage image, SKColor[][] expected)
    {
        using var bitmap = SKBitmap.FromImage(image);
        Assert.AreEqual(expected[0].Length, bitmap.Width, $"{name} width");
        Assert.AreEqual(expected.Length, bitmap.Height, $"{name} height");

        for (var y = 0; y < expected.Length; y++)
        {
            for (var x = 0; x < expected[y].Length; x++)
            {
                Assert.AreEqual(expected[y][x], bitmap.GetPixel(x, y), $"{name} pixel {x},{y}");
            }
        }
    }
}
