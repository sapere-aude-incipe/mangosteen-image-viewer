using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media.Media3D;
using Mangosteen.Rendering.Advanced;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // F3D plugin initialization is process-global.
public sealed class F3dViewportTests
{
    [TestMethod]
    [TestCategory("NativeOpenGL")]
    public Task Grid_Follows_Elevated_Model_Without_Moving_The_Camera_Or_File() => StaTest.RunAsync(async () =>
    {
        var runtime = Environment.GetEnvironmentVariable("MANGOSTEEN_TEST_F3D_DIRECTORY");
        if (string.IsNullOrEmpty(runtime))
            Assert.Inconclusive("Set MANGOSTEEN_TEST_F3D_DIRECTORY to run the grid pixel regression.");
        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-elevated-{Guid.NewGuid():N}.ply");
        await File.WriteAllTextAsync(path, ElevatedCube);
        var originalFile = await File.ReadAllBytesAsync(path);
        try
        {
            using var surface = new HwndSource(new HwndSourceParameters("Mangosteen hidden grid test") { Width = 640, Height = 480, WindowStyle = 0 });
            using var host = new NativeGlHost { Width = 640, Height = 480 };
            try { surface.RootVisual = host; }
            catch (Win32Exception ex) when (ex.Message == "Could not create the OpenGL rendering context.")
            {
                Assert.Inconclusive("A WGL context is unavailable on this machine.");
            }
            host.Measure(new Size(640, 480));
            host.Arrange(new Rect(0, 0, 640, 480));
            host.UpdateLayout();
            using var renderer = new F3dModelRenderer(host, runtime!);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await renderer.OpenAsync(path, true, timeout.Token);
            var probe = new NativeProbe(renderer);
            var grounded = probe.Capture(host);
            AssertVisibleGeometry(grounded);
            SaveCapture("model-grid-beneath.png", grounded, host.PixelWidth, host.PixelHeight);
            var position = probe.Position();
            var focal = probe.FocalPoint();
            var angle = probe.Angle();
            renderer.SetGridAtWorldOrigin(true);
            var absolute = probe.Capture(host);
            AssertVisibleGeometry(absolute);
            SaveCapture("model-grid-world-origin.png", absolute, host.PixelWidth, host.PixelHeight);
            Assert.IsGreaterThan(1000, grounded.Where((b, i) => Math.Abs(b - absolute[i]) > 8).Count(), "The grid must visibly change position for a model above the origin.");
            Assert.AreEqual(0, (probe.Position() - position).Length, 1e-8);
            Assert.AreEqual(0, (probe.FocalPoint() - focal).Length, 1e-8);
            Assert.AreEqual(angle, probe.Angle(), 1e-8);
            Assert.AreEqual(1, renderer.ZoomFactor);
            renderer.ResetView();
            Assert.IsLessThan(3, absolute.Zip(probe.Capture(host), (a, b) => Math.Abs(a - b)).Average(), "Reset must preserve the grid preference.");
            await renderer.OpenAsync(path, true, timeout.Token);
            Assert.IsLessThan(3, absolute.Zip(probe.Capture(host), (a, b) => Math.Abs(a - b)).Average(), "Reopening must preserve the selected grid mode.");
            renderer.SetGridAtWorldOrigin(false);
            Assert.IsLessThan(3, grounded.Zip(probe.Capture(host), (a, b) => Math.Abs(a - b)).Average(), "Switching back must restore the beneath-model grid.");
            CollectionAssert.AreEqual(originalFile, await File.ReadAllBytesAsync(path));
            renderer.Dispose();
            surface.RootVisual = null;
        }
        finally { File.Delete(path); }
    });

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
                var presented = probe.CapturePresented(host, renderer);
                AssertIndicator(presented, host.PixelWidth, host.PixelHeight, true, host.SurfaceScale);
                SaveCapture("model-dark.png", presented, host.PixelWidth, host.PixelHeight);
                var originalScale = host.SurfaceScale;
                foreach (var scale in new[] { 1.5, 2.0 })
                {
                    typeof(NativeGlHost).GetProperty(nameof(NativeGlHost.SurfaceScale))!.SetValue(host, scale);
                    AssertIndicator(probe.CapturePresented(host, renderer), host.PixelWidth, host.PixelHeight, true, scale);
                }
                typeof(NativeGlHost).GetProperty(nameof(NativeGlHost.SurfaceScale))!.SetValue(host, originalScale);
                Assert.IsTrue(probe.Angle() > 1 && probe.Angle() < 120, "Camera viewing angle must not collapse at the hidden 1x1 size.");

