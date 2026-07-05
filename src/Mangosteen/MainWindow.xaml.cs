using Mangosteen.Caching;
using Mangosteen.Core;
using Mangosteen.Decoding;
using Mangosteen.Icons;
using Mangosteen.Localization;
using Mangosteen.Navigation;
using Mangosteen.Rendering;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private const int WmGetMinMaxInfo = 0x0024;
    private const double EmptyStateMinimumWidth = 280.0;
    private const double EmptyStateMaximumWidth = 500.0;
    private const double EmptyStateHorizontalMargin = 240.0;
    private const double ZoomSliderMinimumValue = 0.0;
    private const double ZoomSliderMaximumValue = 100.0;
    private const double MinimumSliderZoom = 0.0001;
    private const double ActualPixelZoomTolerance = 0.0001;
    private const string MaximizeWindowIconGlyph = "\uE922";
    private const string RestoreWindowIconGlyph = "\uE923";
    private static readonly SKColor LightViewerBackground = new(244, 246, 248);
    private static readonly SKColor DarkViewerBackground = new(30, 33, 38);

    private readonly DecoderRegistry _decoders = DecoderRegistry.CreateDefault();
    private readonly ImageNavigator _navigator = new();
    private readonly ViewerState _viewerState = new();
    private readonly DispatcherTimer _animationTimer = new();
    private readonly DispatcherTimer _autoRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ImagePreloadCache _fullPreloadCache = new();
    private readonly ImagePreloadCache _previewPreloadCache = new();
    private readonly object _preloadWorkerGate = new();
    private readonly object _backgroundFullWarmupGate = new();
    private readonly HashSet<string> _backgroundFullWarmups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _backgroundFullWarmupTasks = [];
    private readonly SemaphoreSlim _previewDecodeGate = new(1, 1);
    private readonly SemaphoreSlim _fullDecodeGate = new(1, 1);
    private LoadSession? _loadSession;
    private CancellationTokenSource? _folderIndexCts;
    private CancellationTokenSource _preloadCts = new();
    private CancellationTokenSource _backgroundFullWarmupCts = new();
    private Task _preloadWorkerTask = Task.CompletedTask;
    private DecodedImage? _image;
    private int _frameIndex;
    private int _loadGeneration;
    private int _openGeneration;
    private int _fullDecodeTaskCount;
    private bool _isClosing;
    private bool _isPanning;
    private bool _isCurrentPreviewAwaitingFullResolution;
    private bool _setActualPixelsAfterFullLoad;
    private bool _useSmoothSampling = true;
    private bool _isPreloadEnabled = true;
    private bool _isAutoRefreshEnabled;
    private bool _isUpdatingZoomSlider;
    private bool _isDarkMode = true;
    private bool _isAutoRefreshReloading;
    private int _preloadBudgetGigabytes = DefaultPreloadBudgetGigabytes;
    private PreloadAggressiveness _preloadAggressiveness = PreloadAggressiveness.Balanced;
    private SKColor _viewerBackgroundColor = LightViewerBackground;
    private SKPoint _lastPanPoint;
    private HwndSource? _hwndSource;
    private FileSystemWatcher? _autoRefreshWatcher;
    private string? _autoRefreshPath;

    public MainWindow()
    {
        StartupDiagnostics.Mark("window.ctor.begin");
        InitializeComponent();
        StartupDiagnostics.Mark("window.initialize_component.end");
        ApplySettings(AppSettings.Load());
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

    private void ApplySettings(AppSettings settings)
    {
        _useSmoothSampling = settings.UseSmoothSampling;
        _isPreloadEnabled = settings.IsPreloadEnabled;
        _isAutoRefreshEnabled = settings.IsAutoRefreshEnabled;
        _preloadBudgetGigabytes = Math.Clamp(settings.PreloadBudgetGigabytes, 1, 15);
        _preloadAggressiveness = settings.PreloadAggressiveness;
        ApplyTheme(settings.IsDarkMode);
    }

    private void SaveSettings()
    {
        new AppSettings
        {
            IsDarkMode = _isDarkMode,
            UseSmoothSampling = _useSmoothSampling,
            IsPreloadEnabled = _isPreloadEnabled,
            IsAutoRefreshEnabled = _isAutoRefreshEnabled,
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
        DarkModeMenuItem.Header = LocalizedText.Get(LocalizedText.ToggleDarkMode);
        OptionsHelpMenuItem.Header = LocalizedText.Get(LocalizedText.OptionsHelp);
        SamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.UpscalingTooltip);
        SmoothSamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.SmoothUpscalingTooltip);
        NearestSamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.NearestUpscalingTooltip);
        PreloadEnabledMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadNearbyImagesTooltip);
        PreloadMemoryBudgetMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadMemoryBudgetTooltip);
        PreloadAggressivenessMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadAggressivenessTooltip);
        AutoRefreshMenuItem.ToolTip = LocalizedText.Get(LocalizedText.AutoRefreshCurrentImageTooltip);
        ConservativePreloadMenuItem.ToolTip = LocalizedText.Get(LocalizedText.ConservativePreloadTooltip);
        BalancedPreloadMenuItem.ToolTip = LocalizedText.Get(LocalizedText.BalancedPreloadTooltip);
        AggressivePreloadMenuItem.ToolTip = LocalizedText.Get(LocalizedText.AggressivePreloadTooltip);
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
        PreviousButton.ToolTip = $"{LocalizedText.Get(LocalizedText.PreviousImage)} (←)";
        NextButton.ToolTip = $"{LocalizedText.Get(LocalizedText.NextImage)} (→)";
        ActualPixelsButton.ToolTip = $"{LocalizedText.Get(LocalizedText.ToggleActualPixels)} (1 / F)";
        ZoomPopupButton.ToolTip = LocalizedText.Get(LocalizedText.Zoom);
        ShowInFolderButton.ToolTip = LocalizedText.Get(LocalizedText.ShowImageInFolder);
        DeleteButton.ToolTip = $"{LocalizedText.Get(LocalizedText.DeleteImage)} (Del)";
        ZoomText.ToolTip = LocalizedText.Get(LocalizedText.Zoom);
        ZoomSlider.ToolTip = LocalizedText.Get(LocalizedText.Zoom);
        OpenMenuItem.InputGestureText = "Ctrl+O";
        DeleteMenuItem.InputGestureText = "Del";
        UpdateSettingsMenuChecks();
        UpdateZoomText();
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
        CancelFolderIndexing();
        CancelPreloadWorker(replaceToken: false);
        CancelBackgroundFullWarmups(replaceToken: false);
        _fullPreloadCache.Dispose();
        _previewPreloadCache.Dispose();
        _image?.Dispose();
        _image = null;
        _decoders.Dispose();
        base.OnClosed(e);
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

            var preview = await _decoders.DecodeAsync(
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
            var full = await _decoders.DecodeAsync(request, token, decoderFilter);
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
        _viewerState.SetImage(image.Width, image.Height, fitToWindow);
        ConfigureAnimationTimer();
        HideStatus();
        HidePreviewOnlyBadge();
        var oldBytes = old?.EstimatedBytes ?? 0;
        var oldReleasedBytes = CacheOrDisposeReplacedImage(old, image.Metadata.Path);
        RequestMemoryCleanup(oldReleasedBytes == 0 ? 0 : oldBytes);
        UpdateZoomText();
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
        _viewerState.ClearImage();
        ApplyPreloadCacheBudget();
        RequestMemoryCleanup(oldBytes);
        _frameIndex = 0;
        HidePreviewOnlyBadge();
        UpdateZoomText();
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
        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        canvas.DrawImage(frame.Image, destination, GetRenderSamplingOptions(), paint);
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
        if (_navigator.CanMovePrevious && _navigator.MovePrevious() is not null)
        {
            await LoadCurrentImageAsync(fitToWindow: true);
        }
    }

    private async Task NavigateNextAsync()
    {
        if (_navigator.CanMoveNext && _navigator.MoveNext() is not null)
        {
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

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        await RunUiCommandAsync(async () =>
        {
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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        await RunUiCommandAsync(async () =>
        {
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
                _decoders.SupportedExtensions,
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

        var directory = Path.GetDirectoryName(path);
        if (!File.Exists(path) && (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)))
        {
            UpdateNavigationButtons();
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe");
            startInfo.ArgumentList.Add(File.Exists(path) ? $"/select,{path}" : directory!);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            TraceBackgroundError($"Failed to show image in folder for '{path}'", ex);
            ShowStatus(LocalizedText.Get(LocalizedText.UnexpectedError));
        }
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
        if (_isClosing || !_isAutoRefreshEnabled)
        {
            return;
        }

        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Start();
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _autoRefreshTimer.Stop();
        if (_isClosing || !_isAutoRefreshEnabled || _isAutoRefreshReloading)
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
        var supportedExtensions = ImageFileExtensions.BuildFolderScanExtensions(_decoders.SupportedExtensions, path);

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
                if (!PreloadDecoderPolicy.HasPreloadCandidate(_decoders, path, _isClosing, token))
                {
                    continue;
                }

                metadata = await _decoders.LoadMetadataAsync(path, token, PreloadDecoderPolicy.IsPreloadDecoder);
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

            if (!PreloadDecoderPolicy.HasFullPreloadCandidate(_decoders, path, _isClosing, token))
            {
                continue;
            }

            await Task.Delay(fullPreloadIdleDelayMilliseconds, token);
            if (scheduledLoadGeneration != Volatile.Read(ref _loadGeneration) ||
                IsFullDecodeInProgress ||
                _fullPreloadCache.ContainsFullResolution(path) ||
                !_fullPreloadCache.CanStore(path, fullBytes, priority) ||
                !PreloadDecoderPolicy.HasFullPreloadCandidate(_decoders, path, _isClosing, token))
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
            var preview = await _decoders.DecodeAsync(
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
        ActualPixelsButton.Content = ToolbarIcon.Create(icon, GetToolbarIconBrush());
    }

    private void UpdateToolbarIcons()
    {
        var iconBrush = GetToolbarIconBrush();
        PreviousButton.Content = ToolbarIcon.Create(ToolbarIconKind.Previous, iconBrush);
        NextButton.Content = ToolbarIcon.Create(ToolbarIconKind.Next, iconBrush);
        ZoomPopupButton.Content = ToolbarIcon.Create(ToolbarIconKind.Zoom, iconBrush);
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
    }

    private Brush GetToolbarIconBrush()
    {
        return Resources["TextPrimary"] as Brush ?? Brushes.Black;
    }

    private Brush GetToolbarDangerBrush()
    {
        return Resources["TextDanger"] as Brush ?? Brushes.Firebrick;
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
        PreviousButton.IsEnabled = _navigator.CanMovePrevious;
        NextButton.IsEnabled = _navigator.CanMoveNext;
        ActualPixelsButton.IsEnabled = CanToggleActualPixels();
        ShowInFolderButton.IsEnabled = CanShowCurrentImageInFolder();
        var canDelete = CanDeleteCurrentImage();
        DeleteButton.IsEnabled = canDelete;
        DeleteMenuItem.IsEnabled = canDelete;
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
        ActualPixelsButton.IsEnabled = CanToggleActualPixels();
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
            ZoomSlider.IsEnabled = _image is not null;
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
            _viewerState.FitsAtActualPixels,
            _viewerState.Zoom);
    }

    internal static bool CanToggleActualPixelsForState(bool hasImage, bool fitsAtActualPixels, double zoom)
    {
        return hasImage && (!fitsAtActualPixels || zoom > 1.0 + ActualPixelZoomTolerance);
    }

    private bool CanDeleteCurrentImage()
    {
        var path = _navigator.CurrentPath;
        return path is not null && File.Exists(path);
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

    private void HideStatus()
    {
        StatusOverlay.Visibility = Visibility.Collapsed;
        UpdateStatusOverlayOpenState();
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
