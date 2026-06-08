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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private const double DefaultMenuHeightDips = 28.0;
    private const double DefaultToolbarHeightDips = 38.0;
    private static readonly SKColor LightViewerBackground = new(234, 244, 255);
    private static readonly SKColor DarkViewerBackground = new(32, 32, 32);

    private readonly DecoderRegistry _decoders = DecoderRegistry.CreateDefault();
    private readonly ImageNavigator _navigator = new();
    private readonly ViewerState _viewerState = new();
    private readonly DispatcherTimer _animationTimer = new();
    private readonly ImagePreloadCache _fullPreloadCache = new();
    private readonly ImagePreloadCache _previewPreloadCache = new();
    private readonly object _preloadWorkerGate = new();
    private readonly object _backgroundFullWarmupGate = new();
    private readonly HashSet<string> _backgroundFullWarmups = new(StringComparer.OrdinalIgnoreCase);
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
    private int _preloadBudgetGigabytes = DefaultPreloadBudgetGigabytes;
    private PreloadAggressiveness _preloadAggressiveness = PreloadAggressiveness.Balanced;
    private SKColor _viewerBackgroundColor = LightViewerBackground;
    private SKPoint _lastPanPoint;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(isDarkMode: false);
        ApplyLocalization();
        UpdateViewport();
        UpdateNavigationButtons();
        ApplyPreloadCacheBudget();
        UpdateActualPixelsIcon();
        UpdateToolbarDensity();

        _animationTimer.Tick += AnimationTimer_Tick;
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
        OptionsHelpMenuItem.Header = LocalizedText.Get(LocalizedText.OptionsHelp);
        SamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.UpscalingTooltip);
        SmoothSamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.SmoothUpscalingTooltip);
        NearestSamplingMenuItem.ToolTip = LocalizedText.Get(LocalizedText.NearestUpscalingTooltip);
        PreloadEnabledMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadNearbyImagesTooltip);
        PreloadMemoryBudgetMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadMemoryBudgetTooltip);
        PreloadAggressivenessMenuItem.ToolTip = LocalizedText.Get(LocalizedText.PreloadAggressivenessTooltip);
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
            StatusText.Text = LocalizedText.Get(LocalizedText.NoImage);
        }

        PreviewOnlyText.Text = LocalizedText.Get(LocalizedText.PreviewOnly);
        ThemeToggleButton.ToolTip = ThemeToggleButton.IsChecked == true
            ? LocalizedText.Get(LocalizedText.UseLightMode)
            : LocalizedText.Get(LocalizedText.UseDarkMode);
        PreviousButton.ToolTip = LocalizedText.Get(LocalizedText.PreviousImage);
        NextButton.ToolTip = LocalizedText.Get(LocalizedText.NextImage);
        ActualPixelsButton.ToolTip = LocalizedText.Get(LocalizedText.ToggleActualPixels);
        DeleteButton.ToolTip = LocalizedText.Get(LocalizedText.DeleteImage);
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
        Title = string.IsNullOrWhiteSpace(fileName) ? appTitle : $"{fileName} - {appTitle}";
    }

    public async Task OpenPathAsync(string path)
    {
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
        CancelPreloadWorker();
        CancelFolderIndexing();
        CancelBackgroundFullWarmups();
        ClearPreloadCaches();
        UpdateNavigationButtons();
        if (!_isClosing)
        {
            StartFolderIndexing(fullPath, openGeneration);
        }
        await LoadCurrentImageAsync(fitToWindow: true);
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
                HandleDisplayedImageLoadState(preloaded, path, session);

                return;
            }

            var preview = await DecodePreviewExclusiveAsync(path, GetViewportPixelSize(), session);
            if (preview is null)
            {
                return;
            }

            SetImage(preview, fitToWindow);
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
        }
    }

    private void HandleDisplayedImageLoadState(DecodedImage image, string path, LoadSession session)
    {
        if (image.IsFullResolution)
        {
            ScheduleSmartPreloads(allowFullPreloads: true);
            return;
        }

        if (TryApplyCachedFullResolution(path, setActualPixels: false))
        {
            ScheduleSmartPreloads(allowFullPreloads: true);
            return;
        }

        ShowCurrentPreviewFullResolutionLoading();

        if (ShouldDeferFullResolution(image.Metadata))
        {
            StartBackgroundFullResolutionWarmup(path);
            ScheduleSmartPreloads(allowFullPreloads: true);
            return;
        }

        ScheduleSmartPreloads(allowFullPreloads: false);
        StartFullResolutionLoad(path, session);
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
        if (IsCurrentPreviewAwaitingFullResolution)
        {
            e.Handled = true;
            return;
        }

        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _viewerState.ZoomAt(factor, ToPixelPoint(e.GetPosition(ImageSurface)));
        UpdateZoomText();
        ImageSurface.InvalidateVisual();
        e.Handled = true;
    }

    private void ImageSurface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ImageSurface.Focus();
        if (e.ChangedButton != MouseButton.Left || _image is null) return;
        if (IsCurrentPreviewAwaitingFullResolution)
        {
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _lastPanPoint = ToPixelPoint(e.GetPosition(ImageSurface));
        ImageSurface.CaptureMouse();
        e.Handled = true;
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
        await RunUiCommandAsync(async () =>
        {
            if (_navigator.MovePrevious() is not null)
            {
                await LoadCurrentImageAsync(fitToWindow: true);
            }
        });
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(async () =>
        {
            if (_navigator.MoveNext() is not null)
            {
                await LoadCurrentImageAsync(fitToWindow: true);
            }
        });
    }

    private void ActualPixelsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleActualPixels();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiCommandAsync(DeleteCurrentImageAsync);
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
                _navigator.MovePrevious();
                await LoadCurrentImageAsync(fitToWindow: true);
            }
            else if (e.Key is Key.Right or Key.Space && _navigator.CanMoveNext)
            {
                e.Handled = true;
                _navigator.MoveNext();
                await LoadCurrentImageAsync(fitToWindow: true);
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

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeToggleButton.IsChecked == true);
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

    private void SetSmoothSampling(bool useSmoothSampling)
    {
        _useSmoothSampling = useSmoothSampling;
        UpdateSettingsMenuChecks();
        ImageSurface.InvalidateVisual();
    }

    private void SetPreloadEnabled(bool isEnabled)
    {
        _isPreloadEnabled = isEnabled;
        UpdateSettingsMenuChecks();

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

        if (_isPreloadEnabled)
        {
            ScheduleSmartPreloads(allowFullPreloads: !IsFullDecodeInProgress);
        }
    }

    private void UpdateSettingsMenuChecks()
    {
        SmoothSamplingMenuItem.IsChecked = _useSmoothSampling;
        NearestSamplingMenuItem.IsChecked = !_useSmoothSampling;
        PreloadEnabledMenuItem.IsChecked = _isPreloadEnabled;

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

    private async Task DeleteCurrentImageAsync()
    {
        var path = _navigator.CurrentPath;
        if (path is null || !File.Exists(path))
        {
            UpdateNavigationButtons();
            return;
        }

        var fileName = Path.GetFileName(path);
        var result = MessageBox.Show(
            this,
            LocalizedText.Format(LocalizedText.DeleteImageConfirmationFormat, fileName),
            LocalizedText.Get(LocalizedText.DeleteImage),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
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

        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

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
        var menuHeight = MainMenu is { ActualHeight: > 1.0 }
            ? MainMenu.ActualHeight
            : DefaultMenuHeightDips;
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
        lock (_backgroundFullWarmupGate)
        {
            if (!_backgroundFullWarmups.Add(path))
            {
                return;
            }
        }

        var token = _backgroundFullWarmupCts.Token;
        var maxDecodedBytes = GetInteractiveDecodedByteLimit();
        _ = Task.Run(
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
    }

    private void CancelBackgroundFullWarmups(bool replaceToken = true)
    {
        lock (_backgroundFullWarmupGate)
        {
            var previousCts = _backgroundFullWarmupCts;
            previousCts.Cancel();
            if (replaceToken)
            {
                _backgroundFullWarmupCts = new CancellationTokenSource();
            }

            _backgroundFullWarmups.Clear();
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

    private void StartFolderIndexing(string path, int openGeneration)
    {
        if (_isClosing || IsNetworkPath(path))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _folderIndexCts = cts;
        _ = LoadFolderIndexAsync(path, openGeneration, cts);
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
        DeleteButton.Content = ToolbarIcon.Create(ToolbarIconKind.Delete, Brushes.Firebrick);
        UpdateActualPixelsIcon();
    }

    private void ApplyTheme(bool isDarkMode)
    {
        _viewerBackgroundColor = isDarkMode ? DarkViewerBackground : LightViewerBackground;
        ThemeToggleButton.IsChecked = isDarkMode;
        ThemeToggleButton.ToolTip = isDarkMode
            ? LocalizedText.Get(LocalizedText.UseLightMode)
            : LocalizedText.Get(LocalizedText.UseDarkMode);
        ApplyToolbarThemeResources(isDarkMode);

        Background = isDarkMode
            ? CreateBrush(32, 32, 32)
            : CreateBrush(234, 244, 255);
        ToolbarHost.Background = isDarkMode
            ? CreateBrush(37, 41, 50)
            : CreateBrush(255, 255, 255);
        ToolbarHost.BorderBrush = isDarkMode
            ? CreateBrush(69, 75, 85)
            : CreateBrush(209, 213, 219);
        MainMenu.Background = Resources["MenuBarBackground"] as Brush ?? Brushes.Transparent;
        MainMenu.Foreground = Resources["MenuBarForeground"] as Brush ?? Brushes.Black;
        MainMenu.BorderBrush = Resources["MenuBarBorder"] as Brush ?? Brushes.Transparent;
        ZoomText.Foreground = isDarkMode
            ? CreateBrush(215, 222, 232)
            : CreateBrush(51, 65, 85);

        StatusOverlay.Background = isDarkMode
            ? CreateBrush(224, 38, 45, 54)
            : CreateBrush(221, 234, 246, 255);
        StatusOverlay.BorderBrush = isDarkMode
            ? CreateBrush(107, 114, 128)
            : CreateBrush(142, 169, 199);
        StatusText.Foreground = isDarkMode
            ? CreateBrush(243, 246, 249)
            : CreateBrush(31, 41, 51);

        ImageSurface.InvalidateVisual();
        UpdateToolbarIcons();
    }

    private void ApplyToolbarThemeResources(bool isDarkMode)
    {
        Resources["ToolbarControlForeground"] = isDarkMode
            ? CreateBrush(225, 231, 239)
            : CreateBrush(31, 41, 51);
        Resources["ToolbarButtonBackground"] = isDarkMode
            ? CreateGradientBrush((50, 56, 66), (42, 47, 56), (34, 38, 46))
            : CreateGradientBrush((255, 255, 255), (243, 246, 249), (228, 234, 240));
        Resources["ToolbarButtonBorder"] = isDarkMode
            ? CreateBrush(85, 94, 108)
            : CreateBrush(154, 167, 181);
        Resources["ToolbarButtonHoverBorder"] = isDarkMode
            ? CreateBrush(111, 163, 214)
            : CreateBrush(106, 158, 214);
        Resources["ToolbarButtonPressedBackground"] = isDarkMode
            ? CreateBrush(55, 69, 86)
            : CreateBrush(220, 238, 255);
        Resources["ToolbarButtonDisabledBackground"] = isDarkMode
            ? CreateBrush(39, 44, 53)
            : CreateBrush(246, 248, 250);
        Resources["ToolbarButtonDisabledBorder"] = isDarkMode
            ? CreateBrush(67, 75, 88)
            : CreateBrush(207, 214, 222);
        Resources["ToolbarButtonShadow"] = isDarkMode
            ? CreateBrush(8, 10, 14)
            : CreateBrush(111, 127, 145);
        Resources["ToolbarButtonShell"] = isDarkMode
            ? CreateBrush(80, 255, 255, 255)
            : CreateBrush(85, 255, 255, 255);
        Resources["ToolbarToggleBackground"] = isDarkMode
            ? CreateBrush(34, 38, 46)
            : CreateBrush(255, 255, 255);
        Resources["ToolbarToggleCheckedBackground"] = isDarkMode
            ? CreateBrush(42, 57, 74)
            : CreateBrush(234, 244, 255);
        Resources["ToolbarToggleBorder"] = isDarkMode
            ? CreateBrush(86, 95, 109)
            : CreateBrush(182, 189, 198);
        Resources["ToolbarToggleCheckedBorder"] = isDarkMode
            ? CreateBrush(116, 174, 230)
            : CreateBrush(106, 158, 214);
        Resources["ToolbarComboBackground"] = isDarkMode
            ? CreateBrush(34, 38, 46)
            : CreateBrush(255, 255, 255);
        Resources["ToolbarComboBorder"] = isDarkMode
            ? CreateBrush(86, 95, 109)
            : CreateBrush(182, 189, 198);
        Resources["ToolbarComboPopupBackground"] = isDarkMode
            ? CreateBrush(30, 34, 42)
            : CreateBrush(255, 255, 255);
        Resources["MenuBarBackground"] = isDarkMode
            ? CreateBrush(37, 41, 50)
            : CreateBrush(255, 255, 255);
        Resources["MenuBarForeground"] = isDarkMode
            ? CreateBrush(225, 231, 239)
            : CreateBrush(31, 41, 51);
        Resources["MenuBarBorder"] = isDarkMode
            ? CreateBrush(69, 75, 85)
            : CreateBrush(209, 213, 219);
        Resources["MenuItemForeground"] = isDarkMode
            ? CreateBrush(230, 236, 244)
            : CreateBrush(31, 41, 51);
        Resources["MenuItemDisabledForeground"] = isDarkMode
            ? CreateBrush(132, 142, 156)
            : CreateBrush(123, 132, 145);
        Resources["MenuItemHoverBackground"] = isDarkMode
            ? CreateBrush(51, 61, 76)
            : CreateBrush(234, 243, 255);
        Resources["MenuItemOpenBackground"] = isDarkMode
            ? CreateBrush(59, 73, 91)
            : CreateBrush(221, 238, 255);
        Resources["MenuPopupBackground"] = isDarkMode
            ? CreateBrush(38, 43, 52)
            : CreateBrush(255, 255, 255);
        Resources["MenuPopupBorder"] = isDarkMode
            ? CreateBrush(88, 98, 114)
            : CreateBrush(174, 183, 194);
        Resources["MenuSeparatorBrush"] = isDarkMode
            ? CreateBrush(61, 68, 80)
            : CreateBrush(229, 231, 235);
        Resources["MenuCheckBackground"] = isDarkMode
            ? CreateBrush(49, 75, 98)
            : CreateBrush(234, 243, 255);
        Resources["MenuCheckBorder"] = isDarkMode
            ? CreateBrush(116, 174, 230)
            : CreateBrush(141, 188, 235);
        Resources["ThemeSwitchSunStroke"] = isDarkMode
            ? CreateBrush(213, 166, 83)
            : CreateBrush(198, 123, 18);
        Resources["ThemeSwitchMoonStroke"] = isDarkMode
            ? CreateBrush(147, 197, 253)
            : CreateBrush(107, 142, 184);
    }

    private Brush GetToolbarIconBrush()
    {
        return Resources["ToolbarControlForeground"] as Brush ?? Brushes.Black;
    }

    private static LinearGradientBrush CreateGradientBrush(
        (byte Red, byte Green, byte Blue) top,
        (byte Red, byte Green, byte Blue) middle,
        (byte Red, byte Green, byte Blue) bottom)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new WpfPoint(0, 0),
            EndPoint = new WpfPoint(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(top.Red, top.Green, top.Blue), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(middle.Red, middle.Green, middle.Blue), 0.52));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(bottom.Red, bottom.Green, bottom.Blue), 1));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

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
        var canDelete = CanDeleteCurrentImage();
        DeleteButton.IsEnabled = canDelete;
        DeleteMenuItem.IsEnabled = canDelete;
    }

    private void UpdateZoomText()
    {
        ZoomText.Text = _image is null
            ? string.Empty
            : $"{LocalizedText.Get(LocalizedText.Zoom)}: {_viewerState.Zoom * 100:0}%";
        ActualPixelsButton.IsEnabled = CanToggleActualPixels();
        UpdateActualPixelsIcon();
        UpdateToolbarDensity();
    }

    private bool CanToggleActualPixels()
    {
        return _image is not null && !_viewerState.FitsAtActualPixels;
    }

    private bool CanDeleteCurrentImage()
    {
        var path = _navigator.CurrentPath;
        return path is not null && File.Exists(path);
    }

    private void UpdateToolbarDensity()
    {
        if (ZoomText is null)
        {
            return;
        }

        ZoomText.Visibility = ActualWidth >= 680 && _image is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowStatus(string text)
    {
        HidePreviewOnlyBadge();
        UpdateStatusOverlayMaxWidth();
        StatusText.ToolTip = text;
        StatusText.Text = text;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        StatusOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateStatusOverlayMaxWidth()
    {
        StatusOverlay.MaxWidth = Math.Max(160, ImageSurface.ActualWidth - 80);
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
