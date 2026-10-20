using Mangosteen.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Mangosteen;

public partial class App : Application
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandoffRetryDelay = TimeSpan.FromMilliseconds(100);

    private AppInstanceCoordinator? _instanceCoordinator;
    private MainWindow? _mainWindow;
    private bool _isExiting;

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
            var (primary, forwarded) = await ForwardOrTakeOverAsync(coordinator, launch.ToActivationRequest());
            if (primary is null)
            {
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

            coordinator = primary;
            StartupDiagnostics.Mark("app.instance.primary_after_handoff");
        }

        _instanceCoordinator = coordinator;
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

    private static async Task<(AppInstanceCoordinator? Primary, bool Forwarded)> ForwardOrTakeOverAsync(
        AppInstanceCoordinator secondary,
        AppActivationRequest request)
    {
        var elapsed = Stopwatch.StartNew();
        var coordinator = secondary;
        while (true)
        {
            var result = await coordinator.TrySendAsync(request);
            coordinator.Dispose();
            if (result == ActivationSendResult.Delivered)
            {
                return (null, true);
            }

            // A refusal means the running instance is exiting and has released (or is
            // about to release) the instance lock; a failure may mean the same.
            if (result == ActivationSendResult.Rejected)
            {
                await Task.Delay(HandoffRetryDelay);
            }

            coordinator = new AppInstanceCoordinator();
            if (coordinator.IsPrimaryInstance)
            {
                return (coordinator, false);
            }

            if (result == ActivationSendResult.Failed || elapsed.Elapsed > HandoffTimeout)
            {
                coordinator.Dispose();
                return (null, false);
            }
        }
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
