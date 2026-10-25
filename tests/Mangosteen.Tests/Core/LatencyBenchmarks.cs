using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using ImageMagick;
using Mangosteen.Core;
using Mangosteen.Decoding;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

/// <summary>
/// Opt-in latency measurements (set MANGOSTEEN_BENCHMARK=1). They print medians rather
/// than assert, so results from different machines are not mistaken for regressions.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Benchmark")]
public sealed class LatencyBenchmarks
{
    private static readonly string FixtureDirectory = Path.Combine(Path.GetTempPath(), "mangosteen-benchmark-fixtures");
    private static readonly PixelSize Viewport = new(1920, 1080);

    [TestMethod]
    public async Task Image_Preview_And_Full_Decode_Latency()
    {
        RequireBenchmarks();
        var fixtures = EnsureImageFixtures();
        using var registry = DecoderRegistry.CreateDefault();

        foreach (var path in fixtures)
        {
            // Warm decoders and the OS file cache so the medians compare decode work.
            using (await registry.DecodeAsync(new ImageDecodeRequest(path, Viewport), CancellationToken.None)) { }
            var preview = await Measure(5, () => registry.DecodeAsync(new ImageDecodeRequest(path, Viewport), CancellationToken.None));
            var full = await Measure(3, () => registry.DecodeAsync(new ImageDecodeRequest(path, null, FullResolution: true), CancellationToken.None));
            var decoder = registry.GetDecoderPlan(path, new ImageDecodeRequest(path, Viewport)).First(d => d.CanDecode(path)).Name;
            // What the viewer does on open: interactive preview, then a full decode unless the preview already is one.
            var open = await Measure(3, async () =>
            {
                var first = await registry.DecodeAsync(new ImageDecodeRequest(path, Viewport) { PreferFullResolutionWhenUnscalable = true }, CancellationToken.None);
                if (first.IsFullResolution) return first;
                first.Dispose();
                return await registry.DecodeAsync(new ImageDecodeRequest(path, null, FullResolution: true), CancellationToken.None);
            });
            Console.WriteLine($"{Path.GetFileName(path),-24} preview {preview,7:F0} ms   full {full,7:F0} ms   open-to-full {open,7:F0} ms   first decoder {decoder}");
        }
    }

    /// <summary>
    /// How long a cancelled full decode keeps the (single) full-decode gate, and how long the
    /// next image's full resolution then takes, as the viewer sees it after navigating away.
    /// </summary>
    [TestMethod]
    public async Task Cancelled_Full_Decode_Gate_Release_Latency()
    {
        RequireBenchmarks();
        var fixtures = EnsureImageFixtures()
            .Where(path => path.EndsWith(".png", StringComparison.Ordinal) || path.EndsWith(".jpg", StringComparison.Ordinal))
            .Append(EnsureLargeTiffFixture())
            .ToArray();
        using var registry = DecoderRegistry.CreateDefault();

        foreach (var path in fixtures)
        {
            var fullRequest = new ImageDecodeRequest(path, null, FullResolution: true);
            using (await registry.DecodeAsync(fullRequest, CancellationToken.None)) { }
            var full = await Measure(3, () => registry.DecodeAsync(fullRequest, CancellationToken.None));
            var decoder = registry.GetDecoderPlan(path, fullRequest).First(d => d.CanDecode(path)).Name;

            foreach (var fraction in new[] { 0.25, 0.5 })
            {
                var delay = TimeSpan.FromMilliseconds(full * fraction);
                var release = new List<double>();
                var navigate = new List<double>();
                for (var run = 0; run < 3; run++)
                {
                    using var gate = new SemaphoreSlim(1, 1);
                    using var stale = new CancellationTokenSource();
                    var staleDecode = DecodeUnderGate(registry, gate, fullRequest, stale.Token);
                    await Task.Delay(delay);

                    var clock = Stopwatch.StartNew();
                    stale.Cancel();
                    var next = DecodeUnderGate(registry, gate, fullRequest, CancellationToken.None);
                    var staleResult = await staleDecode;
                    release.Add(clock.Elapsed.TotalMilliseconds);
                    staleResult?.Dispose();
                    using (var nextResult = await next)
                    {
                        navigate.Add(clock.Elapsed.TotalMilliseconds);
                        // A cancelled decode must not leave state that makes the next one fall back.
                        Assert.AreEqual(decoder, nextResult!.Metadata.DecoderName);
                    }
                }

                release.Sort();
                navigate.Sort();
                Console.WriteLine(
                    $"{Path.GetFileName(path),-24} {decoder,-9} full {full,6:F0} ms  cancel at {fraction:P0}: " +
                    $"gate released after {release[1],6:F0} ms   navigation to next full {navigate[1],6:F0} ms");
            }
        }
    }