                var initialBasis = probe.Orientation();
                renderer.OrbitFromDrag(host.PixelWidth * 0.05, 0);
                Assert.AreEqual(0, probe.Orientation().Right.Y, 1e-8, "A horizontal drag must not roll the ground plane.");
                Assert.IsLessThan(0, Vector3D.DotProduct(probe.Orientation().TowardViewer, initialBasis.Right), "Dragging right must move the camera left around the model.");
                renderer.ResetView();
                renderer.OrbitFromDrag(0, host.PixelHeight * 0.05);
                Assert.IsGreaterThan(0, Vector3D.DotProduct(probe.Orientation().TowardViewer, initialBasis.Up), "Dragging down must tilt the camera up, pulling the model down.");
                renderer.ResetView();
                renderer.Orbit(0, 90);
                renderer.OrbitFromDrag(host.PixelWidth * 0.05, 0);
                var poleBasis = probe.Orientation();
                Assert.AreEqual(0, poleBasis.Right.Y, 1e-8);
                Assert.AreEqual(Math.Sin(UprightOrbit.MaximumPitch * Math.PI / 180), poleBasis.TowardViewer.Y, 1e-8);
                Assert.AreEqual(1, poleBasis.Right.Length, 1e-8);
                Assert.AreEqual(1, poleBasis.Up.Length, 1e-8);
                Assert.IsLessThan(1e-8, Math.Abs(Vector3D.DotProduct(poleBasis.Up, poleBasis.TowardViewer)));
                renderer.Orbit(0, 270);
                Assert.AreEqual(poleBasis.TowardViewer.Y, probe.Orientation().TowardViewer.Y, 1e-8, "Continued vertical dragging must not flip the camera over the pole.");
                AssertVisibleGeometry(probe.Capture(host));
                renderer.ResetView();
                Assert.IsLessThan(3, original.Zip(probe.Capture(host), (a, b) => Math.Abs(a - b)).Average(), "Reset must recover from clamped vertical drags.");

                renderer.SetZoom(1.5);
                renderer.Pan(0.1, 0.05);
                var focalPoint = probe.FocalPoint();
                var distance = (probe.Position() - focalPoint).Length;
                renderer.Orbit(45, 15);
                var elevation = probe.Orientation().TowardViewer.Y;
                for (var i = 0; i < 12; i++)
                {
                    renderer.OrbitFromDrag(host.PixelWidth * 0.05, 0);
                    var basis = probe.Orientation();
                    Assert.AreEqual(0, basis.Right.Y, 1e-8, "Repeated horizontal drags after a tilt must keep the horizon level.");
                    Assert.AreEqual(elevation, basis.TowardViewer.Y, 1e-8);
                }
                Assert.AreEqual(0, (probe.FocalPoint() - focalPoint).Length, 1e-8, "Orbit must preserve the panned target.");
                Assert.AreEqual(distance, (probe.Position() - focalPoint).Length, 1e-8, "Orbit must preserve the zoom distance.");
                Assert.AreEqual(1.5, renderer.ZoomFactor);
                SaveCapture("model-upright-orbit.png", probe.CapturePresented(host, renderer), host.PixelWidth, host.PixelHeight);
                renderer.ResetView();

                renderer.Orbit(45, 15);
                var orbited = probe.Capture(host);
                AssertVisibleGeometry(orbited);
                Assert.IsGreaterThan(1000, original.Where((b, i) => Math.Abs(b - orbited[i]) > 8).Count(), "Orbit must change visible model pixels.");
                var expectedRestoredView = probe.CapturePresented(host, renderer);
                // Simulate a lost front buffer while retaining the scene, camera and size.
                host.Render(() =>
                {
                    OpenGl11.Disable(OpenGl11.ScissorTest);
                    OpenGl11.ClearColor(1, 0, 1, 1);
                    OpenGl11.Clear(OpenGl11.ColorBufferBit);
                });
                var damaged = probe.ReadPresented(host);
                Assert.AreEqual(255, damaged[0]);
                Assert.AreEqual(0, damaged[1]);
                Assert.AreEqual(255, damaged[2]);
                NativeRepaintTests.SendMessage(host.Handle, 0x000F, 0, 0);
                await host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var restoredView = probe.ReadPresented(host);
                AssertVisibleGeometry(restoredView);
                Assert.IsLessThan(3, expectedRestoredView.Zip(restoredView, (a, b) => Math.Abs(a - b)).Average(),
                    "Exposure must restore the existing model and orbit, without resizing, resetting the camera or reloading the file.");
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
                var lightPresented = probe.CapturePresented(host, renderer);
                AssertIndicator(lightPresented, host.PixelWidth, host.PixelHeight, false, host.SurfaceScale);
                SaveCapture("model-light.png", lightPresented, host.PixelWidth, host.PixelHeight);
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

    private static void AssertIndicator(byte[] pixels, int width, int height, bool darkMode, double scale)
    {
        var rightInset = (int)Math.Round(NativeOrientationOverlay.RightInset * scale);
        var bottomInset = (int)Math.Round(NativeOrientationOverlay.BottomInset * scale);
        var size = Math.Min((int)Math.Round(OrientationIndicator.LogicalSize * scale), Math.Min(width - 2 * rightInset, height - 2 * bottomInset));
        for (var axis = 0; axis < 3; axis++)
        {
            var color = OrientationIndicator.AxisColor(axis, darkMode);
            var matches = 0;
            for (var y = bottomInset; y < bottomInset + size; y++)
            for (var x = width - rightInset - size; x < width - rightInset; x++)
            {
                var index = (y * width + x) * 3;
                if (Math.Abs(pixels[index] - color.Red) < 4 && Math.Abs(pixels[index + 1] - color.Green) < 4 && Math.Abs(pixels[index + 2] - color.Blue) < 4)
                    matches++;
            }
            Assert.IsGreaterThan(30, matches, $"The composited XYZ indicator must display axis {axis} with the selected theme color.");
        }
    }

