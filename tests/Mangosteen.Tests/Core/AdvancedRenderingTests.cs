using Mangosteen.Decoding;
using Mangosteen.Rendering.Advanced;
using NetVips;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class AdvancedRenderingTests
{
    [TestMethod]
    public void LargeImageClassifier_RoutesLargeDecodedImages()
    {
        var metadata = new ImageMetadata("large.tif", 12_000, 8_000, 1, "test");

        var result = LargeImageClassifier.Classify(metadata, metadata.Path);

        Assert.IsTrue(result.UseAdvancedRenderer);
        Assert.AreEqual(384_000_000L, result.EstimatedDecodedBytes);
    }

    [TestMethod]
    public void LargeImageClassifier_LeavesOrdinaryImagesOnDefaultRenderer()
    {
        var metadata = new ImageMetadata("ordinary.jpg", 2_000, 1_500, 1, "test");

        var result = LargeImageClassifier.Classify(metadata, metadata.Path);

        Assert.IsFalse(result.UseAdvancedRenderer);
    }

    [TestMethod]
    public void LargeImageClassifier_AlwaysRoutesPsbDocuments()
    {
        var metadata = new ImageMetadata("document.psb", 64, 64, 1, "test");

        Assert.IsTrue(LargeImageClassifier.Classify(metadata, metadata.Path).UseAdvancedRenderer);
    }

    [TestMethod]
    public void ImagePyramid_ChoosesLevelClosestToDisplayScale()
    {
        var pyramid = new ImagePyramid(20_000, 10_000, tileSize: 512);

        Assert.AreEqual(0, pyramid.ChooseLevel(1.0).Index);
        Assert.AreEqual(2, pyramid.ChooseLevel(0.25).Index);
        Assert.AreEqual(4, pyramid.ChooseLevel(0.0625).Index);
    }

    [TestMethod]
    public void ImagePyramid_ReturnsVisibleTilesWithBoundedMargin()
    {
        var pyramid = new ImagePyramid(4_096, 4_096, tileSize: 512);
        var level = pyramid.Levels[0];

        var tiles = pyramid.GetTilesForSourceRect(level, 0, 0, 512, 512, marginTiles: 1).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                new ImageTileKey(0, 0, 0),
                new ImageTileKey(0, 1, 0),
                new ImageTileKey(0, 0, 1),
                new ImageTileKey(0, 1, 1)
            },
            tiles);
    }

    [TestMethod]
    public void OptionalComponentCatalog_DetectsManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var componentDirectory = Path.Combine(directory.Path, "components", "gpu-large-images");
        Directory.CreateDirectory(componentDirectory);
        File.WriteAllText(
            Path.Combine(componentDirectory, "component.json"),
            """{"id":"gpu-large-images","version":1,"optional":true}""");
        var catalog = new OptionalComponentCatalog(directory.Path);

        Assert.IsTrue(catalog.IsInstalled(OptionalComponentKind.GpuLargeImages));
        Assert.IsFalse(catalog.IsInstalled(OptionalComponentKind.ModelViewer));
    }

    [TestMethod]
    public void OptionalComponentCatalog_RejectsMalformedOrIncompatibleManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var componentDirectory = Path.Combine(directory.Path, "components", "gpu-large-images");
        Directory.CreateDirectory(componentDirectory);
        var manifestPath = Path.Combine(componentDirectory, "component.json");
        var catalog = new OptionalComponentCatalog(directory.Path);

        File.WriteAllText(manifestPath, "not-json");
        Assert.IsFalse(catalog.IsInstalled(OptionalComponentKind.GpuLargeImages));

        File.WriteAllText(manifestPath, """{"id":"gpu-large-images","version":2}""");
        Assert.IsFalse(catalog.IsInstalled(OptionalComponentKind.GpuLargeImages));
    }

    [TestMethod]
    public void OptionalComponentCatalog_RequiresPinnedModelRuntime()
    {
        using var directory = TemporaryDirectory.Create();
        var componentDirectory = Path.Combine(directory.Path, "components", "model-viewer");
        Directory.CreateDirectory(componentDirectory);
        File.WriteAllText(
            Path.Combine(componentDirectory, "component.json"),
            """{"id":"model-viewer","engine":"F3D","engineVersion":"3.5.0","optional":true}""");
        var catalog = new OptionalComponentCatalog(directory.Path);

        Assert.IsFalse(catalog.IsInstalled(OptionalComponentKind.ModelViewer));

        var runtimeDirectory = Path.Combine(componentDirectory, "bin");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "f3d_c_api.dll"), [1]);
        Assert.IsTrue(catalog.IsInstalled(OptionalComponentKind.ModelViewer));
    }

    [TestMethod]
    public void ModelFileExtensions_RecognizesInitialSupportedSet()
    {
        foreach (var extension in new[] { ".stl", ".ply", ".obj", ".gltf", ".glb" })
        {
            Assert.IsTrue(ModelFileExtensions.IsSupported("model" + extension));
        }

        Assert.IsFalse(ModelFileExtensions.IsSupported("model.fbx"));
    }

    [TestMethod]
    public async Task PersistentTileCache_RoundTripsLosslessPixels()
    {
        using var directory = TemporaryDirectory.Create();
        var sourcePath = Path.Combine(directory.Path, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var cache = new PersistentTileCache(Path.Combine(directory.Path, "cache"), maximumBytes: 1024 * 1024);
        var sourceKey = cache.CreateSourceKey(sourcePath, "decoder-v1");
        var pixels = Enumerable.Range(0, 16 * 16 * 4).Select(value => (byte)(value % 251)).ToArray();
        var tile = new ImageTileData(new ImageTileKey(2, 3, 4), 16, 16, 64, 64, ImageTilePixelFormat.Rgba8, pixels);

        await cache.WriteAsync(sourceKey, tile, CancellationToken.None);
        var restored = await cache.TryReadAsync(sourceKey, tile.Key, CancellationToken.None);

        Assert.IsNotNull(restored);
        Assert.AreEqual(tile.Width, restored.Width);
        Assert.AreEqual(tile.SourceWidth, restored.SourceWidth);
        CollectionAssert.AreEqual(pixels, restored.Pixels);
    }

    [TestMethod]
    public async Task PersistentTileCache_RejectsPayloadLengthThatDoesNotMatchDimensions()
    {
        using var directory = TemporaryDirectory.Create();
        var sourcePath = Path.Combine(directory.Path, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var cache = new PersistentTileCache(Path.Combine(directory.Path, "cache"), maximumBytes: 1024 * 1024);
        var sourceKey = cache.CreateSourceKey(sourcePath, "decoder-v1");
        var key = new ImageTileKey(0, 0, 0);
        var tile = new ImageTileData(key, 4, 4, 4, 4, ImageTilePixelFormat.Rgba8, new byte[4 * 4 * 4]);
        await cache.WriteAsync(sourceKey, tile, CancellationToken.None);

        var tilePath = cache.GetTilePath(sourceKey, key);
        await using (var stream = new FileStream(tilePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = 41;
            await stream.WriteAsync(BitConverter.GetBytes(4));
        }

        Assert.IsNull(await cache.TryReadAsync(sourceKey, key, CancellationToken.None));
    }

    [TestMethod]
    public async Task VipsLargeImageSource_PreservesSixteenBitTileData()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "sixteen-bit.tiff");
        using (var image = NetVips.Image.NewFromMemory(
            new ushort[]
            {
                0, 8_000, 16_000,
                24_000, 32_000, 40_000,
                48_000, 56_000, 64_000,
                4_000, 20_000, 60_000
            },
            2,
            2,
            3,
            Enums.BandFormat.Ushort))
        {
            image.Tiffsave(path, bitdepth: 16);
        }
        var source = new VipsLargeImageSource();

        var tile = await source.DecodeTileAsync(
            path,
            new ImagePyramid(2, 2, tileSize: 128),
            new ImageTileKey(0, 0, 0),
            CancellationToken.None);

        Assert.AreEqual(ImageTilePixelFormat.Rgba16, tile.PixelFormat);
        Assert.HasCount(2 * 2 * 4 * 2, tile.Pixels);
        Assert.IsTrue(tile.HasExpectedPixelLength());
    }

    [TestMethod]
    public void GpuLargeImageRenderer_UnpremultipliesPreviewPixels()
    {
        var pixels = new byte[]
        {
            50, 25, 0, 128,
            10, 20, 30, 0,
            12, 34, 56, 255
        };

        GpuLargeImageRenderer.UnpremultiplyRgba(pixels, width: 3, height: 1, rowBytes: 12);

        CollectionAssert.AreEqual(
            new byte[]
            {
                100, 50, 0, 128,
                0, 0, 0, 0,
                12, 34, 56, 255
            },
            pixels);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mangosteen.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
