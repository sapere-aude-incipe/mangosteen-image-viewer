using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace Mangosteen.Rendering.Advanced;

internal sealed class F3dModelRenderer : IDisposable
{
    private readonly NativeGlHost _host;
    private readonly string _componentDirectory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _animationClock = new();
    private F3dNativeApi? _api;
    private nint _engine;
    private nint _window;
    private nint _scene;
    private nint _camera;
    private double _animationMinimum;
    private double _animationMaximum;
    private bool _hasAnimation;
    private bool _hasOpenScene;
    private int _generation;
    private bool _disposed;
    private bool _resourcesDisposed;
    private volatile bool _darkMode;

    public F3dModelRenderer(NativeGlHost host, string componentDirectory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _componentDirectory = componentDirectory ?? throw new ArgumentNullException(nameof(componentDirectory));
        _host.SurfaceSizeChanged += Host_SurfaceSizeChanged;
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    public bool IsOpen => _hasOpenScene;

    public async Task OpenAsync(string path, bool darkMode, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _darkMode = darkMode;
        _animationTimer.Stop();
        _hasAnimation = false;
        if (!_host.IsSurfaceReady)
        {
            await _host.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded, token);
        }

        token.ThrowIfCancellationRequested();
        if (!_host.IsSurfaceReady) throw new InvalidOperationException("The 3D rendering surface is not ready.");
        var generation = Interlocked.Increment(ref _generation);
        _hasOpenScene = false;
        await Task.Run(async () =>
        {
            await _operationGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || generation != Volatile.Read(ref _generation))
                {
                    throw new OperationCanceledException(token);
                }

                var rendered = _host.Render(() =>
                {
                    EnsureEngine(_darkMode);
                    _api!.ClearScene(_scene);
                    if (!_api.Supports(_scene, path) || !_api.AddSceneFile(_scene, path))
                    {
                        throw new NotSupportedException($"F3D could not load '{Path.GetFileName(path)}'.");
                    }

                    if (_disposed || token.IsCancellationRequested || generation != Volatile.Read(ref _generation))
                    {
                        _api.ClearScene(_scene);
                        return;
                    }

                    ResizeCore();
                    SetBackground(_darkMode);
                    _api.ResetToBounds(_camera, 0.68);
                    _api.Azimuth(_camera, -28);
                    _api.Elevation(_camera, 18);
                    ConfigureAnimation();
                    if (!_api.Render(_window))
                    {
                        throw new InvalidOperationException("F3D could not render the model.");
                    }
                });
                if (!rendered) throw new InvalidOperationException("The 3D rendering surface became unavailable.");

                token.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref _generation))
                {
                    throw new OperationCanceledException(token);
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }, token);

        token.ThrowIfCancellationRequested();
        if (_disposed || generation != Volatile.Read(ref _generation)) throw new OperationCanceledException(token);
        _hasOpenScene = true;
        ApplyTheme(_darkMode);

        if (_hasAnimation)
        {
            _animationClock.Restart();
            _animationTimer.Start();
        }
    }

    public void Orbit(double horizontalDegrees, double verticalDegrees)
    {
        RenderWithCamera(() =>
        {
            _api!.Azimuth(_camera, horizontalDegrees);
            _api.Elevation(_camera, verticalDegrees);
        });
    }

    public void Pan(double horizontal, double vertical)
    {
        RenderWithCamera(() => _api!.Pan(_camera, horizontal, vertical, 0));
    }

    public void Zoom(double factor)
    {
        RenderWithCamera(() => _api!.Dolly(_camera, factor));
    }

    public void ResetView()
    {
        RenderWithCamera(() => _api!.ResetToBounds(_camera, 0.68));
    }

    public void ApplyTheme(bool darkMode)
    {
        _darkMode = darkMode;
        RenderWithCamera(static () => { });
    }

    public void CloseCurrent()
    {
        var generation = Interlocked.Increment(ref _generation);
        _animationTimer.Stop();
        _animationClock.Reset();
        _hasAnimation = false;
        _hasOpenScene = false;
        _ = Task.Run(() => ClearSceneWhenIdleAsync(generation));
    }

    public void Dispose()
    {
        if (_resourcesDisposed) return;
        if (!_disposed) PrepareForDisposal();
        _operationGate.Wait();
        DisposeNativeResourcesWithGateHeld();
    }

    public bool TryDisposeWithoutBlocking()
    {
        if (_resourcesDisposed) return true;
        if (!_disposed) PrepareForDisposal();
        if (!_operationGate.Wait(0))
        {
            return false;
        }

        DisposeNativeResourcesWithGateHeld();
        return true;
    }

    private void PrepareForDisposal()
    {
        _disposed = true;
        Interlocked.Increment(ref _generation);
        _animationTimer.Stop();
        _animationTimer.Tick -= AnimationTimer_Tick;
        _host.SurfaceSizeChanged -= Host_SurfaceSizeChanged;
    }

    private void DisposeNativeResourcesWithGateHeld()
    {
        try
        {
            if (_api is not null && _engine != nint.Zero && _host.IsSurfaceReady)
            {
                _host.ExecuteWithContext(() => _api.DeleteEngine(_engine));
            }
        }
        finally
        {
            _engine = nint.Zero;
            _api?.Dispose();
            _api = null;
            _resourcesDisposed = true;
            _operationGate.Release();
            // Queued cleanup requests still need to acquire/release this gate.
        }
    }

    private void EnsureEngine(bool darkMode)
    {
        if (_engine != nint.Zero) return;
        try
        {
            InitializeEngine(darkMode);
        }
        catch
        {
            try
            {
                if (_engine != nint.Zero) _api?.DeleteEngine(_engine);
            }
            finally
            {
                _engine = _window = _scene = _camera = nint.Zero;
                _api?.Dispose();
                _api = null;
            }
            throw;
        }
    }

    private void InitializeEngine(bool darkMode)
    {
        _api = new F3dNativeApi(_componentDirectory);
        _engine = _api.CreateEngine();
        if (_engine == nint.Zero)
        {
            throw new InvalidOperationException("The optional F3D engine could not be created.");
        }

        var options = _api.GetOptions(_engine);
        if (options == nint.Zero) throw new InvalidOperationException("F3D did not expose its options.");
        _api.SetBool(options, "render.grid.enable", true);
        _api.SetBool(options, "render.grid.absolute", true);
        _api.SetBool(options, "ui.axis", true);
        _api.SetBool(options, "render.effect.ambient_occlusion", true);
        _window = _api.GetWindow(_engine);
        _scene = _api.GetScene(_engine);
        if (_window == nint.Zero || _scene == nint.Zero)
        {
            throw new InvalidOperationException("The optional F3D engine did not expose a renderable scene.");
        }
        _camera = _api.GetCamera(_window);
        if (_window == nint.Zero || _scene == nint.Zero || _camera == nint.Zero)
        {
            throw new InvalidOperationException("The optional F3D engine did not expose a renderable scene.");
        }
        SetBackground(darkMode);
    }

    private void SetBackground(bool darkMode)
    {
        var options = _api!.GetOptions(_engine);
        _api.SetDoubleVector(options, "render.background.color", darkMode
            ? [0.129, 0.129, 0.129]
            : [0.957, 0.965, 0.973]);
    }

    private void ConfigureAnimation()
    {
        _hasAnimation = _api!.AvailableAnimations(_scene) > 0;
        _animationMinimum = 0;
        _animationMaximum = 0;
        if (_hasAnimation)
        {
            _api.AnimationTimeRange(_scene, out _animationMinimum, out _animationMaximum);
            _hasAnimation = _animationMaximum > _animationMinimum;
        }
    }

    private void RenderWithCamera(Action cameraAction)
    {
        if (!IsOpen || _disposed || _api is null || _window == nint.Zero || !_operationGate.Wait(0)) return;
        try
        {
            _host.TryRender(() =>
            {
                SetBackground(_darkMode);
                cameraAction();
                _api!.Render(_window);
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ClearSceneWhenIdleAsync(int generation)
    {
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_disposed &&
                    generation == Volatile.Read(ref _generation) &&
                    _api is not null &&
                    _scene != nint.Zero &&
                    _host.IsSurfaceReady)
                {
                    _host.ExecuteWithContext(() => _api.ClearScene(_scene));
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"F3D scene cleanup failed: {ex}");
        }
    }

    private void Host_SurfaceSizeChanged(object? sender, EventArgs e)
    {
        RenderWithCamera(ResizeCore);
    }

    private void ResizeCore()
    {
        _api!.SetWindowSize(_window, Math.Max(1, _host.PixelWidth), Math.Max(1, _host.PixelHeight));
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_hasAnimation || !IsOpen) return;
        var duration = _animationMaximum - _animationMinimum;
        var time = _animationMinimum + (_animationClock.Elapsed.TotalSeconds % duration);
        RenderWithCamera(() => _api!.LoadAnimationTime(_scene, time));
    }
}