    private static void SaveCapture(string name, byte[] pixels, int width, int height)
    {
        var directory = Environment.GetEnvironmentVariable("MANGOSTEEN_TEST_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = (y * width + x) * 3;
            bitmap.SetPixel(x, height - y - 1, new SKColor(pixels[index], pixels[index + 1], pixels[index + 2]));
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(directory, name));
        data.SaveTo(stream);
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

        private Vector3D Vector(string name)
        {
            var values = new double[3];
            Export<GetVector>(name)(_camera, values);
            return new(values[0], values[1], values[2]);
        }

        public Vector3D Position() => Vector("f3d_camera_get_position");
        public Vector3D FocalPoint() => Vector("f3d_camera_get_focal_point");

        public ModelViewOrientation Orientation()
        {
            var position = Vector("f3d_camera_get_position");
            var focal = Vector("f3d_camera_get_focal_point");
            var up = Vector("f3d_camera_get_view_up");
            var direction = position - focal;
            Assert.IsTrue(double.IsFinite(direction.Length) && direction.Length > 1e-8);
            Assert.IsTrue(double.IsFinite(up.Length) && up.Length > 1e-8);
            direction.Normalize();
            up.Normalize();
            Assert.AreEqual(0, Vector3D.DotProduct(direction, up), 1e-8, "The native camera must preserve an orthogonal up vector, not just the indicator's fallback basis.");
            return ModelViewOrientation.FromCamera(position, focal, up);
        }

        public byte[] ReadPresented(NativeGlHost host)
        {
            var pixels = new byte[host.PixelWidth * host.PixelHeight * 3];
            host.ExecuteWithContext(() =>
            {
                PixelStore(0x0D05, 1);
                ReadBuffer(0x0404); // GL_FRONT: do not render while observing the repaint's result.
                ReadPixels(0, 0, host.PixelWidth, host.PixelHeight, 0x1907, OpenGl11.UnsignedByte, pixels);
                Assert.AreEqual(0u, OpenGl11.GetError());
            });
            return pixels;
        }

        public byte[] CapturePresented(NativeGlHost host, F3dModelRenderer renderer)
        {
            var pixels = new byte[host.PixelWidth * host.PixelHeight * 3];
            host.ExecuteWithContext(() =>
            {
                typeof(F3dModelRenderer).GetMethod("RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
                var states = new uint[] { 0x8B8D, 0x88EF, 0x0CF2, 0x0CF3, 0x0CF4 };
                var original = states.ToDictionary(state => state, state => { GetInteger(state, out var value); return value; });
                using (var overlay = new NativeOrientationOverlay())
                {
                    var api = (F3dNativeApi)typeof(F3dModelRenderer).GetField("_api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
                    overlay.Draw(api.GetOrientation(_camera), true, host.PixelWidth, host.PixelHeight, host.SurfaceScale);
                }
                foreach (var state in states)
                {
                    GetInteger(state, out var after);
                    Assert.AreEqual(original[state], after, $"The indicator must restore OpenGL state 0x{state:X}.");
                }
                // Restore the selected theme after the isolated state-preservation probes.
                typeof(F3dModelRenderer).GetMethod("RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
                PixelStore(0x0D05, 1); // GL_PACK_ALIGNMENT
                ReadBuffer(0x0405); // GL_BACK
                ReadPixels(0, 0, host.PixelWidth, host.PixelHeight, 0x1907, OpenGl11.UnsignedByte, pixels); // GL_RGB
                Assert.AreEqual(0u, OpenGl11.GetError(), "The overlay must not leave an OpenGL error.");
            });
            return pixels;
        }

        [DllImport("opengl32.dll", EntryPoint = "glPixelStorei")] private static extern void PixelStore(uint name, int value);
        [DllImport("opengl32.dll", EntryPoint = "glGetIntegerv")] private static extern void GetInteger(uint name, out int value);
        [DllImport("opengl32.dll", EntryPoint = "glReadBuffer")] private static extern void ReadBuffer(uint mode);
        [DllImport("opengl32.dll", EntryPoint = "glReadPixels")] private static extern void ReadPixels(int x, int y, int width, int height, uint format, uint type, [Out] byte[] pixels);

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
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GetVector(nint camera, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] double[] values);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint RenderImage(nint window, int noBackground);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ImageSize(nint image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ImageContent(nint image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DeleteImage(nint image);
    }

    private const string ElevatedCube = """
        ply
        format ascii 1.0
        element vertex 8
        property float x
        property float y
        property float z
        element face 6
        property list uchar int vertex_indices
        end_header
        -1 4 -1
        1 4 -1
        1 6 -1
        -1 6 -1
        -1 4 1
        1 4 1
        1 6 1
        -1 6 1
        4 0 3 2 1
        4 4 5 6 7
        4 0 1 5 4
        4 1 2 6 5
        4 2 3 7 6
        4 3 0 4 7
        """;

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
