using ImageMagick;
using Mangosteen.Caching;
using Mangosteen.Core;
using Mangosteen.Decoding;
using Mangosteen.Rendering.Advanced;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // libvips and Magick.NET keep process-global caches.
public sealed class DecodeLatencyBehaviorTests
{
    private static readonly PixelSize Viewport = new(400, 300);

    [TestMethod]
    [DataRow(".png", MagickFormat.Png, true)]
    [DataRow(".webp", MagickFormat.WebP, true)]
    [DataRow(".jpg", MagickFormat.Jpeg, false)]
    public async Task Interactive_Preview_Returns_Full_Image_When_Skia_Cannot_Decode_Smaller(string extension, MagickFormat format, bool expectFull)
    {
        var path = CreateImage(extension, format, 2400, 1600);
        try
        {
            var decoder = new SkiaImageDecoder();
            using var interactive = await decoder.DecodeAsync(new ImageDecodeRequest(path, Viewport) { PreferFullResolutionWhenUnscalable = true }, CancellationToken.None);
            using var preload = await decoder.DecodeAsync(new ImageDecodeRequest(path, Viewport), CancellationToken.None);

            Assert.AreEqual(expectFull, interactive.IsFullResolution);
            Assert.AreEqual(expectFull ? 2400 : interactive.Frames[0].Image.Width, interactive.Frames[0].Image.Width);
            Assert.IsFalse(preload.IsFullResolution, "Preloads keep small previews to protect memory.");
            Assert.IsLessThan(2400, preload.Frames[0].Image.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Interactive_Preview_Returns_Full_Image_For_Plain_Tiff_Through_Libvips()
    {
        var path = CreateImage(".tif", MagickFormat.Tiff, 2400, 1600);
        try
        {
            var decoder = new VipsImageDecoder();
            using var interactive = await decoder.DecodeAsync(new ImageDecodeRequest(path, Viewport) { PreferFullResolutionWhenUnscalable = true }, CancellationToken.None);
            using var preload = await decoder.DecodeAsync(new ImageDecodeRequest(path, Viewport), CancellationToken.None);

            Assert.IsTrue(interactive.IsFullResolution);
            Assert.AreEqual(2400, interactive.Frames[0].Image.Width);
            Assert.AreEqual(SKAlphaType.Opaque, interactive.Frames[0].Image.AlphaType, "Images without alpha are marked opaque.");
            Assert.IsFalse(preload.IsFullResolution);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task Direct_Full_Decode_Respects_The_Decode_Limit()
    {
        var path = CreateImage(".png", MagickFormat.Png, 2400, 1600);
        try
        {
            using var codec = SKCodec.Create(path);
            var full = new PixelSize(2400, 1600);
            var interactive = new ImageDecodeRequest(path, Viewport) { PreferFullResolutionWhenUnscalable = true };

            Assert.IsTrue(SkiaImageDecoder.ShouldDecodeFullInsteadOfPreview(interactive, codec, full, isFull: false));
            Assert.IsFalse(SkiaImageDecoder.ShouldDecodeFullInsteadOfPreview(
                interactive with { MaxDecodedBytes = 2400L * 1600 * 4 - 1 }, codec, full, isFull: false));
            Assert.IsFalse(SkiaImageDecoder.ShouldDecodeFullInsteadOfPreview(
                new ImageDecodeRequest(path, Viewport), codec, full, isFull: false));
            await Task.CompletedTask;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Preload_Cache_Exposes_Full_Metadata_Of_Cached_Previews()
    {
        using var cache = new ImagePreloadCache();
        using var bitmap = new SKBitmap(40, 30);
        var metadata = new ImageMetadata("C:\\pictures\\a.jpg", 4000, 3000, 1, "test");
        cache.Store(metadata.Path, new DecodedImage(metadata, [new DecodedFrame(SKImage.FromBitmap(bitmap), TimeSpan.Zero)], false));

        Assert.IsTrue(cache.TryGetMetadata("C:\\Pictures\\A.jpg", out var cached));
        Assert.AreEqual(4000, cached!.Width);
        Assert.IsFalse(cache.TryGetMetadata("C:\\pictures\\missing.jpg", out _));
    }

    [TestMethod]
    public async Task Reduced_Tiles_Come_From_One_Cached_Level_With_Correct_Geometry()
    {
        var path = CreateImage(".jpg", MagickFormat.Jpeg, 5000, 3000);
        try
        {
            var source = new VipsLargeImageSource();
            var pyramid = new ImagePyramid(5000, 3000);
            var level = pyramid.Levels[2];
            Assert.IsLessThanOrEqualTo(VipsLargeImageSource.MaximumCachedLevelBytes, VipsLargeImageSource.EstimateLevelBytes(level));

            var first = await source.DecodeTileAsync(path, pyramid, new ImageTileKey(2, 0, 0), CancellationToken.None);
            var edge = await source.DecodeTileAsync(path, pyramid, new ImageTileKey(2, 2, 1), CancellationToken.None);

            Assert.AreEqual(512, first.Width);
            Assert.AreEqual(Math.Min(512, level.Height), first.Height);
            Assert.AreEqual(level.Width - 1024, edge.Width);
            Assert.AreEqual(level.Height - 512, edge.Height);
            Assert.AreEqual(5000 - 2 * 512 * 4, edge.SourceWidth);
            Assert.IsTrue(edge.HasExpectedPixelLength());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateImage(string extension, MagickFormat format, uint width, uint height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-latency-{Guid.NewGuid():N}{extension}");
        using var image = new MagickImage("gradient:red-blue", width, height);
        image.Write(path, format);
        return path;
    }
}
