using System.Reflection;
using Mangosteen.Decoding;
using Mangosteen.Editing;
using Mangosteen.Navigation;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RotationConcurrencyTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Successful_Save_Rotates_Exactly_Once_And_Releases_The_Controls(bool pngCopy) => StaTest.RunAsync(async () =>
    {
        var directory = Directory.CreateTempSubdirectory("Mangosteen-rotation-test-");
        var path = Path.Combine(directory.FullName, "original.png");
        var window = new MainWindow(new AppSettings { IsPreloadEnabled = false, KeepReadyInBackground = false });
        try
        {
            using (var bitmap = new SKBitmap(16, 24))
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, data.ToArray());
            var original = File.ReadAllBytes(path);
            await window.OpenPathAsync(path);
            typeof(MainWindow).GetMethod("RotatePreview", Flags)!.Invoke(window, [1]);
            await window.ApplyRotationWithPromptsAsync(_ => true,
                _ => pngCopy ? RotationSaveMode.PngCopy : RotationSaveMode.ReplaceOriginal, _ => true);
            var outputPath = pngCopy ? ImageRotationService.GetDefaultPngCopyPath(path) : path;
            Assert.AreEqual(outputPath, Field<ImageNavigator>(window, "_navigator").CurrentPath);
            Assert.IsFalse(Field<bool>(window, "_isApplyingRotation"));
            Assert.AreEqual(0, Field<int>(window, "_pendingRotationQuarterTurns"));
            using var pixels = SKBitmap.Decode(outputPath);
            Assert.AreEqual(24, pixels.Width);
            Assert.AreEqual(16, pixels.Height);
            Assert.IsTrue(((System.Windows.Controls.Button)window.FindName("RotateRightButton")).IsEnabled);
            if (pngCopy) CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }
        finally
        {
            typeof(MainWindow).GetMethod("ClearForMissingFile", Flags)!.Invoke(window, null);
            directory.Delete(recursive: true);
        }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Preflight_Is_Exclusive_And_Cancelled_By_Activation_Or_Background_Close(bool backgroundClose) => StaTest.RunAsync(async () =>
    {
        var directory = Directory.CreateTempSubdirectory("Mangosteen-rotation-test-");
        var window = new MainWindow(new AppSettings { IsPreloadEnabled = false, KeepReadyInBackground = false });
        var gateHeld = false;
        Task? saving = null;
        try
        {
            var path = Path.Combine(directory.FullName, "original.png");
            var next = Path.Combine(directory.FullName, "next.png");
            using (var bitmap = new SKBitmap(16, 24))
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            {
                File.WriteAllBytes(path, data.ToArray());
                File.WriteAllBytes(next, data.ToArray());
            }
            var original = File.ReadAllBytes(path);
            await window.OpenPathAsync(path);
            typeof(MainWindow).GetMethod("RotatePreview", Flags)!.Invoke(window, [1]);
            await MagickRuntime.OperationGate.WaitAsync();
            gateHeld = true;
            var confirmations = 0;
            var riskPrompts = 0;
            saving = window.ApplyRotationWithPromptsAsync(_ => { confirmations++; return true; },
                _ => { riskPrompts++; return RotationSaveMode.ReplaceOriginal; }, _ => true);
            Assert.IsFalse(saving.IsCompleted);
            Assert.IsTrue(Field<bool>(window, "_isApplyingRotation"));
            await window.ApplyRotationWithPromptsAsync(_ => { confirmations++; return true; }, _ => RotationSaveMode.ReplaceOriginal, _ => true);
            Assert.AreEqual(1, confirmations);
            await (Task)typeof(MainWindow).GetMethod("NavigateRelativeAsync", Flags)!.Invoke(window, [1])!;
            Assert.AreEqual(path, Field<ImageNavigator>(window, "_navigator").CurrentPath);
            if (backgroundClose)
                typeof(MainWindow).GetMethod("EnterBackgroundMode", Flags)!.Invoke(window, null);
            else
                await window.OpenPathAsync(next);
            await saving;
            Assert.AreEqual(0, riskPrompts);
            Assert.IsFalse(Field<bool>(window, "_isApplyingRotation"));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }
        finally
        {
            if (gateHeld) MagickRuntime.OperationGate.Release();
            if (saving is not null) await saving;
            typeof(MainWindow).GetMethod("ClearForMissingFile", Flags)!.Invoke(window, null);
            directory.Delete(recursive: true);
        }
    });

    [TestMethod]
    public Task Declining_Confirmation_Releases_The_Guard_And_Keeps_The_Preview() => StaTest.RunAsync(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var window = new MainWindow(new AppSettings { IsPreloadEnabled = false, KeepReadyInBackground = false });
        try
        {
            using (var bitmap = new SKBitmap(16, 24))
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, data.ToArray());
            await window.OpenPathAsync(path);
            typeof(MainWindow).GetMethod("RotatePreview", Flags)!.Invoke(window, [1]);
            await window.ApplyRotationWithPromptsAsync(_ => false,
                _ => throw new AssertFailedException("Risk prompt must not run"), _ => false);
            Assert.IsFalse(Field<bool>(window, "_isApplyingRotation"));
            Assert.AreEqual(1, Field<int>(window, "_pendingRotationQuarterTurns"));
        }
        finally
        {
            typeof(MainWindow).GetMethod("ClearForMissingFile", Flags)!.Invoke(window, null);
            File.Delete(path);
        }
    });

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Flags)!.GetValue(window)!;
}
