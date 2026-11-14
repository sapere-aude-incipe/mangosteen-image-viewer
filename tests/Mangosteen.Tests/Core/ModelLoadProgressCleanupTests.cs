using System.Reflection;
using System.Windows.Threading;

namespace Mangosteen.Tests.Core;

[TestClass]
[DoNotParallelize] // WPF WindowChrome uses process-wide property-descriptor caches.
public sealed class ModelLoadProgressCleanupTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public Task Clearing_The_Viewer_During_A_Model_Load_Stops_The_Progress_Timer() => StaTest.RunAsync(() =>
    {
        // While a model loads the content mode is still Image, so ClearImage (window parked,
        // file deleted, folder emptied) must stop the progress display itself.
        var window = new MainWindow(new AppSettings { KeepReadyInBackground = false, IsPreloadEnabled = false });
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Start();
        Field("_modelLoadProgressTimer").SetValue(window, timer);
        Field("_isModelLoadProgressVisible").SetValue(window, true);

        typeof(MainWindow).GetMethod("ClearImage", Flags)!.Invoke(window, null);

        Assert.IsFalse(timer.IsEnabled);
        Assert.IsNull(Field("_modelLoadProgressTimer").GetValue(window));
        Assert.IsFalse((bool)Field("_isModelLoadProgressVisible").GetValue(window)!);
        return Task.CompletedTask;
    });

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, Flags)!;
}
