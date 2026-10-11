using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class NativeRepaintTests
{
    [TestMethod]
    [TestCategory("NativeOpenGL")]
    [DataRow(0x0215)] // WM_CAPTURECHANGED
    [DataRow(0x001F)] // WM_CANCELMODE
    public Task Lost_Capture_Ends_The_Drag_Exactly_Once(int message) => StaTest.RunAsync(() =>
    {
        using var source = new HwndSource(new HwndSourceParameters("Mangosteen drag capture test") { Width = 64, Height = 64, WindowStyle = 0 });
        using var host = new NativeGlHost { Width = 64, Height = 64 };
        Mount(source, host);
        var field = typeof(NativeGlHost).GetField("_capturingPointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(host, true);
        var releases = 0;
        host.PointerReleased += (_, _) => releases++;
        SendMessage(host.Handle, (uint)message, 0, 0);
        Assert.IsFalse((bool)field.GetValue(host)!);
        Assert.AreEqual(1, releases);
        SendMessage(host.Handle, (uint)message, 0, 0);
        SendMessage(host.Handle, 0x0202, 0, 0); // WM_LBUTTONUP after the lost capture
        Assert.AreEqual(1, releases);
        source.RootVisual = null;
        return Task.CompletedTask;
    });
    [TestMethod]
    [TestCategory("NativeOpenGL")]
    public Task Paint_And_Show_Messages_Request_One_Deferred_Frame_Without_Resizing() => StaTest.RunAsync(async () =>
    {
        using var source = new HwndSource(new HwndSourceParameters("Mangosteen repaint test") { Width = 64, Height = 64, WindowStyle = 0 });
        using var host = new NativeGlHost { Width = 64, Height = 64 };
        Mount(source, host);
        await host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        var paints = 0;
        var sizes = 0;
        host.RepaintRequested += (_, _) => paints++;
        host.SurfaceSizeChanged += (_, _) => sizes++;
        Assert.IsTrue(InvalidateRect(host.Handle, 0, false));
        SendMessage(host.Handle, 0x000F, 0, 0); // WM_PAINT
        SendMessage(host.Handle, 0x000F, 0, 0);
        SendMessage(host.Handle, 0x0018, 1, 0); // WM_SHOWWINDOW
        Assert.AreEqual(0, paints, "Native paint handling must not enter the renderer recursively.");
        Assert.IsFalse(GetUpdateRect(host.Handle, 0, false), "BeginPaint/EndPaint must consume the update region.");
        await host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        Assert.AreEqual(1, paints, "Repeated exposure messages should coalesce into one frame.");
        Assert.AreEqual(0, sizes, "Restoring at the same size still needs a repaint.");
        SendMessage(host.Handle, 0x000F, 0, 0);
        await host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        Assert.AreEqual(2, paints);
        source.RootVisual = null;
    });

    [TestMethod]
    [TestCategory("NativeOpenGL")]
    public Task Queued_Repaint_Does_Not_Call_Renderer_After_Surface_Disposal() => StaTest.RunAsync(async () =>
    {
        using var source = new HwndSource(new HwndSourceParameters("Mangosteen disposed repaint test") { Width = 64, Height = 64, WindowStyle = 0 });
        using var host = new NativeGlHost { Width = 64, Height = 64 };
        Mount(source, host);
        await host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        var paints = 0;
        host.RepaintRequested += (_, _) => paints++;
        host.RequestRepaint();
        source.RootVisual = null;
        host.Dispose();
        await host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        Assert.AreEqual(0, paints);
    });

    private static void Mount(HwndSource source, NativeGlHost host)
    {
        try { source.RootVisual = host; }
        catch (Win32Exception ex) when (ex.Message == "Could not create the OpenGL rendering context.")
        {
            Assert.Inconclusive("A WGL context is unavailable on this machine.");
        }
        host.Measure(new Size(64, 64));
        host.Arrange(new Rect(0, 0, 64, 64));
        host.UpdateLayout();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUpdateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
