using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Mangosteen;

public partial class App : Application
{
    private readonly StartupLaunchOptions _launch;
    private AppInstanceCoordinator? _instanceCoordinator;
    private MainWindow? _mainWindow;
    private bool _isExiting;

    // Program.Main has already parsed the arguments and won the instance mutex;
    // secondary launches are forwarded there without constructing App.
    internal App(StartupLaunchOptions launch, AppInstanceCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(coordinator);
        if (!coordinator.IsPrimaryInstance)
        {
            throw new ArgumentException("App is only constructed for the primary instance.", nameof(coordinator));
        }

        _launch = launch;
        _instanceCoordinator = coordinator;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var launch = _launch;
        var coordinator = _instanceCoordinator!;
        StartupDiagnostics.Mark("app.startup.begin", launch.FilePath is null ? null : System.IO.Path.GetFileName(launch.FilePath));
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        if (launch.RequestShutdown)
        {
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        _ = StartupRegistration.TrySetEnabled(settings.KeepReadyInBackground, out _, replaceExisting: false);
        if (launch.IsBackgroundLaunch && !settings.KeepReadyInBackground)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow(settings);
        StartupDiagnostics.Mark("app.main_window.constructed");
        _mainWindow = window;
        MainWindow = window;
        window.ApplicationExitStarting += MainWindow_ApplicationExitStarting;
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

    // The sender is told whether the request was taken, so one refused here (while
    // exiting) is retried by that process as the new primary instead of being lost.
    // A shutdown request is satisfied by an exit that is already under way.
    private async Task<bool> HandleActivationRequestAsync(AppActivationRequest request)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return request.RequestShutdown;
        }

        try
        {
            return await Dispatcher.InvokeAsync(
                    () => QueueActivationRequest(request),
                    DispatcherPriority.Send)
                .Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return request.RequestShutdown;
        }
    }

    private bool QueueActivationRequest(AppActivationRequest request)
    {
        if (_mainWindow is null || _isExiting)
        {
            return request.RequestShutdown;
        }

        if (request.RequestShutdown)
        {
            _mainWindow.RequestApplicationExit();
            return true;
        }

        _ = HandleActivationRequestOnUiAsync(request);
        return true;
    }

    private void MainWindow_ApplicationExitStarting(object? sender, EventArgs e)
    {
        // Give up the instance lock as soon as exit begins so a file opened during
        // shutdown starts a fresh instance rather than reaching this closing one.
        _isExiting = true;
        _instanceCoordinator?.ReleasePrimary();
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
