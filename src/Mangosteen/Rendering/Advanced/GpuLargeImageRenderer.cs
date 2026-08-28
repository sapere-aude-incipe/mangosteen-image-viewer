using Mangosteen.Decoding;
using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Mangosteen.Rendering.Advanced;

internal sealed class GpuLargeImageRenderer : IDisposable
{
    private const long TextureBudgetBytes = 384L * 1024 * 1024;
    private readonly NativeGlHost _host;
    private readonly VipsLargeImageSource _source = new();
    private readonly PersistentTileCache _cache = new();
    private readonly SemaphoreSlim _decodeGate = new(2, 2);
    private readonly Dictionary<ImageTileKey, TextureEntry> _textures = [];
    private readonly HashSet<ImageTileKey> _pendingTiles = [];
    private CancellationTokenSource _loadCts = new();
    private ImagePyramid? _pyramid;
    private ViewerState? _viewState;
    private string? _path;
    private string? _sourceKey;
    private byte[]? _previewPixels;
    private int _previewWidth;
    private int _previewHeight;
    private uint _previewTexture;
    private int _generation;
    private bool _smoothSampling = true;
    private SKColor _background = new(33, 33, 33);
    private bool _disposed;

    public GpuLargeImageRenderer(NativeGlHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _host.SurfaceReady += Host_SurfaceReady;
        _host.SurfaceSizeChanged += Host_SurfaceSizeChanged;
    }

    public NativeGlHost Host => _host;

    public async Task<bool> OpenAsync(
        string path,
        ImageMetadata metadata,
        SKImage preview,
        ViewerState viewerState,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(viewerState);

        var largeMetadata = await _source.LoadMetadataAsync(path, token);
        if (largeMetadata.Width != metadata.Width || largeMetadata.Height != metadata.Height)
        {
            return false;
        }

        CloseCurrent();
        _path = path;
        _sourceKey = _cache.CreateSourceKey(path, VipsLargeImageSource.DecoderVersion);
        _pyramid = new ImagePyramid(metadata.Width, metadata.Height);
        _viewState = viewerState;
        (_previewPixels, _previewWidth, _previewHeight) = CopyPreviewPixels(preview);
        if (_host.IsSurfaceReady)
        {
            UploadPreviewTexture();
            Render();
        }

        return true;
    }

    public void UpdateView(SKColor background, bool smoothSampling)
    {
        _background = background;
        _smoothSampling = smoothSampling;
        Render();
    }

    public void Render()
    {
        if (_disposed || _path is null || _pyramid is null || _viewState is null || !_host.IsSurfaceReady)
        {
            return;
        }

        _host.Render(RenderCurrentView);
        QueueVisibleTiles();
    }

    public void CloseCurrent()
    {
        _generation++;
        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
        _pendingTiles.Clear();
        if (_host.IsSurfaceReady)
        {
            _host.Render(DeleteTextures);
        }
        else
        {
            _textures.Clear();
            _previewTexture = 0;
        }

        _path = null;
        _sourceKey = null;
        _pyramid = null;
        _viewState = null;
        _previewPixels = null;
        _previewWidth = 0;
        _previewHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseCurrent();
        _loadCts.Dispose();
        _host.SurfaceReady -= Host_SurfaceReady;
        _host.SurfaceSizeChanged -= Host_SurfaceSizeChanged;
    }

    private void Host_SurfaceReady(object? sender, EventArgs e)
    {
        if (_previewPixels is not null)
        {
            UploadPreviewTexture();
            Render();
        }
    }

    private void Host_SurfaceSizeChanged(object? sender, EventArgs e)
    {
        Render();
    }

    private void RenderCurrentView()
    {
        var width = _host.PixelWidth;
        var height = _host.PixelHeight;
        OpenGl11.Viewport(0, 0, width, height);
        OpenGl11.ClearColor(_background.Red / 255f, _background.Green / 255f, _background.Blue / 255f, 1f);
        OpenGl11.Clear(OpenGl11.ColorBufferBit | OpenGl11.DepthBufferBit);
        OpenGl11.Disable(OpenGl11.DepthTest);
        OpenGl11.Enable(OpenGl11.Texture2D);
        OpenGl11.Enable(OpenGl11.Blend);
        OpenGl11.BlendFunc(OpenGl11.SrcAlpha, OpenGl11.OneMinusSrcAlpha);
        OpenGl11.MatrixMode(OpenGl11.Projection);
        OpenGl11.LoadIdentity();
        OpenGl11.Ortho(0, width, height, 0, -1, 1);
        OpenGl11.MatrixMode(OpenGl11.ModelView);
        OpenGl11.LoadIdentity();
        OpenGl11.Color4(1, 1, 1, 1);

        var destination = _viewState!.GetDestinationRect();
        if (_previewTexture != 0)
        {
            DrawTexture(_previewTexture, destination.Left, destination.Top, destination.Right, destination.Bottom);
        }

        var level = _pyramid!.ChooseLevel(_viewState.Zoom);
        var sourceSpan = _pyramid.TileSize * level.Downsample;
        foreach (var pair in _textures.Where(pair => pair.Key.Level == level.Index))
        {
            var tile = pair.Value;
            var sourceLeft = pair.Key.X * sourceSpan;
            var sourceTop = pair.Key.Y * sourceSpan;
            var left = destination.Left + (float)(sourceLeft * _viewState.Zoom);
            var top = destination.Top + (float)(sourceTop * _viewState.Zoom);
            var right = left + (float)(tile.SourceWidth * _viewState.Zoom);
            var bottom = top + (float)(tile.SourceHeight * _viewState.Zoom);
            DrawSolidRect(left, top, right, bottom, _background);
            DrawTexture(tile.Texture, left, top, right, bottom);
            tile.LastUse = Environment.TickCount64;
        }
    }

