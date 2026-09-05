using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class F3dViewportTests
{
    [TestMethod]
    [TestCategory("NativeOpenGL")]
    public Task Model_Remains_Visible_After_Hidden_Load_Resize_Orbit_And_Reset() => StaTest.RunAsync(async () =>
    {
        var runtime = Environment.GetEnvironmentVariable("MANGOSTEEN_TEST_F3D_DIRECTORY");
        if (string.IsNullOrEmpty(runtime))
            Assert.Inconclusive("Set MANGOSTEEN_TEST_F3D_DIRECTORY to a staged F3D component to run the native pixel regression.");

        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-viewport-{Guid.NewGuid():N}.ply");
        await File.WriteAllTextAsync(path, Cube);
        try
        {
            using var surface = new HwndSource(new HwndSourceParameters("Mangosteen hidden model test") { Width = 640, Height = 480, WindowStyle = 0 });
            using var host = new NativeGlHost { Width = 1, Height = 1 };
            try { surface.RootVisual = host; }
            catch (Win32Exception ex) when (ex.Message == "Could not create the OpenGL rendering context.")
            {
                Assert.Inconclusive("A WGL context is unavailable on this machine.");
            }
            host.Measure(new Size(1, 1));
            host.Arrange(new Rect(0, 0, 1, 1));
            host.UpdateLayout();
            using var renderer = new F3dModelRenderer(host, runtime!);
            Exception? failure = null;
            renderer.RenderingFailed += (_, ex) => failure = ex;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            for (var load = 0; load < 2; load++)
            {
                host.ResizeToDips(1, 1);
                await renderer.OpenAsync(path, true, timeout.Token);
                host.ResizeToDips(640, 480);
                renderer.ApplyTheme(true);
                Assert.IsTrue(renderer.IsOpen);
                var probe = new NativeProbe(renderer);
                var original = probe.Capture(host);
                AssertVisibleGeometry(original);
                Assert.IsTrue(probe.Angle() > 1 && probe.Angle() < 120, "Camera viewing angle must not collapse at the hidden 1x1 size.");

                renderer.Orbit(45, 15);
                var orbited = probe.Capture(host);
                AssertVisibleGeometry(orbited);
                Assert.IsGreaterThan(1000, original.Where((b, i) => Math.Abs(b - orbited[i]) > 8).Count(), "Orbit must change visible model pixels.");
                renderer.SetZoom(2);
                Assert.AreEqual(2, renderer.ZoomFactor);
                renderer.ResetView();
                Assert.AreEqual(1, renderer.ZoomFactor);
                var reset = probe.Capture(host);
                AssertVisibleGeometry(reset);
                Assert.IsLessThan(3, original.Zip(reset, (a, b) => Math.Abs(a - b)).Average(), "Reset must restore the starting camera, not keep the orbited angle.");
                renderer.SetZoom(double.NaN);
                Assert.AreEqual(1, renderer.ZoomFactor);
                renderer.ApplyTheme(false);
                var light = probe.Capture(host);
                Assert.IsGreaterThan(original.Take(60).Average(x => (double)x) + 100, light.Take(60).Average(x => (double)x));
                renderer.ApplyTheme(true);
                host.ResizeToDips(900, 500);
                AssertVisibleGeometry(probe.Capture(host));
                Assert.IsNull(failure, failure?.ToString());
                renderer.CloseCurrent();
                Assert.IsFalse(renderer.IsOpen);
            }
            renderer.Dispose();
            surface.RootVisual = null;
        }
        finally { File.Delete(path); }
    });

    private static void AssertVisibleGeometry(byte[] pixels)
    {
        var bright = 0;
        for (var i = 0; i < pixels.Length; i += 3)
            if (pixels[i] > 65 && pixels[i + 1] > 65 && pixels[i + 2] > 65) bright++;
        var ratio = (double)bright / (pixels.Length / 3);
        Assert.IsTrue(ratio > 0.05 && ratio < 0.75, $"Expected a framed model and background, not a blank or solid-color view (bright area {ratio:P1}).");
    }

    private sealed class NativeProbe
    {
        private readonly nint _module;
        private readonly nint _window;
        private readonly nint _camera;

        public NativeProbe(F3dModelRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Field(object value, string name) => value.GetType().GetField(name, flags)!.GetValue(value)!;
            _module = (nint)Field(Field(renderer, "_api"), "_module");
            _window = (nint)Field(renderer, "_window");
            _camera = (nint)Field(renderer, "_camera");
        }

        public double Angle() => Export<GetAngle>("f3d_camera_get_view_angle")(_camera);

        public byte[] Capture(NativeGlHost host)
        {
            byte[] pixels = [];
            host.ExecuteWithContext(() =>
            {
                var image = Export<RenderImage>("f3d_window_render_to_image")(_window, 0);
                Assert.AreNotEqual(nint.Zero, image);
                try
                {
                    var width = Export<ImageSize>("f3d_image_get_width")(image);
                    var height = Export<ImageSize>("f3d_image_get_height")(image);
                    Assert.AreEqual((uint)host.PixelWidth, width);
                    Assert.AreEqual((uint)host.PixelHeight, height);
                    Assert.AreEqual(3u, Export<ImageSize>("f3d_image_get_channel_count")(image));
                    pixels = new byte[checked(width * height * 3)];
                    Marshal.Copy(Export<ImageContent>("f3d_image_get_content")(image), pixels, 0, pixels.Length);
                }
                finally { Export<DeleteImage>("f3d_image_delete")(image); }
            });
            return pixels;
        }

        private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, name));
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double GetAngle(nint camera);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint RenderImage(nint window, int noBackground);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ImageSize(nint image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ImageContent(nint image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DeleteImage(nint image);
    }

    private const string Cube = """
        ply
        format ascii 1.0
        element vertex 8
        property float x
        property float y
        property float z
        element face 6
        property list uchar int vertex_indices
        end_header
        -1 -1 -1
        1 -1 -1
        1 1 -1
        -1 1 -1
        -1 -1 1
        1 -1 1
        1 1 1
        -1 1 1
        4 0 3 2 1
        4 4 5 6 7
        4 0 1 5 4
        4 1 2 6 5
        4 2 3 7 6
        4 3 0 4 7
        """;
}
