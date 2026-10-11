using System.IO;
using System.Reflection;
using System.Windows;
using Mangosteen.Decoding;
using Mangosteen.Navigation;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // WPF WindowChrome uses process-wide property-descriptor caches.
public sealed class HeldNavigationTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(-1, 2)]
    public Task Repeated_Navigation_Waits_For_The_Image_Without_Queuing_Skipped_Steps(int direction, int expectedIndex) => StaTest.RunAsync(async () =>
    {
        var directory = Directory.CreateTempSubdirectory("Mangosteen-navigation-test-");
        var window = new MainWindow(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = false });
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(800, 600));
            root.Arrange(new Rect(0, 0, 800, 600));
            var paths = new string[3];
            for (var i = 0; i < paths.Length; i++)
            {
                paths[i] = Path.Combine(directory.FullName, $"{i}.png");
                using var bitmap = new SKBitmap(16, 16);
                bitmap.Erase(i == 0 ? SKColors.Red : i == 1 ? SKColors.Green : SKColors.Blue);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(paths[i], encoded.ToArray());
            }
            var navigator = Field<ImageNavigator>(window, "_navigator");
            navigator.Apply(new ImageFolderSnapshot(paths, 0));
            await InvokeTask(window, "LoadCurrentImageAsync", true);
            Assert.AreEqual(paths[0], Field<DecodedImage>(window, "_image").Metadata.Path);

            var gate = Field<SemaphoreSlim>(window, "_previewDecodeGate");
            await gate.WaitAsync();
            Task? navigation = null;
            try
            {
                navigation = InvokeTask(window, "NavigateRelativeAsync", direction);
                Assert.IsFalse(navigation.IsCompleted);
                for (var i = 0; i < 20; i++)
                    await InvokeTask(window, "NavigateRelativeAsync", i % 2 == 0 ? direction : -direction);
                Assert.AreEqual(expectedIndex, navigator.CurrentIndex);
            }
            finally
            {
                gate.Release();
                if (navigation is not null) await navigation;
            }

            Assert.AreEqual(paths[expectedIndex], Field<DecodedImage>(window, "_image").Metadata.Path);
            Assert.AreEqual(expectedIndex, navigator.CurrentIndex);
            Assert.IsFalse(Field<bool>(window, "_isNavigating"));
            await InvokeTask(window, "NavigateRelativeAsync", -direction);
            Assert.AreEqual(0, navigator.CurrentIndex);
            Assert.AreEqual(paths[0], Field<DecodedImage>(window, "_image").Metadata.Path);
        }
        finally
        {
            typeof(MainWindow).GetMethod("ClearImage", Flags)!.Invoke(window, null);
            directory.Delete(recursive: true);
        }
    });

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Flags)!.GetValue(window)!;
    private static Task InvokeTask(MainWindow window, string name, params object[] args) =>
        (Task)typeof(MainWindow).GetMethod(name, Flags)!.Invoke(window, args)!;
}
