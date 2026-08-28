using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Mangosteen.Rendering.Advanced;

internal readonly record struct NativePointerEvent(int X, int Y, int Delta = 0);

internal sealed partial class NativeGlHost : HwndHost
{
    private const int WmSize = 0x0005;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmContextMenu = 0x007B;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int XButton1 = 0x0001;
    private const int XButton2 = 0x0002;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint CsOwnDc = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const byte PfdTypeRgba = 0;
    private const uint PfdDrawToWindow = 0x00000004;
    private const uint PfdSupportOpenGl = 0x00000020;
    private const uint PfdDoubleBuffer = 0x00000001;
    private const sbyte PfdMainPlane = 0;
    private const string WindowClassName = "Mangosteen.NativeGlHost";
    private static readonly NativeMethods.WindowProcedure WindowProcedure = NativeMethods.DefWindowProc;
    private static readonly object RegistrationGate = new();
    private static bool _windowClassRegistered;

    private nint _windowHandle;
    private nint _deviceContext;
    private nint _renderingContext;
    private bool _capturingPointer;
    private bool _abandonForProcessShutdown;
    private readonly object _contextGate = new();

    public event EventHandler? SurfaceReady;
    public event EventHandler? SurfaceSizeChanged;
    public event EventHandler<NativePointerEvent>? PointerPressed;
    public event EventHandler<NativePointerEvent>? PointerMoved;
    public event EventHandler<NativePointerEvent>? PointerReleased;
    public event EventHandler<NativePointerEvent>? WheelChanged;
    public event EventHandler<int>? NavigationRequested;

