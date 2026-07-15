using Mangosteen.Caching;
using Mangosteen.Core;
using Mangosteen.Decoding;
using Mangosteen.Dialogs;
using Mangosteen.Editing;
using Mangosteen.Icons;
using Mangosteen.Localization;
using Mangosteen.Navigation;
using Mangosteen.Rendering;
using Mangosteen.Updates;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using WpfPoint = System.Windows.Point;

namespace Mangosteen;

internal enum PreloadAggressiveness
{
    Conservative,
    Balanced,
    Aggressive
}

internal readonly record struct PreloadProfile(
    int ForwardCount,
    int BackwardCount,
    int FullPreloadLikelyPathLimit,
    int SmallFullPreloadCount,
    int FullPreloadIdleDelayMilliseconds,
    int LargeFullPreloadLimitAdjustment);

internal readonly record struct WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height);

public partial class MainWindow : Window
{
    private const int BalancedForwardPreloadCount = 50;
    private const int BalancedBackwardPreloadCount = 10;
    private const int BalancedFullPreloadLikelyPathLimit = 4;
    private const int BalancedSmallFullPreloadCount = 5;
    private const long SmallFullPreloadLimitBytes = 128L * ImageMemoryEstimator.Megabyte;
    private const long LargeImageThresholdBytes = 512L * ImageMemoryEstimator.Megabyte;
    private const long AutoFullDecodeLimitBytes = 512L * ImageMemoryEstimator.Megabyte;
    private const long CleanupThresholdBytes = 512L * ImageMemoryEstimator.Megabyte;
    private const int BalancedFullPreloadIdleDelayMilliseconds = 1_200;
    private const int DefaultPreloadBudgetGigabytes = 2;
    private const double DefaultTopChromeHeightDips = 48.0;
    private const double DefaultToolbarHeightDips = 48.0;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int MonitorDefaultToNearest = 2;
    private const int SpiSetDesktopWallpaper = 0x0014;
    private const int SpifUpdateIniFile = 0x0001;
    private const int SpifSendChange = 0x0002;
    private const uint ShopFilePath = 0x00000002;
    private const int WmGetMinMaxInfo = 0x0024;
    private const double EmptyStateMinimumWidth = 280.0;
    private const double EmptyStateMaximumWidth = 500.0;
    private const double EmptyStateHorizontalMargin = 240.0;
    private const double ZoomSliderMinimumValue = 0.0;
    private const double ZoomSliderMaximumValue = 100.0;
    private const double MinimumSliderZoom = 0.0001;
    private const double ActualPixelZoomTolerance = 0.0001;
    private const double InitialWorkAreaCoverage = 0.86;
    private const string MaximizeWindowIconGlyph = "\uE922";
    private const string RestoreWindowIconGlyph = "\uE923";
    private static readonly SKColor LightViewerBackground = new(244, 246, 248);
    private static readonly SKColor DarkViewerBackground = new(30, 33, 38);

