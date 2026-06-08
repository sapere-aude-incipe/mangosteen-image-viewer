using ClassicPhotoViewer.Core;
using SkiaSharp;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClassicPhotoViewer.Decoding;

public sealed class WicImageDecoder : IImageDecoder
{
    private const double EmbeddedPreviewAspectRatioTolerance = 1.10;

    private static readonly IReadOnlyCollection<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".3fr", ".arw", ".avif", ".bmp", ".cr2", ".cr3", ".crw", ".cur",
            ".dcr", ".dib", ".dng", ".erf", ".heic", ".heif", ".ico", ".jfif",
            ".jpe", ".jpeg", ".jpg", ".jxr", ".kdc", ".mef", ".mos", ".mrw",
            ".nef", ".nrw", ".orf", ".pef", ".png", ".raf", ".raw", ".rw2",
            ".rwl", ".sr2", ".srf", ".srw", ".tif", ".tiff", ".wdp", ".webp"
        };

    public string Name => "Windows Imaging Component";

    public int Priority => 200;

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    internal static BitmapCacheOption InitialDecodeCacheOption => BitmapCacheOption.OnDemand;

    public bool CanDecode(string path)
    {
        return File.Exists(path) && Extensions.Contains(ImageFileExtensions.NormalizeExtension(path));
    }

    public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = OpenRead(path);
            var decoder = CreateDecoder(stream, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidDataException("WIC did not return any image frames.");
            var origin = GetOrientation(frame);
            var size = ImageOrientation.GetOrientedSize(frame.PixelWidth, frame.PixelHeight, origin);

            var frameCount = IsAnimatedFormat(path) ? Math.Max(1, decoder.Frames.Count) : 1;

            return new ImageMetadata(path, size.Width, size.Height, frameCount, Name);
        }, token);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = OpenRead(request.Path);
            // Keep WIC on-demand until the memory guard below has accepted the decode.
            var decoder = CreateDecoder(stream, InitialDecodeCacheOption);
            var firstFrame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidDataException("WIC did not return any image frames.");
            var origin = GetOrientation(firstFrame);
            var size = ImageOrientation.GetOrientedSize(firstFrame.PixelWidth, firstFrame.PixelHeight, origin);

            var metadata = new ImageMetadata(
                request.Path,
                size.Width,
                size.Height,
                IsAnimatedFormat(request.Path) ? Math.Max(1, decoder.Frames.Count) : 1,
                Name);
            var animated = IsAnimatedFormat(request.Path);
            var target = ImageDecodeSizing.GetTargetSize(metadata.Width, metadata.Height, request.TargetPreviewSize, request.FullResolution);
            var isFull = ImageDecodeSizing.IsFullResolution(request, target, metadata, animated);
            ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                metadata,
                target,
                decodesAllFrames: animated && isFull,
                request.MaxDecodedBytes);

            if (!isFull)
            {
                var decodeTarget = ImageOrientation.GetDecodeTarget(target, origin);
                var preview = TryCreateEmbeddedPreview(decoder, target, origin) ?? CreateScaledBitmapImage(request.Path, decodeTarget);
                var image = CreateSkiaImage(preview, request.MaxDecodedBytes);
                image = ImageOrientation.ApplyAndDisposeSource(image, origin);
                return new DecodedImage(metadata with { FrameCount = 1 }, [new DecodedFrame(image, TimeSpan.FromMilliseconds(100))], isFullResolution: false);
            }

            var sourceFrames = animated ? decoder.Frames : decoder.Frames.Take(1);
            var frames = new List<DecodedFrame>(animated ? decoder.Frames.Count : 1);
            try
            {
                foreach (var frame in sourceFrames)
                {
                    token.ThrowIfCancellationRequested();
                    var delay = GetFrameDelay(frame);
                    var image = CreateSkiaImage(frame, request.MaxDecodedBytes);
                    image = ImageOrientation.ApplyAndDisposeSource(image, GetOrientation(frame));
                    frames.Add(new DecodedFrame(image, delay));
                }

                return DecodedFrameOwnership.CreateImageOrDisposeFrames(
                    metadata with { FrameCount = frames.Count },
                    frames,
                    isFullResolution: true);
            }
            catch
            {
                DecodedFrameOwnership.DisposeAll(frames);
                throw;
            }
        }, token);
    }

    internal static FileStream OpenRead(string path)
    {
        return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    internal static BitmapDecoder CreateDecoder(Stream stream, BitmapCacheOption cacheOption)
    {
        return BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            cacheOption);
    }

    private static BitmapSource? TryCreateEmbeddedPreview(BitmapDecoder decoder, PixelSize target, SKEncodedOrigin origin)
    {
        var source = decoder.Thumbnail ?? decoder.Frames.FirstOrDefault()?.Thumbnail;
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return null;
        }

        var sourceSize = ImageOrientation.GetOrientedSize(source.PixelWidth, source.PixelHeight, origin);
        if (!IsEmbeddedPreviewUsable(sourceSize, target))
        {
            return null;
        }

        source = FitEmbeddedPreviewToTarget(source, target, origin);

        if (source.CanFreeze)
        {
            source.Freeze();
        }
        return source;
    }

    internal static bool IsEmbeddedPreviewUsable(PixelSize sourceSize, PixelSize target)
    {
        if (sourceSize.IsEmpty || target.IsEmpty)
        {
            return false;
        }

        var tooSmall = sourceSize.Width < target.Width / 4 || sourceSize.Height < target.Height / 4;
        var tooLarge = sourceSize.Width > target.Width * 2 || sourceSize.Height > target.Height * 2;
        var sourceAspect = (double)sourceSize.Width / sourceSize.Height;
        var targetAspect = (double)target.Width / target.Height;
        var aspectRatio = Math.Max(sourceAspect, targetAspect) / Math.Min(sourceAspect, targetAspect);
        return !tooSmall && !tooLarge && aspectRatio <= EmbeddedPreviewAspectRatioTolerance;
    }

    internal static BitmapSource FitEmbeddedPreviewToTarget(BitmapSource source, PixelSize target, SKEncodedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (target.IsEmpty || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return source;
        }

        var orientedSourceSize = ImageOrientation.GetOrientedSize(source.PixelWidth, source.PixelHeight, origin);
        var orientedTargetSize = ImageDecodeSizing.GetTargetSize(
            orientedSourceSize.Width,
            orientedSourceSize.Height,
            target,
            fullResolution: false);
        if (orientedTargetSize.Width >= orientedSourceSize.Width &&
            orientedTargetSize.Height >= orientedSourceSize.Height)
        {
            return source;
        }

        var rawTargetSize = ImageOrientation.GetDecodeTarget(orientedTargetSize, origin);
        if (rawTargetSize.Width == source.PixelWidth && rawTargetSize.Height == source.PixelHeight)
        {
            return source;
        }

        var scaled = new TransformedBitmap(
            source,
            new ScaleTransform(
                rawTargetSize.Width / (double)source.PixelWidth,
                rawTargetSize.Height / (double)source.PixelHeight));
        if (scaled.CanFreeze)
        {
            scaled.Freeze();
        }

        return scaled;
    }

    private static BitmapSource CreateScaledBitmapImage(string path, PixelSize target)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.DecodePixelWidth = target.Width;
        bitmap.DecodePixelHeight = target.Height;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal static SKEncodedOrigin GetOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return SKEncodedOrigin.TopLeft;
        }

        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                var value = metadata.GetQuery(query);
                if (value is not null)
                {
                    return ImageOrientation.FromExifOrientation(Convert.ToInt32(value));
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or InvalidCastException)
            {
            }
        }

        return SKEncodedOrigin.TopLeft;
    }

    internal static SKImage CreateSkiaImage(BitmapSource source, long? maxDecodedBytes)
    {
        BitmapSource converted = source.Format == System.Windows.Media.PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);

        if (converted.CanFreeze)
        {
            converted.Freeze();
        }

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        ImageDecodeGuards.ThrowIfSingleFrameDecodedBytesExceedLimit(width, height, maxDecodedBytes);
        var stride = ImageDecodeGuards.GetBgraStride(width);
        var bufferLength = ImageDecodeGuards.GetBgraBufferLength(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            throw new InvalidDataException("SkiaSharp could not allocate a pixel buffer for the decoded WIC image.");
        }

        converted.CopyPixels(new Int32Rect(0, 0, width, height), pixels, bufferLength, stride);
        return SKImage.FromBitmap(bitmap);
    }

    private static TimeSpan GetFrameDelay(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return TimeSpan.FromMilliseconds(100);
        }

        try
        {
            if (metadata.GetQuery("/grctlext/Delay") is ushort gifDelay && gifDelay > 0)
            {
                return TimeSpan.FromMilliseconds(Math.Max(20, gifDelay * 10));
            }
        }
        catch (NotSupportedException)
        {
        }

        return TimeSpan.FromMilliseconds(100);
    }

    private static bool IsAnimatedFormat(string path)
    {
        return ImageFileExtensions.NormalizeExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }
}
