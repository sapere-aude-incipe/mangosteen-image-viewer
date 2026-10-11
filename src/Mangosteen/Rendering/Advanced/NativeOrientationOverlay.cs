using System.Runtime.InteropServices;
using SkiaSharp;

namespace Mangosteen.Rendering.Advanced;

// Draw inside the native surface: a WPF overlay would be hidden by HwndHost airspace.
internal sealed partial class NativeOrientationOverlay : IDisposable
{
    internal const int RightInset = 16;
    internal const int BottomInset = 8;
    private SKBitmap? _bitmap;
    private ModelViewOrientation _orientation;
    private bool _darkMode;
    private WindowPosition? _windowPosition;
    private UseProgram? _useProgram;
    private BindBuffer? _bindBuffer;

    public void Draw(ModelViewOrientation orientation, bool darkMode, int width, int height, double dpiScale)
    {
        var scale = Math.Clamp(dpiScale, 0.5, 4);
        var rightInset = (int)Math.Round(RightInset * scale);
        var bottomInset = (int)Math.Round(BottomInset * scale);
        var size = Math.Min((int)Math.Round(OrientationIndicator.LogicalSize * scale), Math.Min(width - 2 * rightInset, height - 2 * bottomInset));
        if (size < 32) return;
        if (_bitmap is null || _bitmap.Width != size || _darkMode != darkMode || !Similar(_orientation, orientation))
        {
            _bitmap?.Dispose();
            _bitmap = OrientationIndicator.Rasterize(orientation, darkMode, size);
            _orientation = orientation;
            _darkMode = darkMode;
        }

        _windowPosition ??= Export<WindowPosition>("glWindowPos2i");
        _useProgram ??= Export<UseProgram>("glUseProgram");
        _bindBuffer ??= Export<BindBuffer>("glBindBuffer");
        GetInteger(0x8B8D, out var program); // GL_CURRENT_PROGRAM
        GetInteger(0x88EF, out var unpackBuffer); // GL_PIXEL_UNPACK_BUFFER_BINDING
        PushAttributes(0x000FFFFF); // GL_ALL_ATTRIB_BITS
        PushClientAttributes(0x00000001); // GL_CLIENT_PIXEL_STORE_BIT
        try
        {
            _useProgram(0);
            _bindBuffer(0x88EC, 0); // GL_PIXEL_UNPACK_BUFFER
            OpenGl11.Disable(OpenGl11.DepthTest);
            OpenGl11.Disable(OpenGl11.ScissorTest);
            OpenGl11.Disable(0x0B90); // GL_STENCIL_TEST
            OpenGl11.Disable(0x0BC0); // GL_ALPHA_TEST
            OpenGl11.Disable(0x0B60); // GL_FOG
            OpenGl11.Disable(0x8DB9); // GL_FRAMEBUFFER_SRGB
            OpenGl11.Enable(OpenGl11.Blend);
            BlendEquation(0x8006); // GL_FUNC_ADD
            OpenGl11.BlendFunc(1, OpenGl11.OneMinusSrcAlpha); // Skia supplies premultiplied alpha.
            OpenGl11.Color4(1, 1, 1, 1);
            PixelStore(0x0CF5, 4); // GL_UNPACK_ALIGNMENT
            PixelStore(0x0CF2, 0); // GL_UNPACK_ROW_LENGTH
            PixelStore(0x0CF3, 0); // GL_UNPACK_SKIP_ROWS
            PixelStore(0x0CF4, 0); // GL_UNPACK_SKIP_PIXELS
            PixelZoom(1, -1); // Skia's first row is the top, OpenGL's is the bottom.
            _windowPosition(width - rightInset - size, bottomInset + size);
            DrawPixels(size, size, OpenGl11.Rgba, OpenGl11.UnsignedByte, _bitmap.GetPixels());
        }
        finally
        {
            PopClientAttributes();
            PopAttributes();
            _bindBuffer(0x88EC, (uint)unpackBuffer);
            _useProgram((uint)program);
        }
    }

    private BlendEquationMode? _blendEquation;
    private void BlendEquation(uint mode) => (_blendEquation ??= Export<BlendEquationMode>("glBlendEquation"))(mode);

    private static bool Similar(ModelViewOrientation a, ModelViewOrientation b) =>
        (a.Right - b.Right).LengthSquared < 1e-12 && (a.Up - b.Up).LengthSquared < 1e-12 &&
        (a.TowardViewer - b.TowardViewer).LengthSquared < 1e-12;

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private static T Export<T>(string name) where T : Delegate
    {
        var pointer = WglGetProcAddress(name);
        if (pointer == 0 || pointer == 1 || pointer == 2 || pointer == 3 || pointer == -1)
            throw new EntryPointNotFoundException($"OpenGL did not expose '{name}'.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void WindowPosition(int x, int y);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void UseProgram(uint program);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BindBuffer(uint target, uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BlendEquationMode(uint mode);
    [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint WglGetProcAddress(string name);
    [LibraryImport("opengl32.dll", EntryPoint = "glGetIntegerv")] private static partial void GetInteger(uint name, out int value);
    [LibraryImport("opengl32.dll", EntryPoint = "glPushAttrib")] private static partial void PushAttributes(uint mask);
    [LibraryImport("opengl32.dll", EntryPoint = "glPopAttrib")] private static partial void PopAttributes();
    [LibraryImport("opengl32.dll", EntryPoint = "glPushClientAttrib")] private static partial void PushClientAttributes(uint mask);
    [LibraryImport("opengl32.dll", EntryPoint = "glPopClientAttrib")] private static partial void PopClientAttributes();
    [LibraryImport("opengl32.dll", EntryPoint = "glPixelStorei")] private static partial void PixelStore(uint name, int value);
    [LibraryImport("opengl32.dll", EntryPoint = "glPixelZoom")] private static partial void PixelZoom(float x, float y);
    [LibraryImport("opengl32.dll", EntryPoint = "glDrawPixels")] private static partial void DrawPixels(int width, int height, uint format, uint type, nint pixels);
}
