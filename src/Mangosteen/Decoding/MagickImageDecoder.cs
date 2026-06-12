using Mangosteen.Core;
using ImageMagick;
using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;

namespace Mangosteen.Decoding;

public sealed class MagickImageDecoder : IImageDecoder
{
    private static readonly SemaphoreSlim ResourceLimitGate = new(1, 1);

    // Initializing Magick.NET loads its ~25 MB native library; deferred to first decode
    // so app startup does not pay for it.
    private static readonly Lazy<bool> NativeRuntime = new(
        static () =>
        {
            MagickNET.Initialize();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static void EnsureNativeRuntime()
    {
        _ = NativeRuntime.Value;
    }

    public string Name => "Magick.NET";

    public int Priority => 10;

    public IReadOnlyCollection<string> SupportedExtensions => ImageFileExtensions.BroadImageExtensions;

    public bool CanDecode(string path)
    {
        return File.Exists(path) && SupportedExtensions.Contains(ImageFileExtensions.NormalizeExtension(path));
    }

    public async Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        await ResourceLimitGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                EnsureNativeRuntime();
                return LoadMetadata(path, token);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            ResourceLimitGate.Release();
        }
    }

    public async Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        await ResourceLimitGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                EnsureNativeRuntime();
                using var resourceLimits = MagickResourceLimitScope.Enter(request.MaxDecodedBytes);
                token.ThrowIfCancellationRequested();
                var animated = IsAnimatedFormat(request.Path);
                var basicInfo = LoadBasicImageInfo(request.Path, token);
                var metadata = basicInfo.Metadata;
                var width = metadata.Width;
                var height = metadata.Height;
                var target = ImageDecodeSizing.GetTargetSize(width, height, request.TargetPreviewSize, request.FullResolution);
                var isFull = ImageDecodeSizing.IsFullResolution(request, target, metadata, animated);
                var guardMetadata = request.MaxDecodedBytes.HasValue && animated && isFull
                    ? metadata with { FrameCount = LoadFrameCount(request.Path, token) }
                    : metadata;
                ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                    guardMetadata,
                    target,
                    decodesAllFrames: animated && isFull,
                    request.MaxDecodedBytes);

                using var images = new MagickImageCollection();
                var settings = new MagickReadSettings();
                if (!isFull)
                {
                    var readTarget = ImageOrientation.GetDecodeTarget(target, basicInfo.Origin);
                    settings.Width = (uint)readTarget.Width;
                    settings.Height = (uint)readTarget.Height;
                }

                if (!animated || !isFull)
                {
                    settings.FrameCount = 1;
                }

                images.Read(request.Path, settings);

                if (images.Count == 0)
                {
                    throw new InvalidDataException("Magick.NET did not return any image frames.");
                }

                if (animated && images.Count > 1)
                {
                    images.Coalesce();
                }

                var frames = new List<DecodedFrame>(images.Count);
                try
                {
                    foreach (var frame in images)
                    {
                        token.ThrowIfCancellationRequested();
                        frame.AutoOrient();

                        if (!isFull)
                        {
                            frame.Resize(new MagickGeometry((uint)target.Width, (uint)target.Height)
                            {
                                Greater = true
                            });
                        }

                        var delay = GetFrameDelay(frame);
                        var image = CreateSkiaImage(frame, request.MaxDecodedBytes);
                        frames.Add(new DecodedFrame(image, delay));
                    }

                    return DecodedFrameOwnership.CreateImageOrDisposeFrames(metadata with { FrameCount = frames.Count }, frames, isFull);
                }
                catch
                {
                    DecodedFrameOwnership.DisposeAll(frames);
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        finally
        {
            ResourceLimitGate.Release();
        }
    }

    private ImageMetadata LoadMetadata(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var images = new MagickImageCollection();
        images.Ping(path);
        if (images.Count == 0)
        {
            throw new InvalidDataException("Magick.NET did not return any image frames.");
        }

        var origin = ImageOrientation.FromExifOrientation((int)images[0].Orientation);
        var size = ImageOrientation.GetOrientedSize(
            ToInt32Dimension(images[0].Width, "width"),
            ToInt32Dimension(images[0].Height, "height"),
            origin);
        var frameCount = IsAnimatedFormat(path) ? Math.Max(1, images.Count) : 1;

        return new ImageMetadata(path, size.Width, size.Height, frameCount, Name);
    }

    private BasicImageInfo LoadBasicImageInfo(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var images = new MagickImageCollection();
        images.Ping(path, new MagickReadSettings
        {
            FrameCount = 1
        });
        if (images.Count == 0)
        {
            throw new InvalidDataException("Magick.NET did not return any image frames.");
        }

        var origin = ImageOrientation.FromExifOrientation((int)images[0].Orientation);
        var size = ImageOrientation.GetOrientedSize(
            ToInt32Dimension(images[0].Width, "width"),
            ToInt32Dimension(images[0].Height, "height"),
            origin);
        var metadata = new ImageMetadata(path, size.Width, size.Height, 1, Name);

        return new BasicImageInfo(metadata, origin);
    }

    private sealed record BasicImageInfo(ImageMetadata Metadata, SKEncodedOrigin Origin);

    private int LoadFrameCount(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var images = new MagickImageCollection();
        images.Ping(path);
        if (images.Count == 0)
        {
            throw new InvalidDataException("Magick.NET did not return any image frames.");
        }

        return Math.Max(1, images.Count);
    }

    internal static TimeSpan GetFrameDelay(IMagickImage frame)
    {
        var ticksPerSecond = frame.AnimationTicksPerSecond > 0 ? frame.AnimationTicksPerSecond : 100;
        var milliseconds = frame.AnimationDelay > 0
            ? frame.AnimationDelay * 1000.0 / ticksPerSecond
            : 100;
        return TimeSpan.FromMilliseconds(Math.Max(20, milliseconds));
    }

    private static SKImage CreateSkiaImage(IMagickImage frame, long? maxDecodedBytes)
    {
        var width = ToInt32Dimension(frame.Width, "width");
        var height = ToInt32Dimension(frame.Height, "height");
        ImageDecodeGuards.ThrowIfSingleFrameDecodedBytesExceedLimit(width, height, maxDecodedBytes);
        ImageDecodeGuards.ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(width, height);
        var pixels = ExportPixels(frame);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        PinnedPixelBuffer? pinnedPixels = new(pixels);
        try
        {
            using var pixmap = new SKPixmap(info, pinnedPixels.Pointer, info.RowBytes);
            var image = SKImage.FromPixels(pixmap, ReleasePinnedPixels, pinnedPixels);
            pinnedPixels = null;
            return image ?? throw new InvalidDataException("SkiaSharp could not wrap the decoded Magick.NET pixels.");
        }
        finally
        {
            pinnedPixels?.Dispose();
        }
    }

    private static void ReleasePinnedPixels(IntPtr address, object context)
    {
        if (context is PinnedPixelBuffer buffer)
        {
            buffer.Dispose();
        }
    }

    private sealed class PinnedPixelBuffer : IDisposable
    {
        private GCHandle _handle;

        public PinnedPixelBuffer(byte[] pixels)
        {
            _handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        }

        public IntPtr Pointer => _handle.AddrOfPinnedObject();

        public void Dispose()
        {
            if (!_handle.IsAllocated)
            {
                return;
            }

            _handle.Free();
        }
    }

    private static int ToInt32Dimension(uint value, string name)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"Magick.NET reported an image {name} that is too large to decode.");
        }

        return (int)value;
    }

    private static byte[] ExportPixels(IMagickImage frame)
    {
        return frame switch
        {
            IMagickImage<byte> image => ExportPixels(image),
            IMagickImage<ushort> image => ExportPixels(image),
            IMagickImage<float> image => ExportPixels(image),
            IMagickImage<double> image => ExportPixels(image),
            _ => throw new InvalidDataException("Magick.NET could not export image pixels for this quantum type.")
        };
    }

    private static byte[] ExportPixels<TQuantum>(IMagickImage<TQuantum> frame)
        where TQuantum : struct, IConvertible
    {
        return frame.GetPixels().ToByteArray(PixelMapping.BGRA)
            ?? throw new InvalidDataException("Magick.NET could not export image pixels.");
    }

    private static bool IsAnimatedFormat(string path)
    {
        return ImageFileExtensions.NormalizeExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }
}
