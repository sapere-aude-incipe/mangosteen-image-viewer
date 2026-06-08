using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;

namespace ClassicPhotoViewer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Length > 0)
        {
            await OpenStartupPathAsync(window, e.Args[0]);
        }
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.WriteLine($"Unhandled UI exception: {e.Exception}");
        if (Current.MainWindow is MainWindow window)
        {
            window.ReportError(e.Exception.Message);
            e.Handled = true;
        }
    }

    private static async Task OpenStartupPathAsync(MainWindow window, string path)
    {
        try
        {
            await window.OpenPathAsync(path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Startup open failed: {ex}");
            window.ReportError(ex.Message);
        }
    }
}
