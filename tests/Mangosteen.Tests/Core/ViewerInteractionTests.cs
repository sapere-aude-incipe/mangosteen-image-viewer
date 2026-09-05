using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Mangosteen.Decoding;
using Mangosteen.Navigation;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ViewerInteractionTests
{
    [TestMethod]
    public Task Preview_Arrow_Keys_Do_Not_Navigate_When_A_Menu_Is_The_Source() => StaTest.RunAsync(() =>
    {
        var window = new MainWindow(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = false });
        var navigator = (ImageNavigator)typeof(MainWindow).GetField("_navigator", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        navigator.Apply(new ImageFolderSnapshot([@"C:\one.png", @"C:\two.png"], 0));
        using var source = new HwndSource(new HwndSourceParameters("Mangosteen keyboard test") { WindowStyle = 0, Width = 1, Height = 1 });
        foreach (var name in new[] { "OptionsMenuItem", "SamplingMenuItem", "DarkModeMenuItem" })
        {
            var menu = (MenuItem)window.FindName(name);
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            menu.RaiseEvent(e);
            Assert.IsFalse(e.Handled);
            Assert.AreEqual(0, navigator.CurrentIndex);
        }
        return Task.CompletedTask;
    });

    [TestMethod]
    public void Modified_Arrow_Keys_Are_Not_Image_Navigation()
    {
        foreach (var modifier in new[] { ModifierKeys.Control, ModifierKeys.Shift, ModifierKeys.Alt })
        {
            Assert.AreEqual(ViewerKeyboardCommand.None, MainWindow.ResolveKeyboardCommand(Key.Right, modifier, false, true, true, true));
        }
    }

    [TestMethod]
    public Task Print_Snapshot_Survives_Disposal_Of_The_Displayed_Image() => StaTest.RunAsync(() =>
    {
        using var bitmap = new SKBitmap(2, 3);
        bitmap.Erase(SKColors.Red);
        using var image = new DecodedImage(new ImageMetadata("original.png", 2, 3, 1, "test"),
            [new DecodedFrame(SKImage.FromBitmap(bitmap), TimeSpan.Zero)], true);
        var snapshot = MainWindow.CapturePrintSnapshot(image, 0, 1, "original.png");
        image.Dispose();
        bitmap.Erase(SKColors.Blue);
        Assert.IsTrue(snapshot.Source.IsFrozen);
        Assert.AreEqual("original.png", snapshot.JobName);
        Assert.AreEqual(1, snapshot.QuarterTurns);
        var pixels = new byte[2 * 3 * 4];
        snapshot.Source.CopyPixels(pixels, 8, 0);
        Assert.AreEqual((byte)255, pixels[2]);
        Assert.AreEqual((byte)0, pixels[0]);
        return Task.CompletedTask;
    });
}
