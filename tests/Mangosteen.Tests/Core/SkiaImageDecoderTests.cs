using Mangosteen.Core;
using Mangosteen.Decoding;
using ImageMagick;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class SkiaImageDecoderTests
{
    [TestMethod]
    public async Task LoadMetadataAsync_Reports_Single_Frame_For_Current_Decode_Path()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 8, 8))
            {
                source.Write(path);
            }

            var decoder = new SkiaImageDecoder();
            var metadata = await decoder.LoadMetadataAsync(path, CancellationToken.None);

            Assert.AreEqual(1, metadata.FrameCount);
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
            using (var source = new MagickImage(MagickColors.Red, 8, 4))
            {
                SetExifOrientation(source, OrientationType.RightTop);
                source.Write(path);
            }

            var decoder = new SkiaImageDecoder();
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
            using (var source = new MagickImage(MagickColors.Red, 8, 8))
            {
                source.Write(path);
            }

            var decoder = new SkiaImageDecoder();
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
    public void GetSkiaDecodeSize_Returns_Source_Size_For_FullResolution()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 128, 64))
            {
                source.Write(path);
            }

            using var codec = SKCodec.Create(path);
            Assert.IsNotNull(codec);

            var size = SkiaImageDecoder.GetSkiaDecodeSize(
                codec,
                new PixelSize(16, 8),
                fullResolution: true);

            Assert.AreEqual(new PixelSize(128, 64), size);
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
    public async Task DecodeAsync_Applies_MaxDecodedBytes_To_Intermediate_Scaled_Decode()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 128, 128))
            {
                source.Write(path);
            }

            var decoder = new SkiaImageDecoder();
            using var codec = SKCodec.Create(path);
            Assert.IsNotNull(codec);

            var previewTarget = new PixelSize(8, 8);
            var skiaDecodeSize = SkiaImageDecoder.GetSkiaDecodeSize(codec, previewTarget, fullResolution: false);
            if (skiaDecodeSize == previewTarget)
            {
                Assert.Inconclusive("This Skia codec build can decode the test PNG directly to the requested preview size.");
            }

            InvalidDataException? ex = null;
            try
            {
                using var _ = await decoder.DecodeAsync(
                    new ImageDecodeRequest(path, previewTarget, FullResolution: false, MaxDecodedBytes: 8 * 8 * 4L),
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
    [DataRow(".png")]
    [DataRow(".jpg")]
    public async Task DecodeAsync_Stops_Reading_The_File_Promptly_When_Cancelled(string extension)
    {
        var path = CreateTempImagePath(extension);
        try
        {
            using (var source = new MagickImage("plasma:", 1500, 1500))
            {
                source.Quality = 90;
                source.Write(path);
            }

            var length = new FileInfo(path).Length;
            var decoder = new SkiaImageDecoder();
            using var cts = new CancellationTokenSource();
            long lastRead = 0;
            SkiaImageDecoder.FileRead.Value = bytes =>
            {
                lastRead = bytes;
                if (bytes > length / 4) cts.Cancel();
            };
            try
            {
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => decoder.DecodeAsync(new ImageDecodeRequest(path, FullResolution: true), cts.Token));
            }
            finally
            {
                SkiaImageDecoder.FileRead.Value = null;
            }

            Assert.IsLessThan(length * 3 / 4, lastRead, "The decoder kept reading after cancellation.");

            // The same file still decodes normally afterwards.
            using var image = await decoder.DecodeAsync(new ImageDecodeRequest(path, FullResolution: true), CancellationToken.None);
            Assert.AreEqual(1500, image.Frames[0].Image.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(".png")]
    [DataRow(".jpg")]
    [DataRow(".webp")]
    public async Task DecodeAsync_Cancellable_Stream_Decode_Matches_Path_Decode(string extension)
    {
        var path = CreateTempImagePath(extension);
        try
        {
            using (var source = new MagickImage("plasma:", 700, 900))
            {
                source.Quality = 90;
                source.Write(path);
            }

            using var image = await new SkiaImageDecoder().DecodeAsync(new ImageDecodeRequest(path, FullResolution: true), CancellationToken.None);
            using var decoded = SKBitmap.FromImage(image.Frames[0].Image);

            using var codec = SKCodec.Create(path);
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var expected = new SKBitmap(info);
            Assert.AreEqual(SKCodecResult.Success, codec.GetPixels(info, expected.GetPixels()));

            CollectionAssert.AreEqual(expected.GetPixelSpan().ToArray(), decoded.GetPixelSpan().ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task DecodeAsync_Truncated_Png_Still_Decodes_The_Rows_Present()
    {
        var path = CreateTempImagePath(".png");
        try
        {
            using (var source = new MagickImage(MagickColors.Red, 512, 4096))
            {
                source.Settings.SetDefine(MagickFormat.Png, "compression-level", "0");
                source.Write(path);
            }

            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

            using var image = await new SkiaImageDecoder().DecodeAsync(new ImageDecodeRequest(path, FullResolution: true), CancellationToken.None);
            using var bitmap = SKBitmap.FromImage(image.Frames[0].Image);

            Assert.AreEqual(SKColors.Red, bitmap.GetPixel(10, 10));
            Assert.AreEqual((byte)0, bitmap.GetPixel(10, 4095).Alpha);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CreateBitmapOrThrow_Rejects_Single_Frame_Buffers_Too_Large_For_Skia()
    {
        var width = (int)(ImageDecodeGuards.MaxSingleFrameBgraBufferBytes / 4L) + 1;
        var info = new SKImageInfo(width, 1, SKColorType.Bgra8888, SKAlphaType.Premul);

        var ex = Assert.ThrowsExactly<InvalidDataException>(
            () =>
            {
                using var _ = SkiaImageDecoder.CreateBitmapOrThrow(info, "test bitmap");
            });

        StringAssert.Contains(ex.Message, "single-frame buffer limit");
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