    private static async Task<DecodedImage?> DecodeUnderGate(
        DecoderRegistry registry,
        SemaphoreSlim gate,
        ImageDecodeRequest request,
        CancellationToken token)
    {
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            return await registry.DecodeAsync(request, token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string EnsureLargeTiffFixture()
    {
        Directory.CreateDirectory(FixtureDirectory);
        var path = Path.Combine(FixtureDirectory, "scan-100mp.tif");
        if (!File.Exists(path))
        {
            using var image = new MagickImage("plasma:", 12000, 8400);
            image.Depth = 16;
            image.Write(path, MagickFormat.Tiff);
        }

        return path;
    }

    [TestMethod]
    public async Task Fit_Paint_With_Mipmaps_Latency()
    {
        RequireBenchmarks();
        var jpeg = EnsureImageFixtures().First(path => path.EndsWith(".jpg", StringComparison.Ordinal));
        using var registry = DecoderRegistry.CreateDefault();
        var info = new SkiaSharp.SKImageInfo(Viewport.Width, Viewport.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using var surface = SkiaSharp.SKSurface.Create(info);
        var sampling = new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear);
        var limit = SkiaSharp.SKGraphics.GetResourceCacheTotalByteLimit();

        foreach (var cacheBytes in new[] { limit, 512L * 1024 * 1024 })
        {
            SkiaSharp.SKGraphics.SetResourceCacheTotalByteLimit(cacheBytes);
            using var full = await registry.DecodeAsync(new ImageDecodeRequest(jpeg, null, FullResolution: true), CancellationToken.None);
            var image = full.Frames[0].Image;
            var destination = new SkiaSharp.SKRect(0, 0, Viewport.Width, Viewport.Width * image.Height / (float)image.Width);
            var draws = new List<double>();
            for (var i = 0; i < 4; i++)
            {
                var clock = Stopwatch.StartNew();
                surface.Canvas.Clear();
                surface.Canvas.DrawImage(image, destination, sampling);
                surface.Flush();
                draws.Add(clock.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"24 MP fit paint, Skia cache {cacheBytes / (1024 * 1024)} MB: {string.Join(" / ", draws.Select(d => d.ToString("F0")))} ms");
        }

        SkiaSharp.SKGraphics.SetResourceCacheTotalByteLimit(limit);

        using (var full = await registry.DecodeAsync(new ImageDecodeRequest(jpeg, null, FullResolution: true), CancellationToken.None))
        {
            var image = full.Frames[0].Image;
            var destination = new SkiaSharp.SKRect(0, 0, Viewport.Width, Viewport.Width * image.Height / (float)image.Width);
            double Draw(SkiaSharp.SKImage source, SkiaSharp.SKSamplingOptions options)
            {
                surface.Canvas.DrawImage(source, destination, options);
                var clock = Stopwatch.StartNew();
                for (var i = 0; i < 5; i++)
                {
                    surface.Canvas.Clear();
                    surface.Canvas.DrawImage(source, destination, options);
                    surface.Flush();
                }
                return clock.Elapsed.TotalMilliseconds / 5;
            }

            var noMips = Draw(image, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.None));
            var nearestMip = Draw(image, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Nearest));
            Console.WriteLine($"24 MP fit paint: nearest mip level {nearestMip:F1} ms");
            var build = Stopwatch.StartNew();
            // A display copy at twice the destination width, built once with Mitchell filtering.
            var displayInfo = new SkiaSharp.SKImageInfo((int)destination.Width * 2, (int)destination.Height * 2, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
            using var displayBitmap = new SkiaSharp.SKBitmap(displayInfo);
            image.ScalePixels(displayBitmap.PeekPixels(), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
            displayBitmap.SetImmutable();
            using var display = SkiaSharp.SKImage.FromBitmap(displayBitmap);
            var buildMs = build.Elapsed.TotalMilliseconds;
            var fromDisplay = Draw(display, sampling);
            Console.WriteLine($"24 MP fit paint: full+mips {Draw(image, sampling):F1} | full no mips {noMips:F1} | 2x display copy+mips {fromDisplay:F1} ms (copy built once in {buildMs:F0} ms)");

            var tiff = EnsureImageFixtures().First(path => path.EndsWith(".tif", StringComparison.Ordinal));
            using var vips = await registry.DecodeAsync(new ImageDecodeRequest(tiff, null, FullResolution: true), CancellationToken.None);
            var vipsImage = vips.Frames[0].Image;
            Console.WriteLine(
                $"24 MP libvips image ({vipsImage.ColorType}, {vipsImage.AlphaType}) fit paint: " +
                $"nearest mip {Draw(vipsImage, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Nearest)):F1} | " +
                $"trilinear {Draw(vipsImage, sampling):F1} ms");
        }
    }

    [TestMethod]
    public async Task Gpu_Tile_Decode_Latency()
    {
        RequireBenchmarks();
        Directory.CreateDirectory(FixtureDirectory);
        var path = Path.Combine(FixtureDirectory, "pano-100mp.jpg");
        if (!File.Exists(path))
        {
            using var image = new MagickImage("plasma:", 12000, 8400);
            image.Quality = 90;
            image.Write(path, MagickFormat.Jpeg);
        }

        var metadata = await new VipsLargeImageSource().LoadMetadataAsync(path, CancellationToken.None);
        var pyramid = new ImagePyramid(metadata.Width, metadata.Height);
        var fitLevel = pyramid.ChooseLevel(1920.0 / metadata.Width);
        foreach (var level in new[] { fitLevel.Index, fitLevel.Index - 1, 0 })
        {
            NetVips.Cache.Max = 0;
            NetVips.Cache.Max = 100;
            var source = new VipsLargeImageSource();
            var across = (pyramid.Levels[level].Width + pyramid.TileSize - 1) / pyramid.TileSize;
            var keys = Enumerable.Range(0, 4).Select(i => new ImageTileKey(level, i % across, i / across)).ToArray();
            var clock = Stopwatch.StartNew();
            await source.DecodeTileAsync(path, pyramid, keys[0], CancellationToken.None);
            var first = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            foreach (var key in keys.Skip(1))
            {
                await source.DecodeTileAsync(path, pyramid, key, CancellationToken.None);
            }
            Console.WriteLine($"100 MP JPEG level {level} (1/{pyramid.Levels[level].Downsample}): first tile {first:F0} ms, next tiles {clock.Elapsed.TotalMilliseconds / 3:F0} ms each");
        }
    }

    [TestMethod]
    public Task Model_Engine_Start_And_Load_Latency() => StaTest.RunAsync(async () =>
    {
        RequireBenchmarks();
        var runtime = Environment.GetEnvironmentVariable("MANGOSTEEN_TEST_F3D_DIRECTORY");
        if (string.IsNullOrEmpty(runtime)) Assert.Inconclusive("Set MANGOSTEEN_TEST_F3D_DIRECTORY.");
        Directory.CreateDirectory(FixtureDirectory);
        var cube = Path.Combine(FixtureDirectory, "cube.obj");
        File.WriteAllText(cube, "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0 0 1\nv 1 0 1\nv 1 1 1\nv 0 1 1\nf 1 2 3 4\nf 5 6 7 8\nf 1 2 6 5\nf 2 3 7 6\nf 3 4 8 7\nf 4 1 5 8\n");

        using var surface = new HwndSource(new HwndSourceParameters("Mangosteen benchmark") { Width = 640, Height = 480, WindowStyle = 0 });
        using var host = new NativeGlHost { Width = 640, Height = 480 };
        try { surface.RootVisual = host; }
        catch (Win32Exception) { Assert.Inconclusive("A WGL context is unavailable on this machine."); }
        host.Measure(new Size(640, 480));
        host.Arrange(new Rect(0, 0, 640, 480));
        host.UpdateLayout();
        using var renderer = new F3dModelRenderer(host, runtime!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var clock = Stopwatch.StartNew();
        await renderer.OpenAsync(cube, true, timeout.Token);
        Console.WriteLine($"first model (engine start + plugins) {clock.Elapsed.TotalMilliseconds,7:F0} ms");
        var reopen = await Measure(5, async () => { await renderer.OpenAsync(cube, true, timeout.Token); return (IDisposable?)null; });
        Console.WriteLine($"next small model                     {reopen,7:F0} ms");

        var orbit = Stopwatch.StartNew();
        for (var i = 0; i < 60; i++) renderer.Orbit(3, 0);
        Console.WriteLine($"orbit frame                          {orbit.Elapsed.TotalMilliseconds / 60,7:F1} ms");
        ReportFrameBreakdown(renderer, "small model");

        var large = Environment.GetEnvironmentVariable("MANGOSTEEN_TEST_LARGE_MODEL");
        if (!string.IsNullOrEmpty(large))
        {
            clock.Restart();
            await renderer.OpenAsync(large, true, timeout.Token);
            Console.WriteLine($"large model {Path.GetFileName(large)} {clock.Elapsed.TotalMilliseconds,7:F0} ms");
            orbit.Restart();
            for (var i = 0; i < 20; i++) renderer.Orbit(3, 0);
            Console.WriteLine($"large model orbit frame              {orbit.Elapsed.TotalMilliseconds / 20,7:F1} ms");
            ReportFrameBreakdown(renderer, "large model");
        }
        renderer.Dispose();
        surface.RootVisual = null;
    }, TimeSpan.FromMinutes(6));

    private static void ReportFrameBreakdown(F3dModelRenderer renderer, string label)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var api = (F3dNativeApi)typeof(F3dModelRenderer).GetField("_api", flags)!.GetValue(renderer)!;
        var engine = (nint)typeof(F3dModelRenderer).GetField("_engine", flags)!.GetValue(renderer)!;
        var options = api.GetOptions(engine);

        double Frames(Action frame, int count = 20)
        {
            frame();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < count; i++) frame();
            return clock.Elapsed.TotalMilliseconds / count;
        }

        var still = Frames(() => renderer.ApplyTheme(true));
        var orbit = Frames(() => renderer.Orbit(3, 0));
        api.SetBool(options, "render.effect.ambient_occlusion", false);
        var noAo = Frames(() => renderer.Orbit(3, 0));
        api.SetBool(options, "render.effect.antialiasing.enable", false);
        var noAoAa = Frames(() => renderer.Orbit(3, 0));
        api.SetBool(options, "render.effect.ambient_occlusion", true);
        var noAa = Frames(() => renderer.Orbit(3, 0));
        api.SetBool(options, "render.effect.antialiasing.enable", true);
        Console.WriteLine($"{label}: still {still:F1} | orbit {orbit:F1} | no AO {noAo:F1} | no AA {noAa:F1} | neither {noAoAa:F1} ms/frame");
    }

    private static async Task<double> Measure(int runs, Func<Task<IDisposable?>> action)
    {
        var samples = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var clock = Stopwatch.StartNew();
            using (await action()) { }
            samples.Add(clock.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static async Task<double> Measure(int runs, Func<Task<DecodedImage>> action)
    {
        return await Measure(runs, async () => (IDisposable?)await action());
    }

    private static string[] EnsureImageFixtures()
    {
        Directory.CreateDirectory(FixtureDirectory);
        var specs = new (string Name, MagickFormat Format, int Width, int Height, int Depth)[]
        {
            ("photo-24mp.jpg", MagickFormat.Jpeg, 6000, 4000, 8),
            ("photo-24mp.png", MagickFormat.Png, 6000, 4000, 8),
            ("photo-24mp.webp", MagickFormat.WebP, 6000, 4000, 8),
            ("scan-24mp-16bit.tif", MagickFormat.Tiff, 6000, 4000, 16),
            ("photo-12mp.heic", MagickFormat.Heic, 4000, 3000, 8),
            ("photo-12mp.avif", MagickFormat.Avif, 4000, 3000, 8)
        };

        var paths = new List<string>();
        foreach (var spec in specs)
        {
            var path = Path.Combine(FixtureDirectory, spec.Name);
            paths.Add(path);
            if (File.Exists(path)) continue;
            // Plasma resembles photographic content more than noise or flat colour.
            using var image = new MagickImage("plasma:", (uint)spec.Width, (uint)spec.Height);
            image.Depth = (uint)spec.Depth;
            image.Quality = 90;
            try
            {
                image.Write(path, spec.Format);
            }
            catch (MagickException)
            {
                paths.Remove(path);
            }
        }

        return [.. paths];
    }

    private static void RequireBenchmarks()
    {
        if (Environment.GetEnvironmentVariable("MANGOSTEEN_BENCHMARK") != "1")
            Assert.Inconclusive("Set MANGOSTEEN_BENCHMARK=1 to run latency measurements.");
    }
}
