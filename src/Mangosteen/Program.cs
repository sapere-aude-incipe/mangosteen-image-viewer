using System.Runtime.CompilerServices;

namespace Mangosteen;

/// <summary>
/// Custom entry point. A second launch (an Explorer double-click while the ready
/// shell runs) only parses arguments, checks the instance mutex and forwards the
/// path over the pipe, without loading WPF. Only the primary instance builds App.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var launch = StartupLaunchOptions.Parse(args);
        StartupDiagnostics.Mark("program.main.begin", launch.FilePath is null ? null : System.IO.Path.GetFileName(launch.FilePath));

        var coordinator = new AppInstanceCoordinator();
        StartupDiagnostics.Mark(coordinator.IsPrimaryInstance ? "app.instance.primary" : "app.instance.secondary");
        if (!coordinator.IsPrimaryInstance)
        {
            var (primary, forwarded) = ActivationHandoff.ForwardOrTakeOver(coordinator, launch.ToActivationRequest());
            if (primary is null)
            {
                StartupDiagnostics.Mark(forwarded ? "app.instance.forwarded" : "app.instance.forward_failed");
                return SecondaryLaunch.Complete(launch, forwarded, ShowRunningInstanceUnavailable);
            }

            // The running instance refused because it is exiting; this launch takes over.
            coordinator = primary;
            StartupDiagnostics.Mark("app.instance.primary_after_handoff");
        }

        return RunPrimary(launch, coordinator);
    }

    // Kept out of Main so the secondary path never resolves WPF types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunPrimary(StartupLaunchOptions launch, AppInstanceCoordinator coordinator)
    {
        var app = new App(launch, coordinator);
        app.InitializeComponent();
        return app.Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowRunningInstanceUnavailable()
    {
        System.Windows.MessageBox.Show(
            Localization.LocalizedText.Get(Localization.LocalizedText.RunningInstanceUnavailable),
            Localization.LocalizedText.Get(Localization.LocalizedText.AppTitle),
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }
}

internal static class ActivationHandoff
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandoffRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Sends the request to the running instance. If that instance refuses because it is
    /// exiting (it releases the instance lock first), retries until this process can
    /// become the primary instance itself, so the file is never silently dropped.
    /// Disposes <paramref name="secondary"/>; a returned primary is owned by the caller.
    /// Runs synchronously on the calling thread: the instance mutex is thread-affine, so a
    /// coordinator that becomes primary must be created on the thread that will run App.
    /// </summary>
    public static (AppInstanceCoordinator? Primary, bool Forwarded) ForwardOrTakeOver(
        AppInstanceCoordinator secondary,
        AppActivationRequest request)
    {
        return ForwardOrTakeOver(secondary, request, static () => new AppInstanceCoordinator());
    }

    internal static (AppInstanceCoordinator? Primary, bool Forwarded) ForwardOrTakeOver(
        AppInstanceCoordinator secondary,
        AppActivationRequest request,
        Func<AppInstanceCoordinator> createCoordinator)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var coordinator = secondary;
        while (true)
        {
            // No synchronization context exists yet, and the client uses
            // ConfigureAwait(false), so blocking here cannot deadlock.
            var result = coordinator.TrySendAsync(request).GetAwaiter().GetResult();
            coordinator.Dispose();
            if (result == ActivationSendResult.Delivered)
            {
                return (null, true);
            }

            // A refusal means the running instance is exiting and has released (or is
            // about to release) the instance lock; a failure may mean the same.
            if (result == ActivationSendResult.Rejected)
            {
                Thread.Sleep(HandoffRetryDelay);
            }

            coordinator = createCoordinator();
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
}

internal static class SecondaryLaunch
{
    public const int ForwardedExitCode = 0;
    public const int NotForwardedExitCode = 1;

    /// <summary>
    /// Returns the exit code for a forwarded launch. Only an interactive launch
    /// reports a failure; --background and --shutdown stay silent.
    /// </summary>
    public static int Complete(StartupLaunchOptions launch, bool forwarded, Action reportFailure)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(reportFailure);

        if (!forwarded && ShouldReportFailure(launch))
        {
            reportFailure();
        }

        return forwarded ? ForwardedExitCode : NotForwardedExitCode;
    }

    public static bool ShouldReportFailure(StartupLaunchOptions launch) =>
        !launch.IsBackgroundLaunch && !launch.RequestShutdown;
}