    private void QueueVisibleTiles()
    {
        var destination = _viewState!.GetDestinationRect();
        var zoom = Math.Max(_viewState.Zoom, 0.000001);
        var sourceLeft = Math.Max(0, -destination.Left / zoom);
        var sourceTop = Math.Max(0, -destination.Top / zoom);
        var sourceRight = Math.Min(_pyramid!.Levels[0].Width, (_host.PixelWidth - destination.Left) / zoom);
        var sourceBottom = Math.Min(_pyramid.Levels[0].Height, (_host.PixelHeight - destination.Top) / zoom);
        var level = _pyramid.ChooseLevel(zoom);
        var generation = _generation;
        var token = _loadCts.Token;
        foreach (var key in _pyramid.GetTilesForSourceRect(level, sourceLeft, sourceTop, sourceRight, sourceBottom))
        {
            if (_textures.ContainsKey(key) || !_pendingTiles.Add(key)) continue;
            _ = LoadTileAsync(key, generation, token);
        }
    }

    private async Task LoadTileAsync(ImageTileKey key, int generation, CancellationToken token)
    {
        try
        {
            await _decodeGate.WaitAsync(token);
            ImageTileData? tile;
            try
            {
                tile = await _cache.TryReadAsync(_sourceKey!, key, token);
                if (tile is null)
                {
                    tile = await _source.DecodeTileAsync(_path!, _pyramid!, key, token);
                    await _cache.WriteAsync(_sourceKey!, tile, token);
                }
            }
            finally
            {
                _decodeGate.Release();
            }

            await _host.Dispatcher.InvokeAsync(() =>
            {
                _pendingTiles.Remove(key);
                if (_disposed || generation != _generation || token.IsCancellationRequested || !_host.IsSurfaceReady)
                {
                    return;
                }

                _host.ExecuteWithContext(() =>
                {
                    var texture = UploadTexture(tile);
                    _textures[key] = new TextureEntry(texture, tile.SourceWidth, tile.SourceHeight, tile.EstimatedBytes);
                    TrimTextureCache();
                });
                Render();
            }, DispatcherPriority.Render, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (_disposed) return;
            try
            {
                await _host.Dispatcher.InvokeAsync(() => _pendingTiles.Remove(key), DispatcherPriority.Background);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    private void UploadPreviewTexture()
    {
        if (_previewPixels is null || !_host.IsSurfaceReady) return;
        _host.ExecuteWithContext(() =>
        {
            if (_previewTexture != 0) OpenGl11.DeleteTextures(1, ref _previewTexture);
            _previewTexture = UploadTexture(new ImageTileData(
                new ImageTileKey(0, 0, 0),
                _previewWidth,
                _previewHeight,
                _pyramid?.Levels[0].Width ?? _previewWidth,
                _pyramid?.Levels[0].Height ?? _previewHeight,
                ImageTilePixelFormat.Rgba8,
                _previewPixels));
        });
    }

    private uint UploadTexture(ImageTileData tile)
    {
        if (!tile.HasExpectedPixelLength())
        {
            throw new InvalidDataException("A large-image tile contained an invalid pixel payload.");
        }

        OpenGl11.GenTextures(1, out var texture);
        OpenGl11.BindTexture(OpenGl11.Texture2D, texture);
        SetTextureSampling();
        var handle = GCHandle.Alloc(tile.Pixels, GCHandleType.Pinned);
        try
        {
            OpenGl11.TexImage2D(
                OpenGl11.Texture2D,
                0,
                (int)(tile.PixelFormat == ImageTilePixelFormat.Rgba16 ? OpenGl11.Rgba16 : OpenGl11.Rgba8),
                tile.Width,
                tile.Height,
                0,
                OpenGl11.Rgba,
                tile.PixelFormat == ImageTilePixelFormat.Rgba16 ? OpenGl11.UnsignedShort : OpenGl11.UnsignedByte,
                handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }

        return texture;
    }

    private void DrawTexture(uint texture, float left, float top, float right, float bottom)
    {
        OpenGl11.BindTexture(OpenGl11.Texture2D, texture);
        SetTextureSampling();
        OpenGl11.Begin(OpenGl11.Quads);
        OpenGl11.TexCoord2(0, 0); OpenGl11.Vertex2(left, top);
        OpenGl11.TexCoord2(1, 0); OpenGl11.Vertex2(right, top);
        OpenGl11.TexCoord2(1, 1); OpenGl11.Vertex2(right, bottom);
        OpenGl11.TexCoord2(0, 1); OpenGl11.Vertex2(left, bottom);
        OpenGl11.End();
    }

    private static void DrawSolidRect(float left, float top, float right, float bottom, SKColor color)
    {
        OpenGl11.Disable(OpenGl11.Texture2D);
        OpenGl11.Color4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, 1f);
        OpenGl11.Begin(OpenGl11.Quads);
        OpenGl11.Vertex2(left, top);
        OpenGl11.Vertex2(right, top);
        OpenGl11.Vertex2(right, bottom);
        OpenGl11.Vertex2(left, bottom);
        OpenGl11.End();
        OpenGl11.Color4(1, 1, 1, 1);
        OpenGl11.Enable(OpenGl11.Texture2D);
    }

    private void SetTextureSampling()
    {
        var filter = _smoothSampling ? OpenGl11.Linear : OpenGl11.Nearest;
        OpenGl11.TexParameteri(OpenGl11.Texture2D, OpenGl11.TextureMinFilter, filter);
        OpenGl11.TexParameteri(OpenGl11.Texture2D, OpenGl11.TextureMagFilter, filter);
        OpenGl11.TexParameteri(OpenGl11.Texture2D, OpenGl11.TextureWrapS, OpenGl11.ClampToEdge);
        OpenGl11.TexParameteri(OpenGl11.Texture2D, OpenGl11.TextureWrapT, OpenGl11.ClampToEdge);
    }

    private void TrimTextureCache()
    {
        var total = _textures.Values.Sum(texture => texture.EstimatedBytes);
        foreach (var key in _textures.OrderBy(pair => pair.Value.LastUse).Select(pair => pair.Key).ToArray())
        {
            if (total <= TextureBudgetBytes) break;
            var entry = _textures[key];
            var texture = entry.Texture;
            OpenGl11.DeleteTextures(1, ref texture);
            _textures.Remove(key);
            total -= entry.EstimatedBytes;
        }
    }

    private void DeleteTextures()
    {
        foreach (var entry in _textures.Values)
        {
            var texture = entry.Texture;
            OpenGl11.DeleteTextures(1, ref texture);
        }
        _textures.Clear();
        if (_previewTexture != 0)
        {
            OpenGl11.DeleteTextures(1, ref _previewTexture);
            _previewTexture = 0;
        }
    }

    private static (byte[] Pixels, int Width, int Height) CopyPreviewPixels(SKImage image)
    {
        using var source = SKBitmap.FromImage(image);
        using var rgba = source.Copy(SKColorType.Rgba8888);
        var byteCount = checked(rgba.RowBytes * rgba.Height);
        var pixels = new byte[byteCount];
        Marshal.Copy(rgba.GetPixels(), pixels, 0, pixels.Length);
        if (rgba.AlphaType == SKAlphaType.Premul)
        {
            UnpremultiplyRgba(pixels, rgba.Width, rgba.Height, rgba.RowBytes);
        }
        return (pixels, rgba.Width, rgba.Height);
    }

    internal static void UnpremultiplyRgba(byte[] pixels, int width, int height, int rowBytes)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || rowBytes < width * 4 || pixels.Length < rowBytes * height)
        {
            throw new ArgumentOutOfRangeException(nameof(rowBytes));
        }

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + x * 4;
                var alpha = pixels[offset + 3];
                if (alpha == 0)
                {
                    pixels[offset] = 0;
                    pixels[offset + 1] = 0;
                    pixels[offset + 2] = 0;
                    continue;
                }

                if (alpha == byte.MaxValue)
                {
                    continue;
                }

                pixels[offset] = UnpremultiplyChannel(pixels[offset], alpha);
                pixels[offset + 1] = UnpremultiplyChannel(pixels[offset + 1], alpha);
                pixels[offset + 2] = UnpremultiplyChannel(pixels[offset + 2], alpha);
            }
        }
    }

    private static byte UnpremultiplyChannel(byte channel, byte alpha)
    {
        return (byte)Math.Min(byte.MaxValue, (channel * byte.MaxValue + alpha / 2) / alpha);
    }

    private sealed class TextureEntry(uint texture, int sourceWidth, int sourceHeight, long estimatedBytes)
    {
        public uint Texture { get; } = texture;
        public int SourceWidth { get; } = sourceWidth;
        public int SourceHeight { get; } = sourceHeight;
        public long EstimatedBytes { get; } = estimatedBytes;
        public long LastUse { get; set; } = Environment.TickCount64;
    }
}
