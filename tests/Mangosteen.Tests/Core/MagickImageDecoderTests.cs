using Mangosteen.Core;
using Mangosteen.Decoding;
using ImageMagick;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class MagickImageDecoderTests
{
    [TestMethod]
    public async Task DecodeAsync_Uses_Raw_Pixel_Transfer_For_Fallback_Formats()
    {
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 8, 8))
            {
                source.Write(path);
            }

            var decoder = new MagickImageDecoder();
            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(4, 4), FullResolution: false),
                CancellationToken.None);

            Assert.AreEqual(8, decoded.Width);
            Assert.AreEqual(8, decoded.Height);
            Assert.AreEqual(4, decoded.Frames[0].Image.Width);
            Assert.AreEqual(4, decoded.Frames[0].Image.Height);
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
    public void CanDecode_Rejects_Existing_Files_With_Unknown_Extensions()
    {
        var path = CreateTempImagePath(".unknownimage");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 8, 8))
            {
                source.Format = MagickFormat.Png;
                source.Write(path);
            }

            var decoder = new MagickImageDecoder();
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
    public async Task DecodeAsync_Pixels_Remain_Readable_After_Magick_Source_Is_Disposed()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 2, 2))
            {
                source.Format = MagickFormat.Png;
                source.Write(path);
            }

            var decoder = new MagickImageDecoder();
            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            Assert.IsTrue(decoded.Frames[0].Image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes));
            Assert.AreEqual(SKColors.Red, bitmap.GetPixel(0, 0));
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
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 8, 8))
            {
                source.Write(path);
            }

            var decoder = new MagickImageDecoder();
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
    public async Task DecodeAsync_Uses_First_Frame_For_NonAnimated_MultiFrame_Files()
    {
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var images = new MagickImageCollection())
            {
                images.Add(new MagickImage(MagickColors.Red, 8, 8));
                images.Add(new MagickImage(MagickColors.Blue, 8, 8));
                images.Write(path);
            }

            var decoder = new MagickImageDecoder();
            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);

            Assert.AreEqual(1, decoded.FrameCount);
            Assert.AreEqual(1, decoded.Metadata.FrameCount);
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
    public async Task DecodeAsync_Uses_First_Frame_For_Animated_Preview()
    {
        var path = CreateTempImagePath(".gif");
        try
        {
            using (var images = new MagickImageCollection())
            {
                images.Add(new MagickImage(MagickColors.Red, 16, 16)
                {
                    AnimationDelay = 10
                });
                images.Add(new MagickImage(MagickColors.Blue, 16, 16)
                {
                    AnimationDelay = 10
                });
                images.Write(path);
            }

            var decoder = new MagickImageDecoder();
            using var preview = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(4, 4), FullResolution: false),
                CancellationToken.None);
            using var full = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);

            Assert.IsFalse(preview.IsFullResolution);
            Assert.AreEqual(1, preview.FrameCount);
            Assert.AreEqual(4, preview.Frames[0].Image.Width);
            Assert.AreEqual(4, preview.Frames[0].Image.Height);
            Assert.AreEqual(2, full.FrameCount);
            Assert.AreEqual(2, full.Metadata.FrameCount);
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
    public async Task DecodeAsync_Uses_First_Frame_For_Animated_Preview_Even_When_Target_Is_Full_Size()
    {
        var path = CreateTempImagePath(".gif");
        try
        {
            using (var images = new MagickImageCollection())
            {
                images.Add(new MagickImage(MagickColors.Red, 8, 8)
                {
                    AnimationDelay = 10
                });
                images.Add(new MagickImage(MagickColors.Blue, 8, 8)
                {
                    AnimationDelay = 10
                });
                images.Write(path);
            }

            var decoder = new MagickImageDecoder();
            using var preview = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(16, 16), FullResolution: false),
                CancellationToken.None);

            Assert.IsFalse(preview.IsFullResolution);
            Assert.AreEqual(1, preview.FrameCount);
            Assert.AreEqual(8, preview.Frames[0].Image.Width);
            Assert.AreEqual(8, preview.Frames[0].Image.Height);
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
    public async Task DecodeAsync_Guarded_Full_Animated_Decode_Counts_All_Frames()
    {
        var path = CreateTempImagePath(".gif");
        try
        {
            using (var images = new MagickImageCollection())
            {
                images.Add(new MagickImage(MagickColors.Red, 8, 8)
                {
                    AnimationDelay = 10
                });
                images.Add(new MagickImage(MagickColors.Blue, 8, 8)
                {
                    AnimationDelay = 10
                });
                images.Write(path);
            }

            var decoder = new MagickImageDecoder();
            InvalidDataException? ex = null;
            try
            {
                using var _ = await decoder.DecodeAsync(
                    new ImageDecodeRequest(path, FullResolution: true, MaxDecodedBytes: 8 * 8 * 4L),
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
    public void GetFrameDelay_Uses_Animation_Ticks_Per_Second()
    {
        using var frame = new MagickImage(MagickColors.Red, 1, 1)
        {
            AnimationDelay = 1,
            AnimationTicksPerSecond = 10
        };

        var delay = MagickImageDecoder.GetFrameDelay(frame);

        Assert.AreEqual(100, delay.TotalMilliseconds, 0.001);
    }

    [TestMethod]
    public void GetFrameDelay_Clamps_Very_Short_Delays()
    {
        using var frame = new MagickImage(MagickColors.Red, 1, 1)
        {
            AnimationDelay = 1,
            AnimationTicksPerSecond = 1000
        };

        var delay = MagickImageDecoder.GetFrameDelay(frame);

        Assert.AreEqual(20, delay.TotalMilliseconds, 0.001);
    }

    [TestMethod]
    public async Task DecodeAsync_Applies_Exif_Orientation()
    {
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 8, 4))
            {
                SetExifOrientation(source, OrientationType.RightTop);
                source.Write(path);
            }

            var decoder = new MagickImageDecoder();
            var metadata = await decoder.LoadMetadataAsync(path, CancellationToken.None);
            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, FullResolution: true),
                CancellationToken.None);

            Assert.AreEqual(4, metadata.Width);
            Assert.AreEqual(8, metadata.Height);
            Assert.AreEqual(4, decoded.Width);
            Assert.AreEqual(8, decoded.Height);
            Assert.AreEqual(4, decoded.Frames[0].Image.Width);
            Assert.AreEqual(8, decoded.Frames[0].Image.Height);
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
    public async Task DecodeAsync_Uses_Raw_Axes_For_Exif_Oriented_Preview_Target()
    {
        var path = CreateTempImagePath(".tiff");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 80, 40))
            {
                SetExifOrientation(source, OrientationType.RightTop);
                source.Write(path);
            }

            var decoder = new MagickImageDecoder();
            using var decoded = await decoder.DecodeAsync(
                new ImageDecodeRequest(path, new PixelSize(20, 20), FullResolution: false),
                CancellationToken.None);

            Assert.IsFalse(decoded.IsFullResolution);
            Assert.AreEqual(40, decoded.Width);
            Assert.AreEqual(80, decoded.Height);
            Assert.AreEqual(10, decoded.Frames[0].Image.Width);
            Assert.AreEqual(20, decoded.Frames[0].Image.Height);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void SetExifOrientation(MagickImage image, OrientationType orientation)
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)orientation);
        image.SetProfile(profile);
        image.Orientation = orientation;
    }

    private static string CreateTempImagePath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
    }
}
