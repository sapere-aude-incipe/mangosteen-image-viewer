using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;

namespace Mangosteen;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        StartupDiagnostics.Mark("app.startup.begin", e.Args.Length > 0 ? System.IO.Path.GetFileName(e.Args[0]) : null);
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        var window = new MainWindow();
        StartupDiagnostics.Mark("app.main_window.constructed");
        MainWindow = window;
        window.Show();
        StartupDiagnostics.Mark("app.main_window.show_returned");

        if (e.Args.Length > 0)
        {
            StartupDiagnostics.Mark("app.startup.open_begin", System.IO.Path.GetFileName(e.Args[0]));
            await OpenStartupPathAsync(window, e.Args[0]);
            StartupDiagnostics.Mark("app.startup.open_returned", System.IO.Path.GetFileName(e.Args[0]));
        }

        StartupDiagnostics.Mark("app.startup.end");
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
