using System.Reflection;
using Mangosteen.Caching;
using Mangosteen.Navigation;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ModelFilePreloaderTests
{
    [TestMethod]
    [DataRow(".stl")]
    [DataRow(".ply")]
    [DataRow(".obj")]
    [DataRow(".gltf")]
    [DataRow(".glb")]
    public async Task Models_Are_Read_Ahead_Without_Changing_Their_Bytes(string extension)
    {
        using var files = new Files();
        var path = files.Create(extension, 4096);
        var original = await File.ReadAllBytesAsync(path);
        var preloader = new ModelFilePreloader();
        Assert.AreEqual(4096L, await preloader.PrefetchAsync([path], 10000, CancellationToken.None));
        Assert.AreEqual(0L, await preloader.PrefetchAsync([path], 10000, CancellationToken.None));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
        File.Delete(path); // The worker must not retain file handles.
    }

    [TestMethod]
    public async Task Traffic_And_Number_Of_Neighbors_Are_Bounded()
    {
        using var files = new Files();
        var paths = new[] { files.Create(".stl", 4000), files.Create(".ply", 4000), files.Create(".glb", 4000) };
        var preloader = new ModelFilePreloader();
        Assert.AreEqual(1000L, await preloader.PrefetchAsync(paths, 1000, CancellationToken.None));
        preloader.Clear();
        Assert.AreEqual(8000L, await preloader.PrefetchAsync(paths, 100000, CancellationToken.None));
        Assert.AreEqual(4000L, await preloader.PrefetchAsync([paths[2]], 100000, CancellationToken.None));
        preloader.Clear();
        Assert.AreEqual(4000L, await preloader.PrefetchAsync([paths[0], paths[0]], 100000, CancellationToken.None));
    }

    [TestMethod]
    public async Task Large_Models_Read_Only_A_Bounded_Prefix()
    {
        using var files = new Files();
        var path = files.Create(".stl", 0);
        using (var stream = File.OpenWrite(path)) stream.SetLength(ModelFilePreloader.MaximumFileBytes + 1);
        var preloader = new ModelFilePreloader();
        Assert.AreEqual(ModelFilePreloader.MaximumFileBytes, await preloader.PrefetchAsync([path], long.MaxValue, CancellationToken.None));
    }

    [TestMethod]
    public async Task Changed_Files_And_Cleared_Caches_Are_Read_Again()
    {
        using var files = new Files();
        var path = files.Create(".stl", 4000);
        var preloader = new ModelFilePreloader();
        Assert.AreEqual(4000L, await preloader.PrefetchAsync([path], 10000, CancellationToken.None));
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        Assert.AreEqual(4000L, await preloader.PrefetchAsync([path], 10000, CancellationToken.None));
        preloader.Clear();
        Assert.AreEqual(4000L, await preloader.PrefetchAsync([path], 10000, CancellationToken.None));
    }

    [TestMethod]
    public async Task Cancellation_Does_Not_Mark_An_Unread_Model_As_Warm()
    {
        using var files = new Files();
        var path = files.Create(".glb", 4096);
        var preloader = new ModelFilePreloader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => preloader.PrefetchAsync([path], 10000, cancellation.Token));
        Assert.AreEqual(4096L, await preloader.PrefetchAsync([path], 10000, CancellationToken.None));
    }

    [TestMethod]
    public async Task Missing_Files_Images_And_Zero_Budgets_Are_Skipped()
    {
        using var files = new Files();
        var path = files.Create(".png", 4096);
        var model = files.Create(".stl", 4096);
        var preloader = new ModelFilePreloader();
        Assert.AreEqual(0L, await preloader.PrefetchAsync([path, Path.Combine(files.Directory, "missing.stl")], 10000, CancellationToken.None));
        Assert.AreEqual(0L, await preloader.PrefetchAsync([model], 0, CancellationToken.None));
        Assert.AreEqual(4096L, await preloader.PrefetchAsync([model], 10000, CancellationToken.None));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public Task Viewer_Schedules_Model_Prefetch_And_Respects_Disable_And_Cancel(bool enabled, bool cancel) => StaTest.RunAsync(async () =>
    {
        using var files = new Files();
        var first = files.Create(".ply", 4096);
        var next = files.Create(".stl", 4096);
        var window = new MainWindow(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = enabled });
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var navigator = (ImageNavigator)type.GetField("_navigator", flags)!.GetValue(window)!;
        navigator.Apply(new ImageFolderSnapshot([first, next], 0));
        type.GetField("_preloadAggressiveness", flags)!.SetValue(window, PreloadAggressiveness.Aggressive);
        type.GetMethod("ScheduleSmartPreloads", flags)!.Invoke(window, [false]);
        if (cancel) type.GetMethod("CancelPreloadWorker", flags)!.Invoke(window, [true]);
        try { await (Task)type.GetField("_preloadWorkerTask", flags)!.GetValue(window)!; }
        catch (OperationCanceledException) when (cancel) { }
        var preloader = (ModelFilePreloader)type.GetField("_modelFilePreloader", flags)!.GetValue(window)!;
        Assert.AreEqual(enabled && !cancel ? 0L : 4096L, await preloader.PrefetchAsync([next], 10000, CancellationToken.None));
        type.GetMethod("ClearPreloadCaches", flags)!.Invoke(window, null);
        Assert.AreEqual(4096L, await preloader.PrefetchAsync([next], 10000, CancellationToken.None));
    });

    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"mangosteen-model-prefetch-{Guid.NewGuid():N}");
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public string Create(string extension, int size)
        {
            var path = Path.Combine(Directory, Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, new byte[size]);
            return path;
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