    private readonly Lazy<DecoderRegistry> _decoders = new(DecoderRegistry.CreateDefault);
    private readonly ImageNavigator _navigator = new();
    private readonly ViewerState _viewerState = new();
    private readonly DispatcherTimer _animationTimer = new();
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ImagePreloadCache _fullPreloadCache = new();
    private readonly ImagePreloadCache _previewPreloadCache = new();
    private readonly Lazy<GitHubUpdateService> _updates = new(static () => new GitHubUpdateService());
    private readonly ImageRotationService _rotationService = new();
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };
    private readonly object _preloadWorkerGate = new();
    private readonly object _backgroundFullWarmupGate = new();
    private readonly HashSet<string> _backgroundFullWarmups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _backgroundFullWarmupTasks = [];
    private readonly SemaphoreSlim _previewDecodeGate = new(1, 1);
    private readonly SemaphoreSlim _fullDecodeGate = new(1, 1);
    private LoadSession? _loadSession;
    private CancellationTokenSource? _folderIndexCts;
    private CancellationTokenSource? _updateCheckCts;
    private CancellationTokenSource? _rotationSaveCts;
    private CancellationTokenSource _preloadCts = new();
    private CancellationTokenSource _backgroundFullWarmupCts = new();
    private Task _preloadWorkerTask = Task.CompletedTask;
    private DecodedImage? _image;
    private int _frameIndex;
    private int _loadGeneration;
    private int _openGeneration;
    private int _fullDecodeTaskCount;
    private int _pendingRotationQuarterTurns;
    private bool _isClosing;
    private bool _isPanning;
    private bool _isCurrentPreviewAwaitingFullResolution;
    private bool _setActualPixelsAfterFullLoad;
    private bool _useSmoothSampling = true;
    private bool _isPreloadEnabled = true;
    private bool _isAutoRefreshEnabled;
    private bool _keepReadyInBackground = true;
    private bool _isUpdatingZoomSlider;
    private bool _isDarkMode = true;
    private bool _isAutoRefreshReloading;
    private bool _isApplyingRotation;
    private bool _exitRequested;
    private bool _isBackgroundWarmup;
    private bool _activationRequestedDuringWarmup;
    private int _preloadBudgetGigabytes = DefaultPreloadBudgetGigabytes;
    private PreloadAggressiveness _preloadAggressiveness = PreloadAggressiveness.Balanced;
    private SKColor _viewerBackgroundColor = DarkViewerBackground;
    private SKPoint _lastPanPoint;
    private HwndSource? _hwndSource;
    private FileSystemWatcher? _autoRefreshWatcher;
    private string? _autoRefreshPath;
    private ToolbarIconKind? _actualPixelsIconKind;
    private Brush? _actualPixelsIconBrush;
    private bool? _actualPixelsIconEnabled;

    private DecoderRegistry Decoders => _decoders.Value;

    private GitHubUpdateService Updates => _updates.Value;

    public MainWindow()
        : this(AppSettings.Load())
    {
    }

    internal MainWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StartupDiagnostics.Mark("window.ctor.begin");
        InitializeComponent();
        StartupDiagnostics.Mark("window.initialize_component.end");
        ContentRendered += Window_ContentRendered;
        ApplyInitialWindowPlacement();
        StartupDiagnostics.Mark("window.initial_placement_applied");
        StartupDiagnostics.Mark("window.settings_loaded");
        ApplySettings(settings);
        StartupDiagnostics.Mark("window.settings_applied");
        ApplyLocalization();
        StartupDiagnostics.Mark("window.localization_applied");
        UpdateViewport();
        UpdateNavigationButtons();
        ApplyPreloadCacheBudget();
        UpdateActualPixelsIcon();
        UpdateToolbarDensity();
        UpdateMaximizeRestoreButton();

        _animationTimer.Tick += AnimationTimer_Tick;
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        StartupDiagnostics.Mark("window.ctor.end");
    }

    private void ApplyInitialWindowPlacement()
    {
        var workArea = SystemParameters.WorkArea;
        var placement = CalculateInitialWindowPlacement(
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            MinWidth,
            MinHeight,
            Width,
            Height);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = placement.Width;
        Height = placement.Height;
        Left = placement.Left;
        Top = placement.Top;
    }

    private void ApplySettings(AppSettings settings)
    {
        _useSmoothSampling = settings.UseSmoothSampling;
        _isPreloadEnabled = settings.IsPreloadEnabled;
        _isAutoRefreshEnabled = settings.IsAutoRefreshEnabled;
        _keepReadyInBackground = settings.KeepReadyInBackground;
        _preloadBudgetGigabytes = Math.Clamp(settings.PreloadBudgetGigabytes, 1, 15);
        _preloadAggressiveness = settings.PreloadAggressiveness;
        if (settings.IsDarkMode == _isDarkMode)
        {
            DarkModeMenuItem.IsChecked = _isDarkMode;
            DarkModeMenuItem.ToolTip = _isDarkMode
                ? LocalizedText.Get(LocalizedText.UseLightMode)
                : LocalizedText.Get(LocalizedText.UseDarkMode);
            UpdateToolbarIcons();
        }
        else
        {
            ApplyTheme(settings.IsDarkMode);
        }
    }

    private void SaveSettings()
    {
        new AppSettings
        {
            IsDarkMode = _isDarkMode,
            UseSmoothSampling = _useSmoothSampling,
            IsPreloadEnabled = _isPreloadEnabled,
            IsAutoRefreshEnabled = _isAutoRefreshEnabled,
            KeepReadyInBackground = _keepReadyInBackground,
            PreloadBudgetGigabytes = _preloadBudgetGigabytes,
            PreloadAggressiveness = _preloadAggressiveness
        }.Save();
    }

    private void ApplyLocalization()
    {
        UpdateWindowTitle(_navigator.CurrentPath is null ? null : Path.GetFileName(_navigator.CurrentPath));
        FileMenuItem.Header = LocalizedText.Get(LocalizedText.FileMenu);
        OpenMenuItem.Header = LocalizedText.Get(LocalizedText.OpenCommand);
        DeleteMenuItem.Header = LocalizedText.Get(LocalizedText.DeleteImage);
        ExitMenuItem.Header = LocalizedText.Get(LocalizedText.Exit);
        OptionsMenuItem.Header = LocalizedText.Get(LocalizedText.OptionsMenu);
        HelpMenuItem.Header = LocalizedText.Get(LocalizedText.HelpMenu);
        CheckForUpdatesMenuItem.Header = LocalizedText.Get(LocalizedText.CheckForUpdates);
        var cancelUpdateText = LocalizedText.Get(LocalizedText.CancelUpdate);
        CancelUpdateButton.Content = cancelUpdateText;
        AutomationProperties.SetName(CancelUpdateButton, cancelUpdateText);
        SamplingMenuItem.Header = LocalizedText.Get(LocalizedText.Upscaling);
        SmoothSamplingMenuItem.Header = LocalizedText.Get(LocalizedText.Smooth);
        NearestSamplingMenuItem.Header = LocalizedText.Get(LocalizedText.Nearest);
        PreloadEnabledMenuItem.Header = LocalizedText.Get(LocalizedText.PreloadNearbyImages);
        PreloadMemoryBudgetMenuItem.Header = LocalizedText.Get(LocalizedText.PreloadMemoryBudget);
        PreloadAggressivenessMenuItem.Header = LocalizedText.Get(LocalizedText.PreloadAggressiveness);
        ConservativePreloadMenuItem.Header = LocalizedText.Get(LocalizedText.Conservative);
        BalancedPreloadMenuItem.Header = LocalizedText.Get(LocalizedText.Balanced);
        AggressivePreloadMenuItem.Header = LocalizedText.Get(LocalizedText.Aggressive);
        AutoRefreshMenuItem.Header = LocalizedText.Get(LocalizedText.AutoRefreshCurrentImage);
        KeepReadyInBackgroundMenuItem.Header = LocalizedText.Get(LocalizedText.KeepReadyInBackground);
        DarkModeMenuItem.Header = LocalizedText.Get(LocalizedText.ToggleDarkMode);
        OptionsHelpMenuItem.Header = LocalizedText.Get(LocalizedText.OptionsHelp);
        SamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.UpscalingTooltip);
        SmoothSamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.SmoothUpscalingTooltip);
        NearestSamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.NearestUpscalingTooltip);
        PreloadEnabledMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadNearbyImagesTooltip);
        PreloadMemoryBudgetMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadMemoryBudgetTooltip);
        PreloadAggressivenessMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadAggressivenessTooltip);
        AutoRefreshMenuItem.ToolTip = LocalizedText.Get(LocalizedText.AutoRefreshCurrentImageTooltip);
        KeepReadyInBackgroundMenuItem.ToolTip = LocalizedText.Get(LocalizedText.KeepReadyInBackgroundTooltip);
        ConservativePreloadMenuItem.ToolTip = LocalizedText.Get(LocalizedText.ConservativePreloadTooltip);
        BalancedPreloadMenuItem.ToolTip = LocalizedText.Get(LocalizedText.BalancedPreloadTooltip);
        AggressivePreloadMenuItem.ToolTip = LocalizedText.Get(LocalizedText.AggressivePreloadTooltip);
        CheckForUpdatesMenuItem.ToolTip = LocalizedText.Get(LocalizedText.CheckForUpdatesTooltip);
        OptionsHelpMenuItem.ToolTip = LocalizedText.Get(LocalizedText.OptionsHelpTooltip);
        foreach (var item in PreloadMemoryBudgetMenuItem.Items.OfType<MenuItem>())
        {
            item.ToolTip = LocalizedText.Get(LocalizedText.PreloadMemoryBudgetOptionTooltip);
        }

        if (_image is null)
        {
            ShowStatus(LocalizedText.Get(LocalizedText.NoImage));
        }
        UpdateStatusOverlayOpenState();

        PreviewOnlyText.Text = LocalizedText.Get(LocalizedText.PreviewOnly);
        DarkModeMenuItem.ToolTip = DarkModeMenuItem.IsChecked
            ? LocalizedText.Get(LocalizedText.UseLightMode)
            : LocalizedText.Get(LocalizedText.UseDarkMode);
        var previousImageText = LocalizedText.Get(LocalizedText.PreviousImage);
        var nextImageText = LocalizedText.Get(LocalizedText.NextImage);
        var actualPixelsText = LocalizedText.Get(LocalizedText.ToggleActualPixels);
        var zoomText = LocalizedText.Get(LocalizedText.Zoom);
        var showInFolderText = LocalizedText.Get(LocalizedText.ShowImageInFolder);
        var deleteImageText = LocalizedText.Get(LocalizedText.DeleteImage);
        var rotateLeftText = LocalizedText.Get(LocalizedText.RotateLeft);
        var rotateRightText = LocalizedText.Get(LocalizedText.RotateRight);
        PreviousButton.ToolTip = $"{previousImageText} (←)";
        NextButton.ToolTip = $"{nextImageText} (→)";
        ActualPixelsButton.ToolTip = $"{actualPixelsText} (1 / F)";
        ZoomPopupButton.ToolTip = zoomText;
        ShowInFolderButton.ToolTip = showInFolderText;
        DeleteButton.ToolTip = $"{deleteImageText} (Del)";
        RotateLeftButton.ToolTip = rotateLeftText;
        RotateRightButton.ToolTip = rotateRightText;
        ResetRotationButton.Content = LocalizedText.Get(LocalizedText.ResetRotation);
        ApplyRotationButton.Content = LocalizedText.Get(LocalizedText.ApplyRotation);
        AutomationProperties.SetName(PreviousButton, previousImageText);
        AutomationProperties.SetName(NextButton, nextImageText);
        AutomationProperties.SetName(ActualPixelsButton, actualPixelsText);
        AutomationProperties.SetName(ZoomPopupButton, zoomText);
        AutomationProperties.SetName(ShowInFolderButton, showInFolderText);
        AutomationProperties.SetName(DeleteButton, deleteImageText);
        AutomationProperties.SetName(RotateLeftButton, rotateLeftText);
        AutomationProperties.SetName(RotateRightButton, rotateRightText);
        AutomationProperties.SetName(ResetRotationButton, LocalizedText.Get(LocalizedText.ResetRotation));
        AutomationProperties.SetName(ApplyRotationButton, LocalizedText.Get(LocalizedText.ApplyRotation));
        ContextOpenWithMenuItem.Header = LocalizedText.Get(LocalizedText.OpenWith);
        ContextSetDesktopBackgroundMenuItem.Header = LocalizedText.Get(LocalizedText.SetAsDesktopBackground);
        ContextOpenFileLocationMenuItem.Header = LocalizedText.Get(LocalizedText.OpenFileLocation);
        ContextCopyMenuItem.Header = LocalizedText.Get(LocalizedText.Copy);
        ContextDeleteMenuItem.Header = LocalizedText.Get(LocalizedText.DeleteImage);
        ContextPropertiesMenuItem.Header = LocalizedText.Get(LocalizedText.Properties);
        ZoomText.ToolTip = zoomText;
        ZoomSlider.ToolTip = zoomText;
        AutomationProperties.SetName(ZoomSlider, zoomText);
        OpenMenuItem.InputGestureText = "Ctrl+O";
        DeleteMenuItem.InputGestureText = "Del";
        ContextDeleteMenuItem.InputGestureText = "Del";
        UpdateSettingsMenuChecks();
        UpdateZoomText();
        UpdateRotationControls();
    }

    private void OptionsHelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            LocalizedText.Get(LocalizedText.OptionsHelpDialogText),
            LocalizedText.Get(LocalizedText.OptionsHelpDialogTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void CheckForUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCheckCts is not null)
        {
            return;
        }

        var currentVersion = GetCurrentReleaseVersion();
        var updateCheckCts = new CancellationTokenSource();
        _updateCheckCts = updateCheckCts;
        CheckForUpdatesMenuItem.IsEnabled = false;

        try
        {
            ShowStatus(LocalizedText.Get(LocalizedText.CheckingForUpdates));
            var update = await Updates.CheckLatestReleaseAsync(currentVersion, updateCheckCts.Token);
            if (!update.IsUpdateAvailable)
            {
                MessageBox.Show(
                    this,
                    LocalizedText.Format(LocalizedText.NoUpdatesAvailableFormat, currentVersion),
                    LocalizedText.Get(LocalizedText.UpdatesDialogTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!IsInstalledBuild())
            {
                var portableChoice = MessageBox.Show(
                    this,
                    LocalizedText.Format(
                        LocalizedText.UpdateAvailablePortableFormat,
                        update.LatestVersion,
                        update.CurrentVersion),
                    LocalizedText.Get(LocalizedText.UpdatesDialogTitle),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (portableChoice == MessageBoxResult.Yes)
                {
                    OpenExternalUrl(update.ReleasePageUrl);
                }

                return;
            }

            if (update.InstallerAsset is null)
            {
                MessageBox.Show(
                    this,
                    LocalizedText.Get(LocalizedText.UpdateInstallerMissing),
                    LocalizedText.Get(LocalizedText.UpdatesDialogTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                OpenExternalUrl(update.ReleasePageUrl);
                return;
            }

            var installedChoice = MessageBox.Show(
                this,
                LocalizedText.Format(
                    LocalizedText.UpdateAvailableInstalledFormat,
                    update.LatestVersion,
                    update.CurrentVersion),
                LocalizedText.Get(LocalizedText.UpdatesDialogTitle),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (installedChoice != MessageBoxResult.Yes)
            {
                return;
            }

            ShowStatus(LocalizedText.Get(LocalizedText.DownloadingUpdate));
            CancelUpdateButton.IsEnabled = true;
            ShowUpdateDownloadProgress(default);
            var isDownloadActive = true;
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (isDownloadActive)
                {
                    ShowUpdateDownloadProgress(value);
                }
            });

            string installerPath;
            try
            {
                installerPath = await Updates.DownloadInstallerAsync(
                    update,
                    GetUpdateDownloadDirectory(update.LatestVersion),
                    progress,
                    updateCheckCts.Token);
            }
            finally
            {
                isDownloadActive = false;
            }

            updateCheckCts.Token.ThrowIfCancellationRequested();
            ShowStatus(LocalizedText.Get(LocalizedText.StartingInstaller));
            Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true
            });
            RequestApplicationExit();
        }
        catch (OperationCanceledException) when (_isClosing || updateCheckCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TraceBackgroundError("Failed to check for updates", ex);
            MessageBox.Show(
                this,
                LocalizedText.Format(LocalizedText.UpdateCheckFailedFormat, ex.Message),
                LocalizedText.Get(LocalizedText.UpdatesDialogTitle),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_updateCheckCts, updateCheckCts))
            {
                _updateCheckCts = null;
            }

            updateCheckCts.Dispose();
            if (!_isClosing)
            {
                RestoreStatusAfterUpdateCheck();
                CheckForUpdatesMenuItem.IsEnabled = true;
            }
        }
    }

    private void CancelUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCheckCts is null || _updateCheckCts.IsCancellationRequested)
        {
            return;
        }

        CancelUpdateButton.IsEnabled = false;
        _updateCheckCts.Cancel();
    }

    private void UpdateWindowTitle(string? fileName = null)
    {
        var appTitle = LocalizedText.Get(LocalizedText.AppTitle);
        var hasFileName = !string.IsNullOrWhiteSpace(fileName);
        var chromeTitle = hasFileName ? fileName! : appTitle;
        Title = hasFileName ? $"{fileName} - {appTitle}" : appTitle;
        ChromeTitleText.Text = chromeTitle;
        ChromeTitleText.ToolTip = Title;
    }

    public async Task OpenPathAsync(string path)
    {
        StartupDiagnostics.Mark("open_path.begin", Path.GetFileName(path));
        if (_isClosing)
        {
            return;
        }

        if (!File.Exists(path))
        {
            ClearForMissingFile();
            ShowStatus(LocalizedText.Format(LocalizedText.FileNotFoundFormat, path));
            return;
        }

        var fullPath = Path.GetFullPath(path);
        DiscardPendingRotation(refitImage: false);
        var openGeneration = ++_openGeneration;
        _navigator.LoadSingle(fullPath);
        StartupDiagnostics.Mark("open_path.navigator_loaded", Path.GetFileName(fullPath));
        CancelPreloadWorker();
        CancelFolderIndexing();
        CancelBackgroundFullWarmups();
        ClearPreloadCaches();
        UpdateNavigationButtons();
        await LoadCurrentImageAsync(fitToWindow: true);
        QueueFolderIndexingAfterFirstPreview(fullPath, openGeneration);
        StartupDiagnostics.Mark("open_path.end", Path.GetFileName(fullPath));
    }

    public void ReportError(string message)
    {
        if (_isClosing)
        {
            return;
        }

        ShowStatus(string.IsNullOrWhiteSpace(message) ? LocalizedText.Get(LocalizedText.UnexpectedError) : message);
    }

    internal async Task WarmForBackgroundAsync()
    {
        if (_isClosing)
        {
            return;
        }

        _isBackgroundWarmup = true;
        _activationRequestedDuringWarmup = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        Opacity = 0;
        IsHitTestVisible = false;
        Show();

        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
        }
        finally
        {
            _isBackgroundWarmup = false;
            IsHitTestVisible = true;
            Opacity = 1;
            ShowActivated = true;

            if (_activationRequestedDuringWarmup)
            {
                ShowInTaskbar = true;
                BringWindowToForeground();
            }
            else
            {
                Hide();
                ShowInTaskbar = false;
            }
        }
    }

    internal async Task HandleActivationAsync(AppActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestShutdown)
        {
            RequestApplicationExit();
            return;
        }

        if (!request.Activate || _isClosing)
        {
            return;
        }

        _activationRequestedDuringWarmup = true;
        IsHitTestVisible = true;
        Opacity = 1;
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        BringWindowToForeground();
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            await OpenPathAsync(request.FilePath);
            BringWindowToForeground();
        }
    }

    internal void RequestApplicationExit()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var dispatcherIsShuttingDown = Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished;
        if (ShouldHideOnClose(_keepReadyInBackground, _exitRequested, dispatcherIsShuttingDown))
        {
            e.Cancel = true;
            EnterBackgroundMode();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        SaveSettings();
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
        DisposeAutoRefreshWatcher();
        _animationTimer.Stop();
        _animationTimer.Tick -= AnimationTimer_Tick;
        StopPanning();
        _loadSession?.CancelAndDisposeWhenInactive();
        _loadSession = null;
        CancelUpdateCheck();
        _rotationSaveCts?.Cancel();
        _rotationSaveCts = null;
        CancelFolderIndexing();
        CancelPreloadWorker(replaceToken: false);
        CancelBackgroundFullWarmups(replaceToken: false);
        _fullPreloadCache.Dispose();
        _previewPreloadCache.Dispose();
        _image?.Dispose();
        _image = null;
        _imagePaint.Dispose();
        if (_decoders.IsValueCreated)
        {
            _decoders.Value.Dispose();
        }

        if (_updates.IsValueCreated)
        {
            _updates.Value.Dispose();
        }
        base.OnClosed(e);
    }

    internal static bool ShouldHideOnClose(
        bool keepReadyInBackground,
        bool exitRequested,
        bool dispatcherIsShuttingDown)
    {
        return keepReadyInBackground && !exitRequested && !dispatcherIsShuttingDown;
    }

    private void EnterBackgroundMode()
    {
        if (_isClosing || _isBackgroundWarmup)
        {
            return;
        }

        ZoomPopup.IsOpen = false;
        DiscardPendingRotation(refitImage: false);
        ClearForMissingFile();
        ShowStatus(LocalizedText.Get(LocalizedText.NoImage));
        Hide();
        ShowInTaskbar = false;
    }

    private void BringWindowToForeground()
    {
        _ = Activate();
        Focus();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            _ = SetForegroundWindow(handle);
        }
    }

    private async Task LoadCurrentImageAsync(bool fitToWindow)
    {
        StartupDiagnostics.Mark("load_current.begin", Path.GetFileName(_navigator.CurrentPath ?? string.Empty));
        if (_isClosing)
        {
            return;
        }

        var path = _navigator.CurrentPath;
        if (path is null) return;

        StopCurrentImageInteraction();
        CancelBackgroundFullWarmups();
        var generation = ++_loadGeneration;
        _isCurrentPreviewAwaitingFullResolution = false;
        _setActualPixelsAfterFullLoad = false;
        var session = StartLoadSession(generation);
        var token = session.Token;
        CancelPreloadWorker();
        var preloaded = TryTakePreload(path);
        StartupDiagnostics.Mark(preloaded is null ? "load_current.preload_miss" : "load_current.preload_hit", Path.GetFileName(path));

        UpdateNavigationButtons();
        if (preloaded is null)
        {
            ShowStatus(LocalizedText.Get(LocalizedText.Loading));
        }
        UpdateWindowTitle(Path.GetFileName(path));

        try
        {
            if (preloaded is not null)
            {
                if (generation != _loadGeneration || token.IsCancellationRequested)
                {
                    preloaded.Dispose();
                    return;
                }

                SetImage(preloaded, fitToWindow);
                StartupDiagnostics.Mark("load_current.preloaded_set_image", Path.GetFileName(path));
                HandleDisplayedImageLoadState(preloaded, path, session);

                return;
            }

            StartupDiagnostics.Mark("load_current.preview_decode.begin", Path.GetFileName(path));
            var preview = await DecodePreviewExclusiveAsync(path, GetViewportPixelSize(), session);
            StartupDiagnostics.Mark("load_current.preview_decode.end", Path.GetFileName(path));
            if (preview is null)
            {
                return;
            }

            SetImage(preview, fitToWindow);
            StartupDiagnostics.Mark("load_current.preview_set_image", $"{Path.GetFileName(path)} full={preview.IsFullResolution}");
            HandleDisplayedImageLoadState(preview, path, session);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
            {
                return;
            }

            if (IsCurrentLoad(generation, token))
            {
                ClearImage();
                ShowStatus(ex.Message);
            }
        }
        finally
        {
            session.Release();
            StartupDiagnostics.Mark("load_current.end", Path.GetFileName(path));
        }
    }

    private void HandleDisplayedImageLoadState(DecodedImage image, string path, LoadSession session)
    {
        StartupDiagnostics.Mark("load_state.begin", $"{Path.GetFileName(path)} full={image.IsFullResolution}");
        if (image.IsFullResolution)
        {
            ScheduleSmartPreloads(allowFullPreloads: true);
            StartupDiagnostics.Mark("load_state.full_resolution_done", Path.GetFileName(path));
            return;
        }

        if (TryApplyCachedFullResolution(path, setActualPixels: false))
        {
            ScheduleSmartPreloads(allowFullPreloads: true);
            StartupDiagnostics.Mark("load_state.cached_full_applied", Path.GetFileName(path));
            return;
        }

        ShowCurrentPreviewFullResolutionLoading();

        if (ShouldDeferFullResolution(image.Metadata))
        {
            StartBackgroundFullResolutionWarmup(path);
            ScheduleSmartPreloads(allowFullPreloads: true);
            StartupDiagnostics.Mark("load_state.deferred_full_warmup", Path.GetFileName(path));
            return;
        }

        ScheduleSmartPreloads(allowFullPreloads: false);
        StartFullResolutionLoad(path, session);
        StartupDiagnostics.Mark("load_state.started_full_decode", Path.GetFileName(path));
    }

    private async Task<DecodedImage?> DecodePreviewExclusiveAsync(
        string path,
        PixelSize previewSize,
        LoadSession session)
    {
        var generation = session.Generation;
        var token = session.Token;

        await _previewDecodeGate.WaitAsync(token);
        try
        {
            if (!IsCurrentLoad(generation, token))
            {
                return null;
            }

            var preview = await Decoders.DecodeAsync(
                new ImageDecodeRequest(
                    path,
                    previewSize,
                    FullResolution: false,
                    MaxDecodedBytes: GetInteractiveDecodedByteLimit()),
                token);
            if (!IsCurrentLoad(generation, token))
            {
                preview.Dispose();
                return null;
            }

            return preview;
        }
        finally
        {
            _previewDecodeGate.Release();
        }
    }

    private async Task LoadFullResolutionAsync(string path, LoadSession session)
    {
        var generation = session.Generation;
        var token = session.Token;

        try
        {
            var full = await DecodeFullResolutionExclusiveAsync(
                path,
                token,
                () => IsCurrentLoad(generation, token),
                schedulePreloadsWhenIdle: true,
                maxDecodedBytes: GetInteractiveDecodedByteLimit(),
                waitForGate: true);
            if (full is null)
            {
                return;
            }

            var appliedFullImage = false;
            await TryDispatchAsync(() =>
            {
                if (generation != _loadGeneration || token.IsCancellationRequested)
                {
                    return;
                }

                SetImage(full, fitToWindow: false);
                if (_setActualPixelsAfterFullLoad)
                {
                    _setActualPixelsAfterFullLoad = false;
                    UpdateZoomText();
                    ImageSurface.InvalidateVisual();
                }

                appliedFullImage = true;
            });
            if (!appliedFullImage)
            {
                full.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
            {
                return;
            }

            // Keep the already displayed preview if full-resolution decoding fails.
            if (IsCurrentLoad(generation, token))
            {
                await TryDispatchAsync(() =>
                {
                    _setActualPixelsAfterFullLoad = false;
                    _isCurrentPreviewAwaitingFullResolution = false;
                    HideStatus();
                    ShowPreviewOnlyBadge(ex.Message);
                });
            }
        }
        finally
        {
            session.Release();
        }
    }

    private async Task<DecodedImage?> DecodeFullResolutionExclusiveAsync(
        string path,
        CancellationToken token,
        Func<bool>? shouldContinue,
        bool schedulePreloadsWhenIdle,
        long? maxDecodedBytes,
        bool waitForGate,
        Func<IImageDecoder, bool>? decoderFilter = null)
    {
        var enteredDecodeGate = false;
        Interlocked.Increment(ref _fullDecodeTaskCount);

        try
        {
            if (waitForGate)
            {
                await _fullDecodeGate.WaitAsync(token);
                enteredDecodeGate = true;
            }
            else
            {
                enteredDecodeGate = await _fullDecodeGate.WaitAsync(TimeSpan.Zero, token);
                if (!enteredDecodeGate)
                {
                    return null;
                }
            }

            if (token.IsCancellationRequested || shouldContinue?.Invoke() == false)
            {
                return null;
            }

            var request = new ImageDecodeRequest(path, FullResolution: true, MaxDecodedBytes: maxDecodedBytes);
            var full = await Decoders.DecodeAsync(request, token, decoderFilter);
            if (token.IsCancellationRequested || shouldContinue?.Invoke() == false)
            {
                full.Dispose();
                return null;
            }

            return full;
        }
        finally
        {
            if (enteredDecodeGate)
            {
                _fullDecodeGate.Release();
            }

            if (Interlocked.Decrement(ref _fullDecodeTaskCount) == 0 &&
                schedulePreloadsWhenIdle &&
                ShouldSchedulePreloadsAfterFullDecode(token, shouldContinue))
            {
                await TryDispatchAsync(() =>
                {
                    if (!_isClosing)
                    {
                        ScheduleSmartPreloads(allowFullPreloads: true);
                    }
                });
            }
        }
    }

    private void SetImage(DecodedImage image, bool fitToWindow)
    {
        var old = _image;
        _image = image;
        _isCurrentPreviewAwaitingFullResolution = false;
        ApplyPreloadCacheBudget();
        _frameIndex = 0;
        var displaySize = ImageRotation.GetRotatedSize(
            image.Width,
            image.Height,
            _pendingRotationQuarterTurns);
        _viewerState.SetImage(displaySize.Width, displaySize.Height, fitToWindow);
        ConfigureAnimationTimer();
        HideStatus();
        HidePreviewOnlyBadge();
        var oldBytes = old?.EstimatedBytes ?? 0;
        var oldReleasedBytes = CacheOrDisposeReplacedImage(old, image.Metadata.Path);
        RequestMemoryCleanup(oldReleasedBytes == 0 ? 0 : oldBytes);
        UpdateZoomText();
        UpdateNavigationButtons();
        ImageSurface.InvalidateVisual();
    }

    private long CacheOrDisposeReplacedImage(DecodedImage? image, string replacementPath)
    {
        if (image is null)
        {
            return 0;
        }

        var releasedBytes = image.EstimatedBytes;
        if (_isPreloadEnabled &&
            (!image.Metadata.Path.Equals(replacementPath, StringComparison.OrdinalIgnoreCase) ||
                !image.IsFullResolution))
        {
            ApplyPreloadCacheBudget();
            var cache = image.IsFullResolution ? _fullPreloadCache : _previewPreloadCache;
            if (cache.Store(image.Metadata.Path, image, evictionPriority: 0))
            {
                return 0;
            }
        }
        else
        {
            image.Dispose();
        }

        return releasedBytes;
    }

    private void ClearImage()
    {
        _animationTimer.Stop();
        var old = _image;
        var oldBytes = old?.EstimatedBytes ?? 0;
        old?.Dispose();
        _image = null;
        _isCurrentPreviewAwaitingFullResolution = false;
        _pendingRotationQuarterTurns = 0;
        _viewerState.ClearImage();
        ApplyPreloadCacheBudget();
        RequestMemoryCleanup(oldBytes);
        _frameIndex = 0;
        HidePreviewOnlyBadge();
        UpdateZoomText();
        UpdateRotationControls();
        ImageSurface.InvalidateVisual();
    }

    private void ConfigureAnimationTimer()
    {
        _animationTimer.Stop();
        if (_image is not { FrameCount: > 1 }) return;

        _animationTimer.Interval = _image.Frames[0].Delay;
        _animationTimer.Start();
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_image is not { FrameCount: > 1 }) return;

        _frameIndex = (_frameIndex + 1) % _image.FrameCount;
        _animationTimer.Interval = _image.Frames[_frameIndex].Delay;
        ImageSurface.InvalidateVisual();
    }

    private void ImageSurface_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(_viewerBackgroundColor);

        if (_image is null || _image.FrameCount == 0) return;

        var frame = _image.Frames[Math.Clamp(_frameIndex, 0, _image.FrameCount - 1)];
        var destination = _viewerState.GetDestinationRect();
        if (_pendingRotationQuarterTurns == 0)
        {
            canvas.DrawImage(frame.Image, destination, GetRenderSamplingOptions(), _imagePaint);
            return;
        }

        var sourceWidth = (float)(_image.Width * _viewerState.Zoom);
        var sourceHeight = (float)(_image.Height * _viewerState.Zoom);
        canvas.Save();
        canvas.Translate(destination.MidX, destination.MidY);
        canvas.RotateDegrees((float)ImageRotation.GetClockwiseDegrees(_pendingRotationQuarterTurns));
        canvas.DrawImage(
            frame.Image,
            new SKRect(-sourceWidth / 2f, -sourceHeight / 2f, sourceWidth / 2f, sourceHeight / 2f),
            GetRenderSamplingOptions(),
            _imagePaint);
        canvas.Restore();
    }

    private void ImageSurface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_image is null) return;

        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _viewerState.ZoomAt(factor, ToPixelPoint(e.GetPosition(ImageSurface)));
        UpdateZoomText();
        ImageSurface.InvalidateVisual();
        e.Handled = true;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingZoomSlider || _image is null)
        {
            return;
        }

        var targetZoom = GetZoomFromSliderValue(e.NewValue);
        if (Math.Abs(targetZoom - _viewerState.Zoom) < 0.0001 || _viewerState.Zoom <= 0)
        {
            return;
        }

        var viewportCenter = new SKPoint(
            _viewerState.ViewportSize.Width / 2f,
            _viewerState.ViewportSize.Height / 2f);
        _viewerState.ZoomAt(targetZoom / _viewerState.Zoom, viewportCenter);
        UpdateZoomText();
        ImageSurface.InvalidateVisual();
    }

    private void ImageSurface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ImageSurface.Focus();
        if (e.ChangedButton != MouseButton.Left || _image is null) return;

        _isPanning = true;
        _lastPanPoint = ToPixelPoint(e.GetPosition(ImageSurface));
        ImageSurface.CaptureMouse();
        e.Handled = true;
    }

    private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var navigationDelta = GetNavigationDeltaForMouseButton(e.ChangedButton);
        if (navigationDelta == 0)
        {
            return;
        }

        e.Handled = true;
        await RunUiCommandAsync(() => NavigateRelativeAsync(navigationDelta));
    }

    private void ImageSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || _image is null) return;

        var point = ToPixelPoint(e.GetPosition(ImageSurface));
        _viewerState.PanBy(new SKPoint(point.X - _lastPanPoint.X, point.Y - _lastPanPoint.Y));
        _lastPanPoint = point;
        ImageSurface.InvalidateVisual();
        e.Handled = true;
    }

    private void ImageSurface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _isPanning = false;
        ImageSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ImageSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isPanning = false;
    }

    private void StopCurrentImageInteraction()
    {
        _animationTimer.Stop();
        StopPanning();
    }

    private void StopPanning()
    {
        _isPanning = false;
        if (ImageSurface.IsMouseCaptured)
        {
            ImageSurface.ReleaseMouseCapture();
        }
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(NavigatePreviousAsync);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(NavigateNextAsync);
    }

    private Task NavigateRelativeAsync(int delta)
    {
        return delta < 0
            ? NavigatePreviousAsync()
            : NavigateNextAsync();
    }

    private async Task NavigatePreviousAsync()
    {
        if (_isApplyingRotation)
        {
            return;
        }

        if (_navigator.CanMovePrevious && _navigator.MovePrevious() is not null)
        {
            DiscardPendingRotation(refitImage: false);
            await LoadCurrentImageAsync(fitToWindow: true);
        }
    }

    private async Task NavigateNextAsync()
    {
        if (_isApplyingRotation)
        {
            return;
        }

        if (_navigator.CanMoveNext && _navigator.MoveNext() is not null)
        {
            DiscardPendingRotation(refitImage: false);
            await LoadCurrentImageAsync(fitToWindow: true);
        }
    }

    private void ActualPixelsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleActualPixels();
    }

    private void ZoomPopupButton_Click(object sender, RoutedEventArgs e)
    {
        ZoomPopup.IsOpen = !ZoomPopup.IsOpen;
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e)
    {
        RotatePreview(-1);
    }

    private void RotateRightButton_Click(object sender, RoutedEventArgs e)
    {
        RotatePreview(1);
    }

    private void ResetRotationButton_Click(object sender, RoutedEventArgs e)
    {
        DiscardPendingRotation(refitImage: true);
    }

    private async void ApplyRotationButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(ApplyPendingRotationAsync);
    }

    private void ShowInFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCurrentImageInFolder();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(DeleteCurrentImageAsync);
    }

    private async void StatusOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!CanOpenFromStatusOverlay())
        {
            return;
        }

        e.Handled = true;
        await RunUiCommandAsync(ShowOpenDialogAsync);
    }

    private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(ShowOpenDialogAsync);
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(DeleteCurrentImageAsync);
    }

    private void ImageSurface_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        UpdateImageContextMenuItems();
        if (!CanUseCurrentImageFile() && !CanCopyCurrentImage())
        {
            e.Handled = true;
        }
    }

    private async void ContextOpenWithMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(() =>
        {
            OpenCurrentImageWith();
            return Task.CompletedTask;
        });
    }

    private async void ContextSetDesktopBackgroundMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(() =>
        {
            SetCurrentImageAsDesktopBackground();
            return Task.CompletedTask;
        });
    }

    private void ContextOpenFileLocationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowCurrentImageInFolder();
    }

    private async void ContextCopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(() =>
        {
            CopyCurrentImageToClipboard();
            return Task.CompletedTask;
        });
    }

    private async void ContextDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(DeleteCurrentImageAsync);
    }

    private async void ContextPropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(() =>
        {
            ShowCurrentImageProperties();
            return Task.CompletedTask;
        });
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RequestApplicationExit();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        await RunUiCommandAsync(async () =>
        {
            if (_isApplyingRotation)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                await ShowOpenDialogAsync();
            }
            else if (e.Key is Key.Left or Key.Back && _navigator.CanMovePrevious)
            {
                e.Handled = true;
                await NavigatePreviousAsync();
            }
            else if (e.Key is Key.Right or Key.Space && _navigator.CanMoveNext)
            {
                e.Handled = true;
                await NavigateNextAsync();
            }
            else if (e.Key is Key.D1 or Key.NumPad1)
            {
                e.Handled = true;
                ToggleActualPixels();
            }
            else if (e.Key == Key.F)
            {
                e.Handled = true;
                if (IsCurrentPreviewAwaitingFullResolution)
                {
                    return;
                }

                _viewerState.FitToWindow();
                UpdateZoomText();
                ImageSurface.InvalidateVisual();
            }
            else if (e.Key == Key.Delete && CanDeleteCurrentImage())
            {
                e.Handled = true;
                await DeleteCurrentImageAsync();
            }
        });
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isApplyingRotation && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        await RunUiCommandAsync(async () =>
        {
            if (_isApplyingRotation)
            {
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                await OpenPathAsync(files[0]);
            }
        });
    }

    private void ImageSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewport();
        UpdateZoomText();
        UpdateToolbarDensity();
        UpdateStatusOverlayMaxWidth();
        ImageSurface.InvalidateVisual();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateToolbarDensity();
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= Window_ContentRendered;
        StartupDiagnostics.Mark("window.content_rendered");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        if (source is null)
        {
            return;
        }

        _hwndSource = source;
        source.AddHook(WindowProc);
        ApplyNativeRoundedCorners(source.Handle);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButton();
        UpdateToolbarDensity();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
            return;
        }

        SystemCommands.MaximizeWindow(this);
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void DarkModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(DarkModeMenuItem.IsChecked);
        SaveSettings();
    }

    private void SmoothSamplingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetSmoothSampling(true);
    }

    private void NearestSamplingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetSmoothSampling(false);
    }

    private void PreloadEnabledMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetPreloadEnabled(PreloadEnabledMenuItem.IsChecked == true);
    }

    private void PreloadBudgetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !int.TryParse(tag, out var gigabytes))
        {
            UpdateSettingsMenuChecks();
            return;
        }

        SetPreloadBudgetGigabytes(gigabytes);
    }

    private void PreloadAggressivenessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } ||
            !Enum.TryParse<PreloadAggressiveness>(tag, out var aggressiveness))
        {
            UpdateSettingsMenuChecks();
            return;
        }

        SetPreloadAggressiveness(aggressiveness);
    }

    private void AutoRefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetAutoRefreshEnabled(AutoRefreshMenuItem.IsChecked == true);
    }

    private void KeepReadyInBackgroundMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var requested = KeepReadyInBackgroundMenuItem.IsChecked == true;
        if (!StartupRegistration.TrySetEnabled(requested, out var error))
        {
            KeepReadyInBackgroundMenuItem.IsChecked = _keepReadyInBackground;
            MessageBox.Show(
                this,
                LocalizedText.Format(
                    LocalizedText.StartupRegistrationFailedFormat,
                    error ?? LocalizedText.Get(LocalizedText.UnexpectedError)),
                LocalizedText.Get(LocalizedText.AppTitle),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _keepReadyInBackground = requested;
        UpdateSettingsMenuChecks();
        SaveSettings();
    }

    private void SetSmoothSampling(bool useSmoothSampling)
    {
        _useSmoothSampling = useSmoothSampling;
        UpdateSettingsMenuChecks();
        SaveSettings();
        ImageSurface.InvalidateVisual();
    }

    private void SetPreloadEnabled(bool isEnabled)
    {
        _isPreloadEnabled = isEnabled;
        UpdateSettingsMenuChecks();
        SaveSettings();

        if (_isPreloadEnabled)
        {
            ScheduleSmartPreloads(allowFullPreloads: !IsFullDecodeInProgress);
        }
        else
        {
            CancelPreloadWorker();
            ClearPreloadCaches();
        }
    }

    private void SetPreloadBudgetGigabytes(int gigabytes)
    {
        _preloadBudgetGigabytes = Math.Clamp(gigabytes, 1, 15);
        UpdateSettingsMenuChecks();
        ApplyPreloadCacheBudget();
        SaveSettings();

        if (RestartFullResolutionLoadForCurrentPreview())
        {
            return;
        }

        if (_isPreloadEnabled)
        {
            ScheduleSmartPreloads(allowFullPreloads: !IsFullDecodeInProgress);
        }
    }

    private void SetPreloadAggressiveness(PreloadAggressiveness aggressiveness)
    {
        _preloadAggressiveness = aggressiveness;
        UpdateSettingsMenuChecks();
        SaveSettings();

        if (_isPreloadEnabled)
        {
            ScheduleSmartPreloads(allowFullPreloads: !IsFullDecodeInProgress);
        }
    }

    private void SetAutoRefreshEnabled(bool isEnabled)
    {
        _isAutoRefreshEnabled = isEnabled;
        UpdateSettingsMenuChecks();
        UpdateAutoRefreshWatcher();
        SaveSettings();
    }

    private void UpdateSettingsMenuChecks()
    {
        SmoothSamplingMenuItem.IsChecked = _useSmoothSampling;
        NearestSamplingMenuItem.IsChecked = !_useSmoothSampling;
        PreloadEnabledMenuItem.IsChecked = _isPreloadEnabled;
        AutoRefreshMenuItem.IsChecked = _isAutoRefreshEnabled;
        KeepReadyInBackgroundMenuItem.IsChecked = _keepReadyInBackground;

        foreach (var item in PreloadMemoryBudgetMenuItem.Items.OfType<MenuItem>())
        {
            item.IsChecked = item.Tag is string tag &&
                int.TryParse(tag, out var gigabytes) &&
                gigabytes == _preloadBudgetGigabytes;
        }

        ConservativePreloadMenuItem.IsChecked = _preloadAggressiveness == PreloadAggressiveness.Conservative;
        BalancedPreloadMenuItem.IsChecked = _preloadAggressiveness == PreloadAggressiveness.Balanced;
        AggressivePreloadMenuItem.IsChecked = _preloadAggressiveness == PreloadAggressiveness.Aggressive;
    }

    private async Task ShowOpenDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizedText.Get(LocalizedText.OpenImage),
            Filter = ImageDialogFilter.Build(
                Decoders.SupportedExtensions,
                LocalizedText.Get(LocalizedText.ImageFiles),
                LocalizedText.Get(LocalizedText.AllFiles))
        };

        if (dialog.ShowDialog(this) == true)
        {
            await OpenPathAsync(dialog.FileName);
        }
    }

    private void ShowCurrentImageInFolder()
    {
        var path = _navigator.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!File.Exists(fullPath) && (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)))
            {
                UpdateNavigationButtons();
                return;
            }

            if (File.Exists(fullPath) && WindowsShell.TryOpenFolderAndSelectItem(fullPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(directory!)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            TraceBackgroundError($"Failed to show image in folder for '{path}'", ex);
            ShowStatus(LocalizedText.Get(LocalizedText.UnexpectedError));
        }
    }

    private static ReleaseVersion GetCurrentReleaseVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (ReleaseVersion.TryParse(informationalVersion, out var releaseVersion))
        {
            return releaseVersion;
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? new ReleaseVersion(0, 0, 0, null)
            : new ReleaseVersion(
                Math.Max(assemblyVersion.Major, 0),
                Math.Max(assemblyVersion.Minor, 0),
                Math.Max(assemblyVersion.Build, 0),
                null);
    }

    private static string GetUpdateDownloadDirectory(ReleaseVersion version)
    {
        return Path.Combine(Path.GetTempPath(), "Mangosteen", "Updates", version.ToString());
    }

    private static bool IsInstalledBuild()
    {
        try
        {
            return Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe", System.IO.SearchOption.TopDirectoryOnly).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void OpenExternalUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private void OpenCurrentImageWith()
    {
        var path = GetExistingCurrentImagePath();
        if (path is null)
        {
            UpdateNavigationButtons();
            return;
        }

        var startInfo = new ProcessStartInfo("rundll32.exe")
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("shell32.dll,OpenAs_RunDLL");
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo);
    }

    private void SetCurrentImageAsDesktopBackground()
    {
        var path = GetExistingCurrentImagePath();
        if (path is null)
        {
            UpdateNavigationButtons();
            return;
        }

        if (!SystemParametersInfo(
            SpiSetDesktopWallpaper,
            0,
            path,
            SpifUpdateIniFile | SpifSendChange))
        {
            throw new InvalidOperationException(LocalizedText.Get(LocalizedText.UnexpectedError));
        }
    }

    private void CopyCurrentImageToClipboard()
    {
        if (!CanCopyCurrentImage() || _image is null)
        {
            UpdateNavigationButtons();
            return;
        }

        var frameIndex = Math.Clamp(_frameIndex, 0, _image.Frames.Count - 1);
        Clipboard.SetImage(CreateClipboardBitmap(_image.Frames[frameIndex].Image));
    }

    private void ShowCurrentImageProperties()
    {
        var path = GetExistingCurrentImagePath();
        if (path is null)
        {
            UpdateNavigationButtons();
            return;
        }

        var ownerHandle = _hwndSource?.Handle ?? new WindowInteropHelper(this).Handle;
        if (SHObjectProperties(ownerHandle, ShopFilePath, path, propertyPage: null))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        throw error == 0
            ? new InvalidOperationException(LocalizedText.Get(LocalizedText.UnexpectedError))
            : new Win32Exception(error);
    }

    private async Task DeleteCurrentImageAsync()
    {
        var path = _navigator.CurrentPath;
        if (path is null || !File.Exists(path))
        {
            UpdateNavigationButtons();
            return;
        }

        // The shell confirmation dialog and the recycle are a single operation, so a
        // cancelled dialog can only be detected by the file still existing afterwards.
        FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
        if (File.Exists(path))
        {
            return;
        }

        DiscardPendingRotation(refitImage: false);

        StopCurrentImageInteraction();
        _loadGeneration++;
        _openGeneration++;
        _setActualPixelsAfterFullLoad = false;
        _loadSession?.CancelAndDisposeWhenInactive();
        _loadSession = null;
        CancelPreloadWorker();
        CancelFolderIndexing();
        CancelBackgroundFullWarmups();
        ClearPreloadCaches();
        ClearImage();

        if (_navigator.RemoveCurrent() is null)
        {
            UpdateWindowTitle();
            ShowStatus(LocalizedText.Get(LocalizedText.NoImage));
            UpdateNavigationButtons();
            return;
        }

        await LoadCurrentImageAsync(fitToWindow: true);
    }

    private void UpdateViewport()
    {
        _viewerState.SetViewport(GetViewportPixelSize());
    }

    private PixelSize GetViewportPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(ImageSurface);
        return PixelSize.FromDipsWithFallback(
            ImageSurface.ActualWidth,
            ImageSurface.ActualHeight,
            GetFallbackViewportWidthDips(),
            GetFallbackViewportHeightDips(),
            dpi.DpiScaleX,
            dpi.DpiScaleY);
    }

    private double GetFallbackViewportWidthDips()
    {
        if (ActualWidth > 1.0) return ActualWidth;
        if (!double.IsNaN(Width) && Width > 1.0) return Width;
        return MinWidth;
    }

    private double GetFallbackViewportHeightDips()
    {
        var menuHeight = TopChrome is { ActualHeight: > 1.0 }
            ? TopChrome.ActualHeight
            : DefaultTopChromeHeightDips;
        var toolbarHeight = ToolbarHost is { ActualHeight: > 1.0 }
            ? ToolbarHost.ActualHeight
            : DefaultToolbarHeightDips;
        var chromeHeight = menuHeight + toolbarHeight;
        if (ActualHeight > chromeHeight + 1.0) return ActualHeight - chromeHeight;
        if (!double.IsNaN(Height) && Height > chromeHeight + 1.0) return Height - chromeHeight;
        return Math.Max(1.0, MinHeight - chromeHeight);
    }

    private SKPoint ToPixelPoint(WpfPoint point)
    {
        var dpi = VisualTreeHelper.GetDpi(ImageSurface);
        return new SKPoint((float)(point.X * dpi.DpiScaleX), (float)(point.Y * dpi.DpiScaleY));
    }

    private SKSamplingOptions GetRenderSamplingOptions()
    {
        return !_useSmoothSampling && _viewerState.Zoom > 1.0
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
    }

    private bool HasPendingRotation => _pendingRotationQuarterTurns != 0;

    private void RotatePreview(int quarterTurnDelta)
    {
        if (!CanRotateCurrentImage() || _image is null)
        {
            UpdateRotationControls();
            return;
        }

        StopPanning();
        _pendingRotationQuarterTurns = ImageRotation.NormalizeQuarterTurns(
            _pendingRotationQuarterTurns + quarterTurnDelta);
        var displaySize = ImageRotation.GetRotatedSize(
            _image.Width,
            _image.Height,
            _pendingRotationQuarterTurns);
        _viewerState.SetImage(displaySize.Width, displaySize.Height, fitToWindow: true);
        UpdateRotationControls();
        UpdateZoomText();
        ImageSurface.InvalidateVisual();
    }

    private void DiscardPendingRotation(bool refitImage)
    {
        if (!HasPendingRotation)
        {
            UpdateRotationControls();
            return;
        }

        _pendingRotationQuarterTurns = 0;
        if (refitImage && _image is not null)
        {
            _viewerState.SetImage(_image.Width, _image.Height, fitToWindow: true);
            UpdateZoomText();
            ImageSurface.InvalidateVisual();
        }

        UpdateRotationControls();
    }

    private async Task ApplyPendingRotationAsync()
    {
        if (!HasPendingRotation || !CanRotateCurrentImage())
        {
            UpdateRotationControls();
            return;
        }

        var path = _navigator.CurrentPath!;
        var turns = _pendingRotationQuarterTurns;
        var confirmation = MessageBox.Show(
            this,
            LocalizedText.Format(LocalizedText.ApplyRotationConfirmationFormat, Path.GetFileName(path)),
            LocalizedText.Get(LocalizedText.RotationDialogTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var risk = await _rotationService.GetWriteRiskAsync(path, CancellationToken.None);
        var saveMode = RotationSaveMode.ReplaceOriginal;
        if (risk == RotationWriteRisk.PngCopyOnly)
        {
            var saveCopy = MessageBox.Show(
                this,
                LocalizedText.Get(LocalizedText.PngCopyOnlyWarning),
                LocalizedText.Get(LocalizedText.RotationDialogTitle),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            if (saveCopy != MessageBoxResult.Yes)
            {
                return;
            }

            saveMode = RotationSaveMode.PngCopy;
        }
        else if (risk == RotationWriteRisk.Lossy)
        {
            var warning = new LossyRotationDialog(this);
            if (warning.ShowDialog() != true || warning.Choice == LossyRotationChoice.No)
            {
                return;
            }

            saveMode = warning.Choice == LossyRotationChoice.SaveAsPng
                ? RotationSaveMode.PngCopy
                : RotationSaveMode.ReplaceOriginal;
        }

        if (saveMode == RotationSaveMode.PngCopy)
        {
            var copyPath = ImageRotationService.GetDefaultPngCopyPath(path);
            if (File.Exists(copyPath))
            {
                var replaceCopy = MessageBox.Show(
                    this,
                    LocalizedText.Format(LocalizedText.RotatedCopyExistsFormat, Path.GetFileName(copyPath)),
                    LocalizedText.Get(LocalizedText.RotationDialogTitle),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (replaceCopy != MessageBoxResult.Yes)
                {
                    return;
                }
            }
        }

        _isApplyingRotation = true;
        var rotationSaveCts = new CancellationTokenSource();
        var rotationSaveToken = rotationSaveCts.Token;
        _rotationSaveCts = rotationSaveCts;
        UpdateNavigationButtons();
        PrepareForRotationWrite(path);
        ShowStatus(LocalizedText.Get(LocalizedText.ApplyingRotation));

        try
        {
            var outputPath = await _rotationService.RotateAsync(
                path,
                turns,
                saveMode,
                rotationSaveToken);
            rotationSaveToken.ThrowIfCancellationRequested();

            _autoRefreshTimer.Stop();
            _pendingRotationQuarterTurns = 0;
            RemovePreloadedImage(path);
            ClearImage();
            if (saveMode == RotationSaveMode.PngCopy)
            {
                await OpenPathAsync(outputPath);
            }
            else
            {
                await LoadCurrentImageAsync(fitToWindow: true);
            }
        }
        catch (OperationCanceledException) when (rotationSaveToken.IsCancellationRequested)
        {
            if (!_isClosing && _image is not null)
            {
                HideStatus();
            }
        }
        catch (Exception ex)
        {
            TraceBackgroundError("Image rotation failed", ex);
            if (!_isClosing)
            {
                HideStatus();
                MessageBox.Show(
                    this,
                    LocalizedText.Format(LocalizedText.RotationFailedFormat, ex.Message),
                    LocalizedText.Get(LocalizedText.RotationDialogTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            rotationSaveCts.Dispose();
            if (ReferenceEquals(_rotationSaveCts, rotationSaveCts))
            {
                _rotationSaveCts = null;
            }
            _isApplyingRotation = false;
            UpdateNavigationButtons();
        }
    }

    private void PrepareForRotationWrite(string path)
    {
        StopCurrentImageInteraction();
        _loadGeneration++;
        _setActualPixelsAfterFullLoad = false;
        _loadSession?.CancelAndDisposeWhenInactive();
        _loadSession = null;
        CancelPreloadWorker();
        CancelBackgroundFullWarmups();
        RemovePreloadedImage(path);
    }

    private void UpdateRotationControls()
    {
        var canRotate = CanRotateCurrentImage();
        RotateLeftButton.IsEnabled = canRotate;
        RotateRightButton.IsEnabled = canRotate;
        RotationActionsDock.Visibility = HasPendingRotation && _image is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResetRotationButton.IsEnabled = HasPendingRotation && !_isApplyingRotation;
        ApplyRotationButton.IsEnabled = HasPendingRotation && !_isApplyingRotation;
        OpenMenuItem.IsEnabled = !_isApplyingRotation;
    }

    private bool CanRotateCurrentImage()
    {
        return !_isApplyingRotation &&
            _image is not null &&
            IsDisplayedImageCurrent() &&
            _navigator.CurrentPath is { } path &&
            File.Exists(path);
    }

    private void ToggleActualPixels()
    {
        if (_image is null) return;
        if (!IsDisplayedImageCurrent())
        {
            return;
        }

        if (_viewerState.FitsAtActualPixels)
        {
            if (_viewerState.Zoom > 1.0 + ActualPixelZoomTolerance)
            {
                _viewerState.SetActualPixels();
                UpdateZoomText();
                ImageSurface.InvalidateVisual();
            }

            return;
        }

        if (!_image.IsFullResolution)
        {
            var path = _navigator.CurrentPath;
            if (path is not null && TryApplyCachedFullResolution(path, setActualPixels: true))
            {
                return;
            }

            _setActualPixelsAfterFullLoad = true;
            _viewerState.SetActualPixels();
            UpdateZoomText();
            ImageSurface.InvalidateVisual();
            if (path is not null)
            {
                ShowCurrentPreviewFullResolutionLoading();
                StartBackgroundFullResolutionWarmup(path);
            }

            return;
        }

        if (Math.Abs(_viewerState.Zoom - _viewerState.FitZoom) < 0.0001)
        {
            _viewerState.SetActualPixels();
        }
        else
        {
            _viewerState.FitToWindow();
        }

        UpdateZoomText();
        ImageSurface.InvalidateVisual();
    }

    private DecodedImage? TryTakePreload(string path)
    {
        if (_fullPreloadCache.TryTake(path, out var full))
        {
            return full;
        }

        return _previewPreloadCache.TryTake(path, out var preview) ? preview : null;
    }

    private void RemovePreloadedImage(string path)
    {
        _fullPreloadCache.Remove(path);
        _previewPreloadCache.Remove(path);
    }

    private bool TryApplyCachedFullResolution(string path, bool setActualPixels)
    {
        var currentPath = _navigator.CurrentPath;
        if (currentPath is null ||
            !currentPath.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            _image is not { IsFullResolution: false } ||
            !_image.Metadata.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!_fullPreloadCache.TryTake(path, out var full) || full is null)
        {
            return false;
        }

        SetImage(full, fitToWindow: false);
        if (setActualPixels)
        {
            if (_viewerState.Mode != ViewerFitMode.ActualPixels)
            {
                _viewerState.SetActualPixels();
            }

            _setActualPixelsAfterFullLoad = false;
            UpdateZoomText();
            ImageSurface.InvalidateVisual();
        }

        return true;
    }

    private bool IsCurrentPreviewAwaitingFullResolution =>
        _image is { IsFullResolution: false } && _isCurrentPreviewAwaitingFullResolution;

    private void ShowCurrentPreviewFullResolutionLoading()
    {
        if (_image is not { IsFullResolution: false })
        {
            return;
        }

        StopPanning();
        _isCurrentPreviewAwaitingFullResolution = true;
        ShowStatus(LocalizedText.Get(LocalizedText.Loading));
    }

    private LoadSession StartLoadSession(int generation)
    {
        _loadSession?.CancelAndDisposeWhenInactive();
        var session = new LoadSession(generation);
        _loadSession = session;
        return session;
    }

    private void StartFullResolutionLoad(string path, LoadSession session)
    {
        if (!session.TryAddReference())
        {
            return;
        }

        _ = LoadFullResolutionAsync(path, session);
    }

    private void StartBackgroundFullResolutionWarmup(string path)
    {
        if (_isClosing)
        {
            return;
        }

        if (_fullPreloadCache.ContainsFullResolution(path))
        {
            TryApplyCachedFullResolution(path, setActualPixels: _setActualPixelsAfterFullLoad);
            return;
        }

        var startGeneration = Volatile.Read(ref _loadGeneration);
        var maxDecodedBytes = GetInteractiveDecodedByteLimit();
        Task warmupTask;
        CancellationToken token;
        lock (_backgroundFullWarmupGate)
        {
            if (!_backgroundFullWarmups.Add(path))
            {
                return;
            }

            token = _backgroundFullWarmupCts.Token;
            warmupTask = Task.Run(
                async () =>
                {
                    DecodedImage? full = null;
                    try
                    {
                        full = await DecodeFullResolutionExclusiveAsync(
                            path,
                            token,
                            shouldContinue: () => IsCurrentWarmup(path, startGeneration, token),
                            schedulePreloadsWhenIdle: false,
                            maxDecodedBytes: maxDecodedBytes,
                            waitForGate: true,
                            decoderFilter: null);
                        if (full is null || token.IsCancellationRequested)
                        {
                            full?.Dispose();
                            return;
                        }

                        var handedOff = false;
                        await TryDispatchAsync(() =>
                        {
                            if (_isClosing)
                            {
                                return;
                            }

                            var currentPath = _navigator.CurrentPath;
                            if (currentPath is not null &&
                                startGeneration == Volatile.Read(ref _loadGeneration) &&
                                currentPath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                                _image is { IsFullResolution: false } &&
                                _image.Metadata.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                            {
                                SetImage(full, fitToWindow: false);
                                if (_setActualPixelsAfterFullLoad)
                                {
                                    _setActualPixelsAfterFullLoad = false;
                                    UpdateZoomText();
                                    ImageSurface.InvalidateVisual();
                                }

                                handedOff = true;
                                full = null;
                            }
                        });

                        if (!handedOff && full is not null)
                        {
                            await TryDispatchAsync(() =>
                            {
                                if (_isClosing || token.IsCancellationRequested)
                                {
                                    return;
                                }

                                ApplyPreloadCacheBudget();
                                _fullPreloadCache.Store(path, full, evictionPriority: 0);
                                full = null;
                            });

                            full?.Dispose();
                            full = null;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        full?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        full?.Dispose();
                        if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
                        {
                            return;
                        }

                        await TryDispatchAsync(() =>
                        {
                            var currentPath = _navigator.CurrentPath;
                            if (currentPath is not null &&
                                currentPath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                                _image is { IsFullResolution: false } &&
                                _image.Metadata.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                            {
                                _setActualPixelsAfterFullLoad = false;
                                _isCurrentPreviewAwaitingFullResolution = false;
                                HideStatus();
                                ShowPreviewOnlyBadge(ex.Message);
                            }
                        });
                        TraceBackgroundError($"Background full warmup failed for '{path}'", ex);
                    }
                    finally
                    {
                        lock (_backgroundFullWarmupGate)
                        {
                            _backgroundFullWarmups.Remove(path);
                        }
                    }
                },
                token);
            _backgroundFullWarmupTasks.Add(warmupTask);
        }

        _ = RemoveBackgroundWarmupTaskWhenDoneAsync(warmupTask);
    }

    private void CancelBackgroundFullWarmups(bool replaceToken = true)
    {
        CancellationTokenSource previousCts;
        Task[] previousTasks;
        lock (_backgroundFullWarmupGate)
        {
            previousCts = _backgroundFullWarmupCts;
            previousCts.Cancel();
            previousTasks = _backgroundFullWarmupTasks.ToArray();
            if (replaceToken)
            {
                _backgroundFullWarmupCts = new CancellationTokenSource();
            }

            _backgroundFullWarmups.Clear();
        }

        _ = DisposeCancellationSourceWhenDoneAsync(previousCts, Task.WhenAll(previousTasks));
    }

    private async Task RemoveBackgroundWarmupTaskWhenDoneAsync(Task task)
    {
        await IgnorePreloadCompletionAsync(task).ConfigureAwait(false);
        lock (_backgroundFullWarmupGate)
        {
            _backgroundFullWarmupTasks.Remove(task);
        }
    }

    private static bool ShouldDeferFullResolution(Mangosteen.Decoding.ImageMetadata metadata)
    {
        return IsRawLikePath(metadata.Path) ||
            ImageMemoryEstimator.EstimateFullDecodeBytes(metadata) >= AutoFullDecodeLimitBytes;
    }

    private static bool IsRawLikePath(string path)
    {
        return ImageFileExtensions.RawImageExtensions.Contains(ImageFileExtensions.NormalizeExtension(path));
    }

    private void ClearForMissingFile()
    {
        StopCurrentImageInteraction();
        _loadGeneration++;
        _openGeneration++;
        _setActualPixelsAfterFullLoad = false;
        _loadSession?.CancelAndDisposeWhenInactive();
        _loadSession = null;
        CancelPreloadWorker();
        CancelFolderIndexing();
        CancelBackgroundFullWarmups();
        ClearPreloadCaches();
        _navigator.Clear();
        UpdateWindowTitle();
        ClearImage();
        UpdateNavigationButtons();
    }

    private bool RestartFullResolutionLoadForCurrentPreview(bool forceDeferred = false)
    {
        if (_isClosing || _image is not { IsFullResolution: false })
        {
            return false;
        }

        if (!forceDeferred && ShouldDeferFullResolution(_image.Metadata))
        {
            return false;
        }

        var path = _navigator.CurrentPath;
        if (path is null)
        {
            return false;
        }

        CancelBackgroundFullWarmups();
        var generation = ++_loadGeneration;
        var session = StartLoadSession(generation);
        ScheduleSmartPreloads(allowFullPreloads: false);
        StartFullResolutionLoad(path, session);
        session.Release();
        return true;
    }

    private async Task<bool> TryDispatchAsync(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return false;
        }

        try
        {
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                await Dispatcher.InvokeAsync(action);
            }

            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return false;
        }
        catch (ObjectDisposedException) when (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return false;
        }
    }

    private async Task RunUiCommandAsync(Func<Task> command)
    {
        if (_isClosing)
        {
            return;
        }

        try
        {
            await command();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing))
            {
                return;
            }

            TraceBackgroundError("UI command failed", ex);
            if (!_isClosing)
            {
                ShowStatus(ex.Message);
            }
        }
    }

    private void UpdateAutoRefreshWatcher()
    {
        if (!_isAutoRefreshEnabled || _isClosing)
        {
            DisposeAutoRefreshWatcher();
            return;
        }

        var path = _navigator.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            DisposeAutoRefreshWatcher();
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (string.Equals(_autoRefreshPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeAutoRefreshWatcher();

        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory, fileName)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += AutoRefreshWatcher_Changed;
            watcher.Created += AutoRefreshWatcher_Changed;
            watcher.Renamed += AutoRefreshWatcher_Renamed;
            watcher.Deleted += AutoRefreshWatcher_Changed;
            watcher.EnableRaisingEvents = true;

            _autoRefreshWatcher = watcher;
            _autoRefreshPath = fullPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TraceBackgroundError($"Failed to watch '{fullPath}' for changes", ex);
            DisposeAutoRefreshWatcher();
        }
    }

    private void DisposeAutoRefreshWatcher()
    {
        _autoRefreshTimer.Stop();
        if (_autoRefreshWatcher is not null)
        {
            _autoRefreshWatcher.EnableRaisingEvents = false;
            _autoRefreshWatcher.Changed -= AutoRefreshWatcher_Changed;
            _autoRefreshWatcher.Created -= AutoRefreshWatcher_Changed;
            _autoRefreshWatcher.Renamed -= AutoRefreshWatcher_Renamed;
            _autoRefreshWatcher.Deleted -= AutoRefreshWatcher_Changed;
            _autoRefreshWatcher.Dispose();
        }

        _autoRefreshWatcher = null;
        _autoRefreshPath = null;
    }

    private void AutoRefreshWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (IsAutoRefreshEventForCurrentPath(e.FullPath))
        {
            _ = TryDispatchAsync(ScheduleAutoRefreshReload);
        }
    }

    private void AutoRefreshWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        if (IsAutoRefreshEventForCurrentPath(e.FullPath) ||
            IsAutoRefreshEventForCurrentPath(e.OldFullPath))
        {
            _ = TryDispatchAsync(ScheduleAutoRefreshReload);
        }
    }

    private bool IsAutoRefreshEventForCurrentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(_autoRefreshPath) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Path.GetFullPath(path).Equals(_autoRefreshPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void ScheduleAutoRefreshReload()
    {
        if (_isClosing || !_isAutoRefreshEnabled || _isApplyingRotation)
        {
            return;
        }

        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Start();
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _autoRefreshTimer.Stop();
        if (_isClosing || !_isAutoRefreshEnabled || _isAutoRefreshReloading || _isApplyingRotation)
        {
            return;
        }

        var path = _navigator.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path) ||
            !IsAutoRefreshEventForCurrentPath(path))
        {
            UpdateAutoRefreshWatcher();
            return;
        }

        _isAutoRefreshReloading = true;
        try
        {
            DiscardPendingRotation(refitImage: false);
            RemovePreloadedImage(path);
            await RunUiCommandAsync(() => LoadCurrentImageAsync(fitToWindow: false));
        }
        finally
        {
            _isAutoRefreshReloading = false;
        }
    }

    private void StartFolderIndexing(string path, int openGeneration)
    {
        if (_isClosing || IsNetworkPath(path))
        {
            return;
        }

        StartupDiagnostics.Mark("folder_index.start", Path.GetFileName(path));
        var cts = new CancellationTokenSource();
        _folderIndexCts = cts;
        _ = LoadFolderIndexAsync(path, openGeneration, cts);
    }

    private void QueueFolderIndexingAfterFirstPreview(string path, int openGeneration)
    {
        if (_isClosing || openGeneration != _openGeneration)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            () =>
            {
                if (!_isClosing && openGeneration == _openGeneration)
                {
                    StartFolderIndexing(path, openGeneration);
                }
            },
            DispatcherPriority.Background);
    }

    private async Task LoadFolderIndexAsync(string path, int openGeneration, CancellationTokenSource cts)
    {
        var token = cts.Token;
        var supportedExtensions = ImageFileExtensions.BuildFolderScanExtensions(Decoders.SupportedExtensions, path);

        try
        {
            var snapshot = await Task.Run(
                () => ImageNavigator.ScanFolderFor(path, supportedExtensions, token),
                token);

            if (token.IsCancellationRequested || openGeneration != _openGeneration)
            {
                return;
            }

            _navigator.Apply(snapshot);
            UpdateNavigationButtons();
            StartupDiagnostics.Mark("folder_index.applied", $"{Path.GetFileName(path)} count={snapshot.Files.Count}");
            if (_image is not null)
            {
                ScheduleSmartPreloads(allowFullPreloads: !IsFullDecodeInProgress);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
            {
                return;
            }

            TraceBackgroundError("Folder indexing failed", ex);
            // Keep the opened file usable even if folder indexing fails.
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_folderIndexCts, cts))
            {
                _folderIndexCts = null;
            }
        }
    }

    private void CancelFolderIndexing()
    {
        _folderIndexCts?.Cancel();
        _folderIndexCts = null;
    }

    private void CancelUpdateCheck()
    {
        _updateCheckCts?.Cancel();
        _updateCheckCts?.Dispose();
        _updateCheckCts = null;
    }

    private void ScheduleSmartPreloads(bool allowFullPreloads)
    {
        if (_isClosing) return;
        if (!_isPreloadEnabled) return;

        var budgetBytes = GetSelectedPreloadBudgetBytes();
        var profile = GetSelectedPreloadProfile();
        var largeFullLimit = Math.Max(
            0,
            DecodeMemoryPolicy.GetLargeFullPreloadLimit(budgetBytes) + profile.LargeFullPreloadLimitAdjustment);
        var previewDecodeLimit = DecodeMemoryPolicy.GetPreloadDecodeLimit(budgetBytes);
        var fullDecodeLimit = DecodeMemoryPolicy.GetFullPreloadDecodeLimit(budgetBytes);
        ApplyPreloadCacheBudget(budgetBytes);

        var previewSize = GetViewportPixelSize();
        var currentPath = _navigator.CurrentPath;
        var scheduledLoadGeneration = _loadGeneration;
        var paths = _navigator.GetLookaroundPaths(profile.ForwardCount, profile.BackwardCount)
            .Where(path => currentPath is null || !path.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (paths.Length == 0) return;

        CancellationToken token;
        Task previousWorker;
        lock (_preloadWorkerGate)
        {
            if (_isClosing)
            {
                return;
            }

            var previousCts = _preloadCts;
            previousCts.Cancel();
            previousWorker = _preloadWorkerTask;
            _preloadCts = new CancellationTokenSource();
            token = _preloadCts.Token;
            _ = DisposeCancellationSourceWhenDoneAsync(previousCts, previousWorker);

            _preloadWorkerTask = Task.Run(async () =>
            {
                await IgnorePreloadCompletionAsync(previousWorker);
                if (token.IsCancellationRequested) return;

                try
                {
                    await PreloadLookaroundAsync(
                        paths,
                        previewSize,
                        allowFullPreloads,
                        largeFullLimit,
                        previewDecodeLimit,
                        fullDecodeLimit,
                        profile.FullPreloadLikelyPathLimit,
                        profile.SmallFullPreloadCount,
                        profile.FullPreloadIdleDelayMilliseconds,
                        scheduledLoadGeneration,
                        token);
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }
    }

    private static async Task IgnorePreloadCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task DisposeCancellationSourceWhenDoneAsync(CancellationTokenSource source, Task task)
    {
        await IgnorePreloadCompletionAsync(task).ConfigureAwait(false);
        source.Dispose();
    }

    private async Task PreloadLookaroundAsync(
        IReadOnlyList<string> paths,
        PixelSize previewSize,
        bool allowFullPreloads,
        int largeFullLimit,
        long previewDecodeLimit,
        long fullDecodeLimit,
        int fullPreloadLikelyPathLimit,
        int smallFullPreloadCount,
        int fullPreloadIdleDelayMilliseconds,
        int scheduledLoadGeneration,
        CancellationToken token)
    {
        var smallFullPreloads = 0;
        var largeFullPreloads = 0;

        for (var priority = 0; priority < paths.Count; priority++)
        {
            var path = paths[priority];
            token.ThrowIfCancellationRequested();
            if (scheduledLoadGeneration != Volatile.Read(ref _loadGeneration)) return;
            if (_fullPreloadCache.ContainsFullResolution(path)) continue;

            Mangosteen.Decoding.ImageMetadata metadata;
            try
            {
                if (!PreloadDecoderPolicy.HasPreloadCandidate(Decoders, path, _isClosing, token))
                {
                    continue;
                }

                metadata = await Decoders.LoadMetadataAsync(path, token, PreloadDecoderPolicy.IsPreloadDecoder);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
                {
                    return;
                }

                TraceBackgroundError($"Preload metadata failed for '{path}'", ex);
                continue;
            }

            var previewBytes = ImageMemoryEstimator.EstimatePreviewBytes(metadata, previewSize);
            if (!_previewPreloadCache.Contains(path) &&
                !_fullPreloadCache.ContainsFullResolution(path) &&
                _previewPreloadCache.CanStore(path, previewBytes, priority))
            {
                var perPreviewDecodeLimit = DecodeMemoryPolicy.GetPreloadPreviewDecodeLimit(previewBytes, previewDecodeLimit);
                await PreloadPreviewAsync(path, previewSize, perPreviewDecodeLimit, priority, token);
            }
            if (_fullPreloadCache.ContainsFullResolution(path)) continue;

            if (!allowFullPreloads ||
                IsFullDecodeInProgress ||
                scheduledLoadGeneration != Volatile.Read(ref _loadGeneration) ||
                priority >= fullPreloadLikelyPathLimit)
            {
                continue;
            }

            var fullBytes = ImageMemoryEstimator.EstimateFullDecodeBytes(metadata);
            var isSmall = fullBytes <= SmallFullPreloadLimitBytes;
            var isLarge = fullBytes >= LargeImageThresholdBytes;
            var shouldPreloadFull =
                (isSmall && smallFullPreloads < smallFullPreloadCount) ||
                (isLarge && largeFullPreloads < largeFullLimit);

            if (!shouldPreloadFull ||
                fullBytes > fullDecodeLimit ||
                !_fullPreloadCache.CanStore(path, fullBytes, priority))
            {
                continue;
            }

            if (!PreloadDecoderPolicy.HasFullPreloadCandidate(Decoders, path, _isClosing, token))
            {
                continue;
            }

            await Task.Delay(fullPreloadIdleDelayMilliseconds, token);
            if (scheduledLoadGeneration != Volatile.Read(ref _loadGeneration) ||
                IsFullDecodeInProgress ||
                _fullPreloadCache.ContainsFullResolution(path) ||
                !_fullPreloadCache.CanStore(path, fullBytes, priority) ||
                !PreloadDecoderPolicy.HasFullPreloadCandidate(Decoders, path, _isClosing, token))
            {
                continue;
            }

            try
            {
                var full = await DecodeFullResolutionExclusiveAsync(
                    path,
                    token,
                    shouldContinue: null,
                    schedulePreloadsWhenIdle: false,
                    maxDecodedBytes: fullDecodeLimit,
                    waitForGate: false,
                    decoderFilter: PreloadDecoderPolicy.IsFullPreloadDecoder);
                if (token.IsCancellationRequested)
                {
                    full?.Dispose();
                    return;
                }

                if (full is null)
                {
                    continue;
                }

                if (_fullPreloadCache.Store(path, full, priority))
                {
                    if (isSmall)
                    {
                        smallFullPreloads++;
                    }
                    else if (isLarge)
                    {
                        largeFullPreloads++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
                {
                    return;
                }

                TraceBackgroundError($"Full preload failed for '{path}'", ex);
                // Preloading should never affect interactive navigation.
            }
        }
    }

    private async Task PreloadPreviewAsync(
        string path,
        PixelSize previewSize,
        long maxDecodedBytes,
        long priority,
        CancellationToken token)
    {
        if (_previewPreloadCache.Contains(path) || _fullPreloadCache.ContainsFullResolution(path)) return;

        try
        {
            var preview = await Decoders.DecodeAsync(
                new ImageDecodeRequest(path, previewSize, FullResolution: false, MaxDecodedBytes: maxDecodedBytes),
                token,
                PreloadDecoderPolicy.IsPreloadDecoder);
            if (token.IsCancellationRequested)
            {
                preview.Dispose();
                return;
            }

            _previewPreloadCache.Store(path, preview, priority);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, _isClosing, token))
            {
                return;
            }

            TraceBackgroundError($"Preview preload failed for '{path}'", ex);
            // Preloading is opportunistic; failed preview decodes are ignored.
        }
    }

    private void CancelPreloadWorker(bool replaceToken = true)
    {
        lock (_preloadWorkerGate)
        {
            var previousCts = _preloadCts;
            previousCts.Cancel();

            if (replaceToken)
            {
                _preloadCts = new CancellationTokenSource();
            }

            _ = DisposeCancellationSourceWhenDoneAsync(previousCts, _preloadWorkerTask);
        }
    }

    private long GetSelectedPreloadBudgetBytes()
    {
        return Math.Max(1, _preloadBudgetGigabytes) * ImageMemoryEstimator.Gigabyte;
    }

    private PreloadProfile GetSelectedPreloadProfile()
    {
        return _preloadAggressiveness switch
        {
            PreloadAggressiveness.Conservative => new(
                ForwardCount: 20,
                BackwardCount: 4,
                FullPreloadLikelyPathLimit: 2,
                SmallFullPreloadCount: 2,
                FullPreloadIdleDelayMilliseconds: 1_800,
                LargeFullPreloadLimitAdjustment: -1),
            PreloadAggressiveness.Aggressive => new(
                ForwardCount: 100,
                BackwardCount: 20,
                FullPreloadLikelyPathLimit: 8,
                SmallFullPreloadCount: 10,
                FullPreloadIdleDelayMilliseconds: 400,
                LargeFullPreloadLimitAdjustment: 2),
            _ => new(
                ForwardCount: BalancedForwardPreloadCount,
                BackwardCount: BalancedBackwardPreloadCount,
                FullPreloadLikelyPathLimit: BalancedFullPreloadLikelyPathLimit,
                SmallFullPreloadCount: BalancedSmallFullPreloadCount,
                FullPreloadIdleDelayMilliseconds: BalancedFullPreloadIdleDelayMilliseconds,
                LargeFullPreloadLimitAdjustment: 0)
        };
    }

    private void ApplyPreloadCacheBudget(long? selectedBudgetBytes = null)
    {
        var selected = selectedBudgetBytes ?? GetSelectedPreloadBudgetBytes();
        var activeImageBytes = _image?.EstimatedBytes ?? 0;
        var remaining = ImageMemoryEstimator.GetRemainingCacheBudget(selected, activeImageBytes);
        var previewBudget = GetPreviewCacheBudget(selected, remaining);
        _previewPreloadCache.BudgetBytes = previewBudget;
        _fullPreloadCache.BudgetBytes = Math.Max(0, remaining - previewBudget);
    }

    private static long GetPreviewCacheBudget(long selectedBudgetBytes, long remainingCacheBudgetBytes)
    {
        if (remainingCacheBudgetBytes <= 0)
        {
            return 0;
        }

        var desired = Math.Min(
            2L * ImageMemoryEstimator.Gigabyte,
            Math.Max(256L * ImageMemoryEstimator.Megabyte, selectedBudgetBytes / 4));
        return Math.Min(remainingCacheBudgetBytes, desired);
    }

    private void ClearPreloadCaches()
    {
        _fullPreloadCache.Clear();
        _previewPreloadCache.Clear();
    }

    private long GetInteractiveDecodedByteLimit()
    {
        return DecodeMemoryPolicy.GetInteractiveDecodeLimit(GetSelectedPreloadBudgetBytes());
    }

    private bool IsFullDecodeInProgress => Volatile.Read(ref _fullDecodeTaskCount) > 0;

    private bool IsCurrentLoad(int generation, CancellationToken token)
    {
        return generation == _loadGeneration && !token.IsCancellationRequested;
    }

    private bool IsCurrentWarmup(string path, int generation, CancellationToken token)
    {
        if (generation != Volatile.Read(ref _loadGeneration) || token.IsCancellationRequested)
        {
            return false;
        }

        var currentPath = _navigator.CurrentPath;
        return currentPath is not null && currentPath.Equals(path, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsDisplayedImageCurrent()
    {
        var currentPath = _navigator.CurrentPath;
        return currentPath is not null &&
            _image is not null &&
            _image.Metadata.Path.Equals(currentPath, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldSchedulePreloadsAfterFullDecode(CancellationToken token, Func<bool>? shouldContinue)
    {
        return !token.IsCancellationRequested && shouldContinue?.Invoke() != false;
    }

    internal static WindowPlacement CalculateInitialWindowPlacement(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double minWidth,
        double minHeight,
        double fallbackWidth,
        double fallbackHeight)
    {
        var width = GetInitialWindowDimension(workAreaWidth, minWidth, fallbackWidth);
        var height = GetInitialWindowDimension(workAreaHeight, minHeight, fallbackHeight);
        var left = GetCenteredPosition(workAreaLeft, workAreaWidth, width);
        var top = GetCenteredPosition(workAreaTop, workAreaHeight, height);

        return new WindowPlacement(left, top, width, height);
    }

    private static double GetInitialWindowDimension(double workAreaDimension, double minDimension, double fallbackDimension)
    {
        if (!IsUsableDimension(workAreaDimension))
        {
            return RoundDip(Math.Max(minDimension, fallbackDimension));
        }

        var target = workAreaDimension * InitialWorkAreaCoverage;
        target = Math.Max(target, minDimension);
        target = Math.Min(target, workAreaDimension);
        return RoundDip(target);
    }

    private static double GetCenteredPosition(double workAreaPosition, double workAreaDimension, double windowDimension)
    {
        if (!IsUsableDimension(workAreaDimension))
        {
            return double.IsFinite(workAreaPosition) ? RoundDip(workAreaPosition) : 0.0;
        }

        var position = workAreaPosition + (workAreaDimension - windowDimension) / 2.0;
        return RoundDip(position);
    }

    private static bool IsUsableDimension(double value)
    {
        return double.IsFinite(value) && value > 0.0;
    }

    private static double RoundDip(double value)
    {
        return double.IsFinite(value)
            ? Math.Round(value, MidpointRounding.AwayFromZero)
            : 0.0;
    }

    private static void RequestMemoryCleanup(long releasedBytes)
    {
        if (releasedBytes < CleanupThresholdBytes) return;

        _ = Task.Run(() =>
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
            GC.WaitForPendingFinalizers();
        });
    }

    private static void TraceBackgroundError(string context, Exception exception)
    {
        Debug.WriteLine($"{context}: {exception}");
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            ApplyMaximizedWorkArea(hwnd, lParam);
            handled = true;
        }

        return nint.Zero;
    }

    private static void ApplyNativeRoundedCorners(nint hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var preference = (int)DwmWindowCornerPreference.Round;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            Marshal.SizeOf<int>());
    }

    private static void ApplyMaximizedWorkArea(nint hwnd, nint minMaxInfoPointer)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = MonitorInfo.Create();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;

        minMaxInfo.MaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
        minMaxInfo.MaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
        minMaxInfo.MaxSize.X = Math.Abs(workArea.Right - workArea.Left);
        minMaxInfo.MaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);

        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, fDeleteOld: true);
    }

    private static bool IsNetworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\\?\", StringComparison.Ordinal);
    }

    private void UpdateMaximizeRestoreButton()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreIcon.Text = isMaximized ? RestoreWindowIconGlyph : MaximizeWindowIconGlyph;
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }

    private void UpdateActualPixelsIcon()
    {
        var icon = _viewerState.Mode != ViewerFitMode.Fit
            ? ToolbarIconKind.FitToWindow
            : ToolbarIconKind.ActualPixels;
        var canToggle = !_isApplyingRotation && CanToggleActualPixels();
        var brush = canToggle ? GetToolbarIconBrush() : GetToolbarDisabledBrush();
        ActualPixelsButton.IsEnabled = canToggle;
        if (_actualPixelsIconKind == icon &&
            _actualPixelsIconEnabled == canToggle &&
            ReferenceEquals(_actualPixelsIconBrush, brush))
        {
            return;
        }

        ActualPixelsButton.Content = ToolbarIcon.Create(icon, brush);
        _actualPixelsIconKind = icon;
        _actualPixelsIconEnabled = canToggle;
        _actualPixelsIconBrush = brush;
    }

    private void UpdateToolbarIcons()
    {
        var iconBrush = GetToolbarIconBrush();
        PreviousButton.Content = ToolbarIcon.Create(ToolbarIconKind.Previous, iconBrush);
        NextButton.Content = ToolbarIcon.Create(ToolbarIconKind.Next, iconBrush);
        ZoomPopupButton.Content = ToolbarIcon.Create(ToolbarIconKind.Zoom, iconBrush);
        RotateLeftButton.Content = ToolbarIcon.Create(ToolbarIconKind.RotateLeft, iconBrush);
        RotateRightButton.Content = ToolbarIcon.Create(ToolbarIconKind.RotateRight, iconBrush);
        ShowInFolderButton.Content = ToolbarIcon.Create(ToolbarIconKind.Folder, iconBrush);
        DeleteButton.Content = ToolbarIcon.Create(ToolbarIconKind.Delete, GetToolbarDangerBrush());
        UpdateActualPixelsIcon();
    }

    private void ApplyTheme(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;
        _viewerBackgroundColor = isDarkMode ? DarkViewerBackground : LightViewerBackground;
        DarkModeMenuItem.IsChecked = isDarkMode;
        DarkModeMenuItem.ToolTip = isDarkMode
            ? LocalizedText.Get(LocalizedText.UseLightMode)
            : LocalizedText.Get(LocalizedText.UseDarkMode);
        ApplyToolbarThemeResources(isDarkMode);

        Background = isDarkMode
            ? CreateBrush(30, 33, 38)
            : CreateBrush(244, 246, 248);

        ImageSurface.InvalidateVisual();
        UpdateToolbarIcons();
    }

    private void ApplyToolbarThemeResources(bool isDarkMode)
    {
        Resources["ChromeBackground"] = isDarkMode
            ? CreateBrush(30, 33, 38)
            : CreateBrush(244, 246, 248);
        Resources["ChromeSurfaceBackground"] = isDarkMode
            ? CreateBrush(39, 43, 50)
            : CreateBrush(238, 243, 247);
        Resources["ChromeSurfaceBorder"] = isDarkMode
            ? CreateBrush(62, 69, 80)
            : CreateBrush(215, 223, 231);
        Resources["ChromeButtonForeground"] = isDarkMode
            ? CreateBrush(236, 240, 245)
            : CreateBrush(36, 50, 64);
        Resources["ChromeButtonHoverBackground"] = isDarkMode
            ? CreateBrush(54, 61, 72)
            : CreateBrush(229, 236, 243);
        Resources["ChromeButtonPressedBackground"] = isDarkMode
            ? CreateBrush(64, 72, 84)
            : CreateBrush(215, 225, 234);
        Resources["ChromeCloseHoverBackground"] = CreateBrush(196, 43, 28);
        Resources["ChromeCloseHoverForeground"] = CreateBrush(255, 255, 255);
        Resources["EmptyStateBackground"] = isDarkMode
            ? CreateBrush(14, 255, 255, 255)
            : CreateBrush(12, 36, 50, 64);
        Resources["EmptyStateBorder"] = isDarkMode
            ? CreateBrush(96, 104, 114)
            : CreateBrush(148, 160, 172);
        Resources["EmptyStateIcon"] = isDarkMode
            ? CreateBrush(150, 157, 166)
            : CreateBrush(112, 126, 140);
        Resources["PanelBackground"] = isDarkMode
            ? CreateBrush(43, 47, 54)
            : CreateBrush(255, 255, 255);
        Resources["PanelBorder"] = isDarkMode
            ? CreateBrush(70, 76, 86)
            : CreateBrush(212, 219, 227);
        Resources["PanelSeparator"] = isDarkMode
            ? CreateBrush(62, 68, 78)
            : CreateBrush(226, 232, 238);
        Resources["ControlHoverBackground"] = isDarkMode
            ? CreateBrush(60, 67, 77)
            : CreateBrush(235, 241, 247);
        Resources["ControlPressedBackground"] = isDarkMode
            ? CreateBrush(72, 80, 92)
            : CreateBrush(221, 230, 239);
        Resources["TextPrimary"] = isDarkMode
            ? CreateBrush(236, 240, 245)
            : CreateBrush(36, 50, 64);
        Resources["TextSecondary"] = isDarkMode
            ? CreateBrush(160, 170, 182)
            : CreateBrush(94, 108, 122);
        Resources["TextDisabled"] = isDarkMode
            ? CreateBrush(122, 132, 144)
            : CreateBrush(147, 160, 173);
        Resources["TextDanger"] = isDarkMode
            ? CreateBrush(235, 135, 128)
            : CreateBrush(178, 55, 48);
        Resources["AccentCheckBackground"] = isDarkMode
            ? CreateBrush(52, 72, 94)
            : CreateBrush(232, 241, 251);
        Resources["AccentCheckBorder"] = isDarkMode
            ? CreateBrush(120, 170, 220)
            : CreateBrush(127, 168, 209);
        Resources["BadgeBackground"] = isDarkMode
            ? CreateBrush(58, 53, 38)
            : CreateBrush(255, 247, 228);
        Resources["BadgeBorder"] = isDarkMode
            ? CreateBrush(138, 109, 47)
            : CreateBrush(217, 160, 63);
        Resources["BadgeForeground"] = isDarkMode
            ? CreateBrush(232, 201, 122)
            : CreateBrush(107, 74, 0);
        ApplyContextMenuThemeResources();
    }

    private void ApplyContextMenuThemeResources()
    {
        foreach (var key in new[]
        {
            "PanelBackground",
            "PanelBorder",
            "PanelSeparator",
            "ControlHoverBackground",
            "ControlPressedBackground",
            "TextPrimary",
            "TextSecondary",
            "TextDisabled",
            "TextDanger",
            "AccentCheckBackground",
            "AccentCheckBorder"
        })
        {
            CopyResourceToContextMenu(key);
        }

        ImageContextMenu.Background = GetContextMenuBrush("PanelBackground", Brushes.White);
        ImageContextMenu.BorderBrush = GetContextMenuBrush("PanelBorder", Brushes.LightGray);
        ImageContextMenu.Foreground = GetContextMenuBrush("TextPrimary", Brushes.Black);
    }

    private void CopyResourceToContextMenu(string key)
    {
        if (Resources[key] is not null)
        {
            ImageContextMenu.Resources[key] = Resources[key];
        }
    }

    private Brush GetContextMenuBrush(string key, Brush fallback)
    {
        return ImageContextMenu.Resources[key] as Brush ??
            Resources[key] as Brush ??
            fallback;
    }

    private Brush GetToolbarIconBrush()
    {
        return Resources["TextPrimary"] as Brush ?? Brushes.Black;
    }

    private Brush GetToolbarDangerBrush()
    {
        return Resources["TextDanger"] as Brush ?? Brushes.Firebrick;
    }

    private Brush GetToolbarDisabledBrush()
    {
        return Resources["TextDisabled"] as Brush ?? Brushes.Gray;
    }

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public int Flags;

        public static MonitorInfo Create()
        {
            return new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(
        int action,
        int parameter,
        string value,
        int update);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(
        nint ownerWindow,
        uint objectType,
        string objectName,
        string? propertyPage);

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        return CreateBrush(255, red, green, blue);
    }

    private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private void UpdateNavigationButtons()
    {
        PreviousButton.IsEnabled = !_isApplyingRotation && _navigator.CanMovePrevious;
        NextButton.IsEnabled = !_isApplyingRotation && _navigator.CanMoveNext;
        UpdateActualPixelsIcon();
        ShowInFolderButton.IsEnabled = !_isApplyingRotation && CanShowCurrentImageInFolder();
        var canDelete = !_isApplyingRotation && CanDeleteCurrentImage();
        DeleteButton.IsEnabled = canDelete;
        DeleteMenuItem.IsEnabled = canDelete;
        UpdateRotationControls();
        UpdateImageContextMenuItems();
        UpdateImagePositionText();
        UpdateAutoRefreshWatcher();
    }

    private void UpdateImagePositionText()
    {
        if (_navigator.CurrentIndex >= 0 && _navigator.Files.Count > 1)
        {
            ImagePositionText.Text = LocalizedText.Format(
                LocalizedText.ImagePositionFormat,
                _navigator.CurrentIndex + 1,
                _navigator.Files.Count);
            ImagePositionText.Visibility = Visibility.Visible;
            return;
        }

        ImagePositionText.Text = string.Empty;
        ImagePositionText.Visibility = Visibility.Collapsed;
    }

    private void UpdateZoomText()
    {
        ZoomText.Text = _image is null
            ? "-"
            : $"{_viewerState.Zoom * 100:0}%";
        UpdateZoomSlider();
        UpdateActualPixelsIcon();
        UpdateToolbarDensity();
    }

    private void UpdateZoomSlider()
    {
        if (ZoomSlider is null)
        {
            return;
        }

        _isUpdatingZoomSlider = true;
        try
        {
            ZoomSlider.Minimum = ZoomSliderMinimumValue;
            ZoomSlider.Maximum = ZoomSliderMaximumValue;
            ZoomSlider.IsEnabled = _image is not null && !_isApplyingRotation;
            ZoomSlider.Value = _image is null
                ? ZoomSliderMinimumValue
                : GetSliderValueFromZoom(_viewerState.Zoom);
        }
        finally
        {
            _isUpdatingZoomSlider = false;
        }
    }

    private double GetSliderValueFromZoom(double zoom)
    {
        var minimumZoom = GetSliderMinimumZoom();
        var maximumZoom = ViewerState.MaximumZoom;
        if (maximumZoom <= minimumZoom)
        {
            return ZoomSliderMaximumValue;
        }

        var clampedZoom = Math.Clamp(zoom, minimumZoom, maximumZoom);
        var t = Math.Log(clampedZoom / minimumZoom) / Math.Log(maximumZoom / minimumZoom);
        return Math.Clamp(t, 0.0, 1.0) * ZoomSliderMaximumValue;
    }

    private double GetZoomFromSliderValue(double sliderValue)
    {
        var minimumZoom = GetSliderMinimumZoom();
        var maximumZoom = ViewerState.MaximumZoom;
        if (maximumZoom <= minimumZoom)
        {
            return minimumZoom;
        }

        var t = Math.Clamp(sliderValue, ZoomSliderMinimumValue, ZoomSliderMaximumValue) / ZoomSliderMaximumValue;
        return minimumZoom * Math.Pow(maximumZoom / minimumZoom, t);
    }

    private double GetSliderMinimumZoom()
    {
        return Math.Clamp(_viewerState.FitZoom, MinimumSliderZoom, ViewerState.MaximumZoom);
    }

    private bool CanToggleActualPixels()
    {
        return CanToggleActualPixelsForState(
            _image is not null,
            _image?.IsFullResolution ?? false,
            _viewerState.FitsAtActualPixels,
            _viewerState.Zoom,
            _viewerState.Mode);
    }

    internal static bool CanToggleActualPixelsForState(
        bool hasImage,
        bool isFullResolution,
        bool fitsAtActualPixels,
        double zoom,
        ViewerFitMode mode)
    {
        if (!hasImage)
        {
            return false;
        }

        if (!isFullResolution)
        {
            return true;
        }

        if (!fitsAtActualPixels)
        {
            return true;
        }

        return mode != ViewerFitMode.Fit ||
            zoom < 1.0 - ActualPixelZoomTolerance ||
            zoom > 1.0 + ActualPixelZoomTolerance;
    }

    private bool CanDeleteCurrentImage()
    {
        var path = _navigator.CurrentPath;
        return path is not null && File.Exists(path);
    }

    private bool CanUseCurrentImageFile()
    {
        return GetExistingCurrentImagePath() is not null;
    }

    private bool CanCopyCurrentImage()
    {
        return _image is not null && IsDisplayedImageCurrent();
    }

    private bool CanShowCurrentImageInFolder()
    {
        var path = _navigator.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (File.Exists(path))
        {
            return true;
        }

        var directory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
    }

    private string? GetExistingCurrentImagePath()
    {
        var path = _navigator.CurrentPath;
        return path is not null && File.Exists(path) ? path : null;
    }

    private void UpdateImageContextMenuItems()
    {
        var canUseFile = CanUseCurrentImageFile();
        ContextOpenWithMenuItem.IsEnabled = canUseFile;
        ContextSetDesktopBackgroundMenuItem.IsEnabled = canUseFile;
        ContextOpenFileLocationMenuItem.IsEnabled = CanShowCurrentImageInFolder();
        ContextCopyMenuItem.IsEnabled = CanCopyCurrentImage();
        ContextDeleteMenuItem.IsEnabled = CanDeleteCurrentImage();
        ContextPropertiesMenuItem.IsEnabled = canUseFile;
    }

    private static BitmapSource CreateClipboardBitmap(SKImage image)
    {
        using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidDataException("Could not encode image for the clipboard.");
        using var stream = new MemoryStream(encoded.ToArray());
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal static int GetNavigationDeltaForMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.XButton1 => -1,
            MouseButton.XButton2 => 1,
            _ => 0
        };
    }

    private void UpdateToolbarDensity()
    {
        if (ZoomText is null || ZoomSlider is null)
        {
            return;
        }

        ZoomSlider.Visibility = Visibility.Visible;
        ZoomText.Visibility = Visibility.Visible;
    }

    private bool CanOpenFromStatusOverlay()
    {
        return _image is null &&
            _navigator.CurrentPath is null &&
            StatusOverlay.Visibility == Visibility.Visible &&
            EmptyStatePanel.Visibility == Visibility.Visible &&
            string.Equals(StatusText.Text, LocalizedText.Get(LocalizedText.NoImage), StringComparison.Ordinal);
    }

    private void UpdateStatusOverlayOpenState()
    {
        var canOpen = CanOpenFromStatusOverlay();
        StatusOverlay.Cursor = canOpen ? Cursors.Hand : Cursors.Arrow;
        StatusOverlay.ToolTip = canOpen ? LocalizedText.Get(LocalizedText.NoImage) : StatusText.ToolTip;
    }

    private void ShowStatus(string text)
    {
        HideUpdateDownloadProgress();
        HidePreviewOnlyBadge();
        UpdateStatusOverlayMaxWidth();
        var isEmptyState = string.Equals(text, LocalizedText.Get(LocalizedText.NoImage), StringComparison.Ordinal);
        StatusText.ToolTip = text;
        StatusText.Text = text;
        StatusMessageText.ToolTip = text;
        StatusMessageText.Text = text;
        EmptyStatePanel.Visibility = isEmptyState ? Visibility.Visible : Visibility.Collapsed;
        StatusMessagePanel.Visibility = isEmptyState ? Visibility.Collapsed : Visibility.Visible;
        StatusOverlay.Visibility = Visibility.Visible;
        UpdateStatusOverlayOpenState();
    }

    private void RestoreStatusAfterUpdateCheck()
    {
        if (_image is null && _navigator.CurrentPath is null)
        {
            ShowStatus(LocalizedText.Get(LocalizedText.NoImage));
        }
        else if (_image is not null && !_isCurrentPreviewAwaitingFullResolution)
        {
            HideStatus();
        }
    }

    private void HideStatus()
    {
        HideUpdateDownloadProgress();
        StatusOverlay.Visibility = Visibility.Collapsed;
        UpdateStatusOverlayOpenState();
    }

    private void ShowUpdateDownloadProgress(UpdateDownloadProgress progress)
    {
        var hasKnownTotal = progress.TotalBytes is > 0;
        UpdateProgressBar.IsIndeterminate = !hasKnownTotal;
        if (progress.TotalBytes is long totalBytes && totalBytes > 0)
        {
            UpdateProgressBar.Maximum = totalBytes;
            UpdateProgressBar.Value = Math.Clamp(progress.BytesDownloaded, 0, totalBytes);
        }
        else
        {
            UpdateProgressBar.Maximum = 1;
            UpdateProgressBar.Value = 0;
        }

        UpdateProgressDetailsText.Text = FormatUpdateProgressDetails(progress, CultureInfo.CurrentCulture);
        UpdateProgressPanel.Visibility = Visibility.Visible;
    }

    private void HideUpdateDownloadProgress()
    {
        UpdateProgressPanel.Visibility = Visibility.Collapsed;
        UpdateProgressBar.IsIndeterminate = false;
        UpdateProgressBar.Value = 0;
        UpdateProgressDetailsText.Text = string.Empty;
        CancelUpdateButton.IsEnabled = true;
    }

    internal static string FormatUpdateProgressDetails(UpdateDownloadProgress progress, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        const double megabyte = 1024.0 * 1024.0;
        var downloadedMegabytes = Math.Max(0, progress.BytesDownloaded) / megabyte;
        if (progress.TotalBytes is long totalBytes && totalBytes > 0)
        {
            var totalMegabytes = totalBytes / megabyte;
            return string.Format(culture, "{0:0.0} MB / {1:0.0} MB", downloadedMegabytes, totalMegabytes);
        }

        return string.Format(culture, "{0:0.0} MB", downloadedMegabytes);
    }

    private void UpdateStatusOverlayMaxWidth()
    {
        var surfaceWidth = ImageSurface.ActualWidth;
        StatusOverlay.MaxWidth = Math.Max(160, surfaceWidth - 80);
        EmptyStatePanel.Width = Math.Clamp(
            surfaceWidth - EmptyStateHorizontalMargin,
            EmptyStateMinimumWidth,
            EmptyStateMaximumWidth);
    }

    private void ShowPreviewOnlyBadge(string details)
    {
        PreviewOnlyBadge.ToolTip = string.IsNullOrWhiteSpace(details)
            ? LocalizedText.Get(LocalizedText.FullResolutionDecodeFailed)
            : LocalizedText.Format(LocalizedText.FullResolutionDecodeFailedFormat, details);
        PreviewOnlyBadge.Visibility = Visibility.Visible;
    }

    private void HidePreviewOnlyBadge()
    {
        PreviewOnlyBadge.Visibility = Visibility.Collapsed;
        PreviewOnlyBadge.ClearValue(ToolTipProperty);
    }

}
