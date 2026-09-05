using System.Reflection;
using System.Windows.Interop;
using Mangosteen.Decoding;
using Mangosteen.Rendering;
using Mangosteen.Rendering.Advanced;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class AdvancedRenderingLifecycleTests
{
    [TestMethod]
    public Task Tile_Queue_Is_Bounded_And_Cancels_Requests_After_Panning() => StaTest.RunAsync(async () =>
    {
        var path = Path.GetTempFileName();
        try
        {
            using var host = new NativeGlHost();
            typeof(NativeGlHost).GetProperty(nameof(NativeGlHost.PixelWidth))!.SetValue(host, 4096);
            typeof(NativeGlHost).GetProperty(nameof(NativeGlHost.PixelHeight))!.SetValue(host, 4096);
            var source = new StubSource
            {
                Metadata = (_, _) => Task.FromResult(new LargeImageMetadata(10000, 10000, 4, 8, false)),
                Decode = async (_, _, _, token) => { await Task.Delay(Timeout.Infinite, token); return Tile; }
            };
            using var renderer = new GpuLargeImageRenderer(host, source, new PersistentTileCache(path));
            using var preview = CreatePreview();
            var view = new ViewerState();
            view.SetViewport(new Mangosteen.Core.PixelSize(4096, 4096));
            view.SetImage(10000, 10000, true);
            view.SetActualPixels();
            await renderer.OpenAsync(path, new ImageMetadata(path, 10000, 10000, 1, "test"), preview, view, CancellationToken.None);
            var queue = typeof(GpuLargeImageRenderer).GetMethod("QueueVisibleTiles", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var pending = (Dictionary<ImageTileKey, CancellationTokenSource>)typeof(GpuLargeImageRenderer)
                .GetField("_pendingTiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!;
            queue.Invoke(renderer, null);
            Assert.HasCount(GpuLargeImageRenderer.MaximumPendingTiles, pending);
            var oldRequests = pending.ToDictionary(pair => pair.Key, pair => pair.Value.Token);
            view.PanBy(new SKPoint(10000, 10000));
            queue.Invoke(renderer, null);
            Assert.IsLessThanOrEqualTo(GpuLargeImageRenderer.MaximumPendingTiles, pending.Count);
            var obsolete = oldRequests.Where(pair => !pending.ContainsKey(pair.Key)).ToArray();
            Assert.IsNotEmpty(obsolete);
            Assert.IsTrue(obsolete.All(pair => pair.Value.IsCancellationRequested));
            renderer.CloseCurrent();
            Assert.IsEmpty(pending);
            await Task.Yield();
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public Task Native_Surface_Has_Pixel_Dimensions_Before_The_First_Resize_Message() => StaTest.RunAsync(() =>
    {
        using var source = new HwndSource(new HwndSourceParameters("Mangosteen surface test") { Width = 32, Height = 32, WindowStyle = 0 });
        using var host = new NativeGlHost { Width = 16, Height = 16 };
        source.RootVisual = host;
        host.Measure(new System.Windows.Size(16, 16));
        host.Arrange(new System.Windows.Rect(0, 0, 16, 16));
        host.UpdateLayout();
        Assert.IsTrue(host.IsSurfaceReady);
        Assert.IsGreaterThan(0, host.PixelWidth);
        Assert.IsGreaterThan(0, host.PixelHeight);
        var called = false;
        Assert.IsTrue(host.Render(() => called = true));
        Assert.IsTrue(called);
        source.RootVisual = null;
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Older_Gpu_Activation_Cannot_Replace_A_Newer_Image() => StaTest.RunAsync(async () =>
    {
        var path = Path.GetTempFileName();
        try
        {
            using var host = new NativeGlHost();
            var pending = new TaskCompletionSource<LargeImageMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new StubSource { Metadata = (file, _) => file == "old.tif" ? pending.Task : Task.FromResult(Metadata) };
            using var renderer = new GpuLargeImageRenderer(host, source);
            using var preview = CreatePreview();
            var old = renderer.OpenAsync("old.tif", ImageInfo("old.tif"), preview, new ViewerState(), CancellationToken.None);
            preview.Dispose();
            using var nextPreview = CreatePreview();
            await renderer.OpenAsync(path, ImageInfo(path), nextPreview, new ViewerState(), CancellationToken.None);
            pending.SetResult(Metadata);
            await Assert.ThrowsAsync<OperationCanceledException>(() => old);
            Assert.AreEqual(path, renderer.CurrentPath);
        }
        finally { File.Delete(path); }
    });

    [TestMethod]
    public Task Gpu_Activation_Honors_Cancellation_Even_If_The_Decoder_Does_Not() => StaTest.RunAsync(async () =>
    {
        using var host = new NativeGlHost();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<LargeImageMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var renderer = new GpuLargeImageRenderer(host, new StubSource { Metadata = (_, _) => pending.Task });
        using var preview = CreatePreview();
        var loading = renderer.OpenAsync("cancelled.tif", ImageInfo("cancelled.tif"), preview, new ViewerState(), cancellation.Token);
        cancellation.Cancel();
        pending.SetResult(Metadata);
        await Assert.ThrowsAsync<OperationCanceledException>(() => loading);
        Assert.IsNull(renderer.CurrentPath);
    });

    [TestMethod]
    public Task Disposing_Gpu_Renderer_Rejects_Pending_Activation() => StaTest.RunAsync(async () =>
    {
        using var host = new NativeGlHost();
        var pending = new TaskCompletionSource<LargeImageMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var renderer = new GpuLargeImageRenderer(host, new StubSource { Metadata = (_, _) => pending.Task });
        using var preview = CreatePreview();
        var loading = renderer.OpenAsync("disposed.tif", ImageInfo("disposed.tif"), preview, new ViewerState(), CancellationToken.None);
        renderer.Dispose();
        pending.SetResult(Metadata);
        await Assert.ThrowsAsync<OperationCanceledException>(() => loading);
        Assert.IsNull(renderer.CurrentPath);
    });

    [TestMethod]
    public Task Tile_Pixels_Are_Returned_When_The_Disk_Cache_Is_Unwritable() => StaTest.RunAsync(async () =>
    {
        var root = Path.GetTempFileName();
        try
        {
            using var host = new NativeGlHost();
            using var renderer = new GpuLargeImageRenderer(host, new StubSource(), new PersistentTileCache(root));
            var pixels = await renderer.LoadTilePixelsAsync("source.tif", new string('a', 64), new ImagePyramid(2, 2), new ImageTileKey(0, 0, 0), CancellationToken.None);
            Assert.IsTrue(pixels.HasExpectedPixelLength());
            File.Delete(root);
            // Once storage has failed, subsequent tiles do not keep retrying it.
            await renderer.LoadTilePixelsAsync("source.tif", new string('a', 64), new ImagePyramid(2, 2), new ImageTileKey(0, 0, 0), CancellationToken.None);
            Assert.IsFalse(Directory.Exists(root));
        }
        finally { if (File.Exists(root)) File.Delete(root); }
    });

    [TestMethod]
    public Task Cancelled_Tile_Is_Not_Persisted() => StaTest.RunAsync(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var host = new NativeGlHost();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ImageTileData>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var renderer = new GpuLargeImageRenderer(host, new StubSource { Decode = (_, _, _, _) => pending.Task }, new PersistentTileCache(root));
        var loading = renderer.LoadTilePixelsAsync("source.tif", new string('a', 64), new ImagePyramid(2, 2), new ImageTileKey(0, 0, 0), cancellation.Token);
        cancellation.Cancel();
        pending.SetResult(Tile);
        await Assert.ThrowsAsync<OperationCanceledException>(() => loading);
        Assert.IsFalse(Directory.Exists(root));
    });

    [TestMethod]
    public Task Ui_Render_Does_Not_Wait_For_A_Background_Context_Owner() => StaTest.RunAsync(async () =>
    {
        using var host = new NativeGlHost();
        var gate = typeof(NativeGlHost).GetField("_contextGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var worker = Task.Run(() => { lock (gate) { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); } });
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.IsFalse(host.TryRender(() => Assert.Fail("Busy render context must not be used.")));
            Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally { release.Set(); await worker; }
    });

    [TestMethod]
    public Task Model_Ui_Commands_And_Close_Do_Not_Wait_For_An_Import() => StaTest.RunAsync(() =>
    {
        using var host = new NativeGlHost();
        using var renderer = new F3dModelRenderer(host, "unused");
        var gate = (SemaphoreSlim)typeof(F3dModelRenderer).GetField("_operationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        typeof(F3dModelRenderer).GetField("_hasOpenScene", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, true);
        gate.Wait();
        try
        {
            renderer.ApplyTheme(false);
            renderer.Orbit(1, 1);
            renderer.CloseCurrent();
            Assert.IsFalse(renderer.IsOpen);
            Assert.IsFalse(renderer.TryDisposeWithoutBlocking());
        }
        finally { gate.Release(); }
        return Task.CompletedTask;
    });

    private static readonly LargeImageMetadata Metadata = new(2, 2, 4, 8, false);
    private static ImageMetadata ImageInfo(string path) => new(path, 2, 2, 1, "test");
    private static ImageTileData Tile => new(new ImageTileKey(0, 0, 0), 2, 2, 2, 2, ImageTilePixelFormat.Rgba8, new byte[16]);
    private static SKImage CreatePreview()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Red);
        return SKImage.FromBitmap(bitmap);
    }

    private sealed class StubSource : ILargeImageSource
    {
        public Func<string, CancellationToken, Task<LargeImageMetadata>> Metadata { get; init; } = (_, _) => Task.FromResult(AdvancedRenderingLifecycleTests.Metadata);
        public Func<string, ImagePyramid, ImageTileKey, CancellationToken, Task<ImageTileData>> Decode { get; init; } = (_, _, _, _) => Task.FromResult(Tile);
        public Task<LargeImageMetadata> LoadMetadataAsync(string path, CancellationToken token) => Metadata(path, token);
        public Task<ImageTileData> DecodeTileAsync(string path, ImagePyramid pyramid, ImageTileKey key, CancellationToken token) => Decode(path, pyramid, key, token);
    }
}
