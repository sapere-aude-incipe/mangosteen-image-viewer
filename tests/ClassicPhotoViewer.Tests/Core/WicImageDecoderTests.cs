using ClassicPhotoViewer.Core;
using ClassicPhotoViewer.Decoding;
using ImageMagick;
using SkiaSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class WicImageDecoderTests
{
    [TestMethod]
    public async Task DecodeAsync_Creates_Scaled_Preview_With_Windows_Codecs()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            WritePng(path, 32, 16);

            var decoder = new WicImageDecoder();
            using var image = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(8, 8), FullResolution: false),
                CancellationToken.None);

            Assert.AreEqual("Windows Imaging Component", image.Metadata.DecoderName);
            Assert.AreEqual(32, image.Width);
            Assert.AreEqual(16, image.Height);
            Assert.IsFalse(image.IsFullResolution);
            Assert.AreEqual(8, image.Frames[0].Image.Width);
            Assert.AreEqual(4, image.Frames[0].Image.Height);
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
    public async Task DecodeAsync_Applies_Exif_Orientation()
    {
        var path = CreateTempImagePath(".jpg");
        try
        {
            WriteJpegWithOrientation(path, 8, 4, OrientationType.RightTop);

            var decoder = new WicImageDecoder();
            using var image = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);

            Assert.AreEqual(4, image.Width);
            Assert.AreEqual(8, image.Height);
            Assert.AreEqual(4, image.Frames[0].Image.Width);
            Assert.AreEqual(8, image.Frames[0].Image.Height);
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
    public async Task DecodeAsync_Rejects_Full_Decode_When_MaxDecodedBytes_Is_Too_Low()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            WritePng(path, 8, 8);

            var decoder = new WicImageDecoder();
            InvalidDataException? ex = null;
            try
            {
                using var _ = await decoder.DecodeAsync(
                    new ImageDecodeRequest(path, FullResolution: true, MaxDecodedBytes: 8 * 8 * 4L - 1),
                    CancellationToken.None);
            }
            catch (InvalidDataException caught)
            {
                ex = caught;
            }

            Assert.IsNotNull(ex);
            StringAssert.Contains(ex.Message, "exceeds the decode limit");
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
    public void InitialDecodeCacheOption_Is_OnDemand_So_Guards_Run_Before_Pixel_Cache()
    {
        Assert.AreEqual(
            System.Windows.Media.Imaging.BitmapCacheOption.OnDemand,
            WicImageDecoder.InitialDecodeCacheOption);
    }

    [TestMethod]
    public async Task DecodeAsync_Uses_First_Frame_For_Animated_Preview_Even_When_Target_Is_Full_Size()
    {
        var path = CreateTempImagePath(".gif");
        try
        {
            WriteGif(path, 8, 8);

            var decoder = new WicImageDecoder();
            using var image = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(16, 16), FullResolution: false),
                CancellationToken.None);

            Assert.IsFalse(image.IsFullResolution);
            Assert.AreEqual(1, image.FrameCount);
            Assert.AreEqual(8, image.Frames[0].Image.Width);
            Assert.AreEqual(8, image.Frames[0].Image.Height);
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
    public void CanDecode_Returns_False_For_Gif_So_Magick_Can_Coalesce_Animation_Frames()
    {
        var path = CreateTempImagePath(".gif");
        try
        {
            File.WriteAllBytes(path, []);
            var decoder = new WicImageDecoder();

            Assert.IsFalse(decoder.CanDecode(path));
            CollectionAssert.DoesNotContain(decoder.SupportedExtensions.ToList(), ".gif");
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
    public void FitEmbeddedPreviewToTarget_Scales_Oversized_Unoriented_Preview()
    {
        var source = CreateBitmapSource(200, 100);

        var scaled = WicImageDecoder.FitEmbeddedPreviewToTarget(
            source,
            new PixelSize(100, 100),
            SKEncodedOrigin.TopLeft);

        Assert.AreEqual(100, scaled.PixelWidth);
        Assert.AreEqual(50, scaled.PixelHeight);
    }

    [TestMethod]
    public void FitEmbeddedPreviewToTarget_Scales_Oversized_Oriented_Preview_On_Raw_Axes()
    {
        var source = CreateBitmapSource(200, 100);

        var scaled = WicImageDecoder.FitEmbeddedPreviewToTarget(
            source,
            new PixelSize(100, 100),
            SKEncodedOrigin.RightTop);

        Assert.AreEqual(100, scaled.PixelWidth);
        Assert.AreEqual(50, scaled.PixelHeight);
        var oriented = ImageOrientation.GetOrientedSize(scaled.PixelWidth, scaled.PixelHeight, SKEncodedOrigin.RightTop);
        Assert.AreEqual(50, oriented.Width);
        Assert.AreEqual(100, oriented.Height);
    }

    [TestMethod]
    public void FitEmbeddedPreviewToTarget_Does_Not_Upscale_Small_Preview()
    {
        var source = CreateBitmapSource(50, 25);

        var scaled = WicImageDecoder.FitEmbeddedPreviewToTarget(
            source,
            new PixelSize(100, 100),
            SKEncodedOrigin.TopLeft);

        Assert.AreSame(source, scaled);
    }

    [TestMethod]
    public void IsEmbeddedPreviewUsable_Rejects_When_Either_Axis_Is_Too_Small()
    {
        Assert.IsFalse(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(200, 100),
            new PixelSize(1000, 100)));
        Assert.IsFalse(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(1000, 20),
            new PixelSize(1000, 100)));
    }

    [TestMethod]
    public void IsEmbeddedPreviewUsable_Rejects_When_Either_Axis_Is_Too_Large()
    {
        Assert.IsFalse(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(2100, 100),
            new PixelSize(1000, 100)));
        Assert.IsFalse(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(1000, 250),
            new PixelSize(1000, 100)));
    }

    [TestMethod]
    public void IsEmbeddedPreviewUsable_Accepts_Reasonably_Sized_Preview()
    {
        Assert.IsTrue(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(800, 80),
            new PixelSize(1000, 100)));
    }

    [TestMethod]
    public void IsEmbeddedPreviewUsable_Rejects_Mismatched_Aspect_Ratio()
    {
        Assert.IsFalse(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(500, 500),
            new PixelSize(1000, 500)));
    }

    [TestMethod]
    public void IsEmbeddedPreviewUsable_Accepts_Slightly_Rounded_Aspect_Ratio()
    {
        Assert.IsTrue(WicImageDecoder.IsEmbeddedPreviewUsable(
            new PixelSize(790, 82),
            new PixelSize(1000, 100)));
    }

    private static void WritePng(string path, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void WriteGif(string path, int width, int height)
    {
        using var images = new MagickImageCollection();
        images.Add(new MagickImage(MagickColors.Red, (uint)width, (uint)height)
        {
            AnimationDelay = 10
        });
        images.Add(new MagickImage(MagickColors.Blue, (uint)width, (uint)height)
        {
            AnimationDelay = 10
        });
        images.Write(path);
    }

    private static BitmapSource CreateBitmapSource(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        source.Freeze();
        return source;
    }

    private static void WriteJpegWithOrientation(string path, int width, int height, OrientationType orientation)
    {
        using var image = new MagickImage(MagickColors.Red, (uint)width, (uint)height)
        {
            Orientation = orientation
        };
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)orientation);
        image.SetProfile(profile);
        image.Write(path);
    }

    private static string CreateTempImagePath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
    }
}