    public event EventHandler? ContextMenuRequested;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public bool IsSurfaceReady => _renderingContext != nint.Zero;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureWindowClassRegistered();
        _windowHandle = NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            string.Empty,
            WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            nint.Zero,
            NativeMethods.GetModuleHandle(null),
            nint.Zero);
        if (_windowHandle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the OpenGL child window.");
        }

        try
        {
            _deviceContext = NativeMethods.GetDC(_windowHandle);
            if (_deviceContext == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not acquire the OpenGL device context.");
            }

            var descriptor = PixelFormatDescriptor.Create();
            var pixelFormat = NativeMethods.ChoosePixelFormat(_deviceContext, ref descriptor);
            if (pixelFormat == 0 || !NativeMethods.SetPixelFormat(_deviceContext, pixelFormat, ref descriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure the OpenGL pixel format.");
            }

            _renderingContext = NativeMethods.WglCreateContext(_deviceContext);
            if (_renderingContext == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the OpenGL rendering context.");
            }

            lock (_contextGate)
            {
                MakeCurrentCore();
                SurfaceReady?.Invoke(this, EventArgs.Empty);
                NativeMethods.WglMakeCurrent(nint.Zero, nint.Zero);
            }
            return new HandleRef(this, _windowHandle);
        }
        catch
        {
            ReleaseNativeResources();
            throw;
        }
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_capturingPointer)
        {
            NativeMethods.ReleaseCapture();
            _capturingPointer = false;
        }

        var contextLockTaken = false;
        if (_abandonForProcessShutdown)
        {
            contextLockTaken = Monitor.TryEnter(_contextGate);
            if (!contextLockTaken)
            {
                _renderingContext = nint.Zero;
                _deviceContext = nint.Zero;
                _windowHandle = nint.Zero;
                return;
            }
        }
        else
        {
            Monitor.Enter(_contextGate);
            contextLockTaken = true;
        }

        try
        {
            ReleaseNativeResources();
        }
        finally
        {
            if (contextLockTaken)
            {
                Monitor.Exit(_contextGate);
            }
        }
    }

    private void ReleaseNativeResources()
    {
        if (_renderingContext != nint.Zero)
        {
            NativeMethods.WglMakeCurrent(nint.Zero, nint.Zero);
            NativeMethods.WglDeleteContext(_renderingContext);
            _renderingContext = nint.Zero;
        }

        if (_deviceContext != nint.Zero && _windowHandle != nint.Zero)
        {
            NativeMethods.ReleaseDC(_windowHandle, _deviceContext);
            _deviceContext = nint.Zero;
        }

        if (_windowHandle != nint.Zero)
        {
            NativeMethods.DestroyWindow(_windowHandle);
            _windowHandle = nint.Zero;
        }
    }

    protected override nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case WmSize:
                PixelWidth = LowWord(lParam);
                PixelHeight = HighWord(lParam);
                SurfaceSizeChanged?.Invoke(this, EventArgs.Empty);
                break;
            case WmLButtonDown:
                _capturingPointer = true;
                NativeMethods.SetCapture(hwnd);
                PointerPressed?.Invoke(this, GetClientPointer(lParam));
                handled = true;
                break;
            case WmMouseMove:
                PointerMoved?.Invoke(this, GetClientPointer(lParam));
                break;
            case WmLButtonUp:
                if (_capturingPointer)
                {
                    NativeMethods.ReleaseCapture();
                    _capturingPointer = false;
                }
                PointerReleased?.Invoke(this, GetClientPointer(lParam));
                handled = true;
                break;
            case WmMouseWheel:
                var screenPoint = new NativePoint { X = SignedLowWord(lParam), Y = SignedHighWord(lParam) };
                NativeMethods.ScreenToClient(hwnd, ref screenPoint);
                WheelChanged?.Invoke(this, new NativePointerEvent(screenPoint.X, screenPoint.Y, SignedHighWord(wParam)));
                handled = true;
                break;
            case WmXButtonDown:
                var button = HighWord(wParam);
                if (button is XButton1 or XButton2)
                {
                    NavigationRequested?.Invoke(this, button == XButton1 ? -1 : 1);
                    handled = true;
                    return new nint(1);
                }
                break;
            case WmContextMenu:
                ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return nint.Zero;
    }

    public void Render(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsSurfaceReady || PixelWidth <= 0 || PixelHeight <= 0) return;
        lock (_contextGate)
        {
            MakeCurrentCore();
            try
            {
                action();
                NativeMethods.SwapBuffers(_deviceContext);
            }
            finally
            {
                NativeMethods.WglMakeCurrent(nint.Zero, nint.Zero);
            }
        }
    }

    public void ExecuteWithContext(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsSurfaceReady) return;
        lock (_contextGate)
        {
            MakeCurrentCore();
            try
            {
                action();
            }
            finally
            {
                NativeMethods.WglMakeCurrent(nint.Zero, nint.Zero);
            }
        }
    }

    public void ResizeToDips(double width, double height)
    {
        if (_windowHandle == nint.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY));
        NativeMethods.SetWindowPos(
            _windowHandle,
            nint.Zero,
            0,
            0,
            pixelWidth,
            pixelHeight,
            SwpNoMove | SwpNoZOrder | SwpNoActivate);
    }

    public void AbandonForProcessShutdown()
    {
        _abandonForProcessShutdown = true;
    }

    private void MakeCurrentCore()
    {
        if (_renderingContext == nint.Zero || !NativeMethods.WglMakeCurrent(_deviceContext, _renderingContext))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not activate the OpenGL rendering context.");
        }
    }

    private static NativePointerEvent GetClientPointer(nint lParam)
    {
        return new NativePointerEvent(SignedLowWord(lParam), SignedHighWord(lParam));
    }

    private static int LowWord(nint value) => unchecked((ushort)((long)value & 0xffff));

    private static int HighWord(nint value) => unchecked((ushort)(((long)value >> 16) & 0xffff));

    private static int SignedLowWord(nint value) => unchecked((short)((long)value & 0xffff));

    private static int SignedHighWord(nint value) => unchecked((short)(((long)value >> 16) & 0xffff));

    private static void EnsureWindowClassRegistered()
    {
        if (_windowClassRegistered) return;
        lock (RegistrationGate)
        {
            if (_windowClassRegistered) return;
            var windowClass = new WindowClass
            {
                Style = CsOwnDc,
                WindowProcedure = WindowProcedure,
                Instance = NativeMethods.GetModuleHandle(null),
                ClassName = WindowClassName
            };
            var atom = NativeMethods.RegisterClass(ref windowClass);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the OpenGL window class.");
            }

            _windowClassRegistered = true;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public NativeMethods.WindowProcedure WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size;
        public ushort Version;
        public uint Flags;
        public byte PixelType;
        public byte ColorBits;
        public byte RedBits;
        public byte RedShift;
        public byte GreenBits;
        public byte GreenShift;
        public byte BlueBits;
        public byte BlueShift;
        public byte AlphaBits;
        public byte AlphaShift;
        public byte AccumBits;
        public byte AccumRedBits;
        public byte AccumGreenBits;
        public byte AccumBlueBits;
        public byte AccumAlphaBits;
        public byte DepthBits;
        public byte StencilBits;
        public byte AuxiliaryBuffers;
        public sbyte LayerType;
        public byte Reserved;
        public uint LayerMask;
        public uint VisibleMask;
        public uint DamageMask;

        public static PixelFormatDescriptor Create()
        {
            return new PixelFormatDescriptor
            {
                Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
                Version = 1,
                Flags = PfdDrawToWindow | PfdSupportOpenGl | PfdDoubleBuffer,
                PixelType = PfdTypeRgba,
                ColorBits = 32,
                AlphaBits = 8,
                DepthBits = 24,
                StencilBits = 8,
                LayerType = PfdMainPlane
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static partial class NativeMethods
    {
        internal delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", EntryPoint = "RegisterClassW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern ushort RegisterClass(ref WindowClass windowClass);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyWindow(nint hwnd);

        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        internal static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial nint GetDC(nint hwnd);

        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(nint hwnd, nint deviceContext);

        [LibraryImport("user32.dll")]
        internal static partial nint SetCapture(nint hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ReleaseCapture();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ScreenToClient(nint hwnd, ref NativePoint point);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        internal static partial int ChoosePixelFormat(nint deviceContext, ref PixelFormatDescriptor descriptor);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetPixelFormat(nint deviceContext, int format, ref PixelFormatDescriptor descriptor);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SwapBuffers(nint deviceContext);

        [LibraryImport("opengl32.dll", EntryPoint = "wglCreateContext", SetLastError = true)]
        internal static partial nint WglCreateContext(nint deviceContext);

        [LibraryImport("opengl32.dll", EntryPoint = "wglDeleteContext", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool WglDeleteContext(nint renderingContext);

        [LibraryImport("opengl32.dll", EntryPoint = "wglMakeCurrent", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool WglMakeCurrent(nint deviceContext, nint renderingContext);
    }
}
