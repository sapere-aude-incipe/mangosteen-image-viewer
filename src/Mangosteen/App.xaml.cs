using Mangosteen.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Mangosteen;

public partial class App : Application
{
    private AppInstanceCoordinator? _instanceCoordinator;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var launch = StartupLaunchOptions.Parse(e.Args);
        StartupDiagnostics.Mark("app.startup.begin", launch.FilePath is null ? null : System.IO.Path.GetFileName(launch.FilePath));
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        var coordinator = new AppInstanceCoordinator();
        StartupDiagnostics.Mark(coordinator.IsPrimaryInstance ? "app.instance.primary" : "app.instance.secondary");
        if (!coordinator.IsPrimaryInstance)
        {
            var forwarded = await coordinator.TrySendAsync(launch.ToActivationRequest());
            coordinator.Dispose();

            if (!forwarded && !launch.IsBackgroundLaunch && !launch.RequestShutdown)
            {
                MessageBox.Show(
                    LocalizedText.Get(LocalizedText.RunningInstanceUnavailable),
                    LocalizedText.Get(LocalizedText.AppTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown(forwarded ? 0 : 1);
            return;
        }

        _instanceCoordinator = coordinator;
        if (launch.RequestShutdown)
        {
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        _ = StartupRegistration.TrySetEnabled(settings.KeepReadyInBackground, out _);
        if (launch.IsBackgroundLaunch && !settings.KeepReadyInBackground)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow(settings);
        StartupDiagnostics.Mark("app.main_window.constructed");
        _mainWindow = window;
        MainWindow = window;
        SessionEnding += App_SessionEnding;
        coordinator.StartServer(HandleActivationRequestAsync);

        if (launch.IsBackgroundLaunch && launch.FilePath is null)
        {
            await window.WarmForBackgroundAsync();
            StartupDiagnostics.Mark("app.background_ready");
        }
        else
        {
            window.Show();
            StartupDiagnostics.Mark("app.main_window.show_returned");

            if (launch.FilePath is not null)
            {
                StartupDiagnostics.Mark("app.startup.open_begin", System.IO.Path.GetFileName(launch.FilePath));
                await OpenStartupPathAsync(window, launch.FilePath);
                StartupDiagnostics.Mark("app.startup.open_returned", System.IO.Path.GetFileName(launch.FilePath));
            }
        }

        StartupDiagnostics.Mark("app.startup.end");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SessionEnding -= App_SessionEnding;
        _instanceCoordinator?.Dispose();
        _instanceCoordinator = null;
        _mainWindow = null;
        base.OnExit(e);
    }

    private Task HandleActivationRequestAsync(AppActivationRequest request)
    {
        if (_mainWindow is null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(
                () => QueueActivationRequest(request),
                DispatcherPriority.Send)
            .Task;
    }

    private void QueueActivationRequest(AppActivationRequest request)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (request.RequestShutdown)
        {
            _mainWindow.RequestApplicationExit();
            return;
        }

        _ = HandleActivationRequestOnUiAsync(request);
    }

    private async Task HandleActivationRequestOnUiAsync(AppActivationRequest request)
    {
        try
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.HandleActivationAsync(request);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Activation request failed: {ex}");
            _mainWindow?.ReportError(ex.Message);
        }
    }

    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _mainWindow?.RequestApplicationExit();
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
