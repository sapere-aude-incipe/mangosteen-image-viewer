using Mangosteen.Core;
using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace Mangosteen.Decoding;

public sealed class WicGifImageDecoder : IImageDecoder
{
    private const string GifExtension = ".gif";
    private static readonly IReadOnlyCollection<string> Extensions = [GifExtension];

    public string Name => "Windows GIF";

    public int Priority => 300;

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanDecode(string path)
    {
        return File.Exists(path) &&
            ImageFileExtensions.NormalizeExtension(path).Equals(GifExtension, StringComparison.OrdinalIgnoreCase);
    }

    public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = WicImageDecoder.OpenRead(path);
            var decoder = WicImageDecoder.CreateDecoder(stream, BitmapCacheOption.OnDemand);
            var size = GetCanvasSize(decoder);
            return new ImageMetadata(path, size.Width, size.Height, Math.Max(1, decoder.Frames.Count), Name);
        }, token);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        return Task.Run(() => Decode(request, token), token);
    }

    private static DecodedImage Decode(ImageDecodeRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = WicImageDecoder.OpenRead(request.Path);
        var decoder = WicImageDecoder.CreateDecoder(stream, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("WIC did not return any GIF frames.");
        }

        var canvasSize = GetCanvasSize(decoder);
        var metadata = new ImageMetadata(
            request.Path,
            canvasSize.Width,
            canvasSize.Height,
            decoder.Frames.Count,
            "Windows GIF");
        var target = ImageDecodeSizing.GetTargetSize(
            metadata.Width,
            metadata.Height,
            request.TargetPreviewSize,
            request.FullResolution);
        ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
            metadata,
            target,
            decodesAllFrames: true,
            request.MaxDecodedBytes);

        var info = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidDataException("SkiaSharp could not allocate a GIF animation canvas.");
        var canvas = surface.Canvas;
        var backgroundColor = GetBackgroundColor(decoder);
        canvas.Clear(backgroundColor);

        var frames = new List<DecodedFrame>(decoder.Frames.Count);
        SKImage? restorePrevious = null;
        var previousDisposal = 0;
        var previousRect = SKRect.Empty;
        var previousClearColor = backgroundColor;
        try
        {
            foreach (var bitmapFrame in decoder.Frames)
            {
                token.ThrowIfCancellationRequested();
                ApplyDisposal(canvas, previousDisposal, previousRect, previousClearColor, restorePrevious);
                restorePrevious?.Dispose();
                restorePrevious = null;

                var frameRect = GetScaledFrameRect(bitmapFrame, metadata, target);
                var disposal = GetMetadataInt(bitmapFrame.Metadata, "/grctlext/Disposal");
                if (disposal == 3)
                {
                    restorePrevious = surface.Snapshot();
                }

                using var source = CreatePremultipliedSkiaImage(bitmapFrame, request.MaxDecodedBytes);
                canvas.DrawImage(
                    source,
                    frameRect,
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                canvas.Flush();
                frames.Add(new DecodedFrame(surface.Snapshot(), GetFrameDelay(bitmapFrame)));

                previousDisposal = disposal;
                previousRect = frameRect;
                previousClearColor = IsTransparent(bitmapFrame) ? SKColors.Transparent : backgroundColor;
            }

            return DecodedFrameOwnership.CreateImageOrDisposeFrames(
                metadata,
                frames,
                request.FullResolution || (target.Width == metadata.Width && target.Height == metadata.Height));
        }
        catch
        {
            DecodedFrameOwnership.DisposeAll(frames);
            throw;
        }
        finally
        {
            restorePrevious?.Dispose();
        }
    }

    private static void ApplyDisposal(
        SKCanvas canvas,
        int disposal,
        SKRect frameRect,
        SKColor clearColor,
        SKImage? restorePrevious)
    {
        if (disposal == 2 && !frameRect.IsEmpty)
        {
            using var clearPaint = new SKPaint
            {
                Color = clearColor,
                BlendMode = SKBlendMode.Src,
                IsAntialias = false
            };
            canvas.DrawRect(frameRect, clearPaint);
        }
        else if (disposal == 3 && restorePrevious is not null)
        {
            using var restorePaint = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(restorePrevious, 0, 0, restorePaint);
        }
    }

    internal static SKImage CreatePremultipliedSkiaImage(BitmapSource source, long? maxDecodedBytes)
    {
        BitmapSource converted = source.Format == System.Windows.Media.PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Pbgra32, null, 0);

        if (converted.CanFreeze)
        {
            converted.Freeze();
        }

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        ImageDecodeGuards.ThrowIfSingleFrameDecodedBytesExceedLimit(width, height, maxDecodedBytes);
        var stride = ImageDecodeGuards.GetBgraStride(width);
        var bufferLength = ImageDecodeGuards.GetBgraBufferLength(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            throw new InvalidDataException("SkiaSharp could not allocate a pixel buffer for the decoded GIF frame.");
        }

        converted.CopyPixels(
            new System.Windows.Int32Rect(0, 0, width, height),
            pixels,
            bufferLength,
            stride);
        return SKImage.FromBitmap(bitmap);
    }

    private static PixelSize GetCanvasSize(BitmapDecoder decoder)
    {
        var width = GetMetadataInt(decoder.Metadata, "/logscrdesc/Width");
        var height = GetMetadataInt(decoder.Metadata, "/logscrdesc/Height");
        if (width > 0 && height > 0)
        {
            return new PixelSize(width, height);
        }

        foreach (var frame in decoder.Frames)
        {
            width = Math.Max(width, GetMetadataInt(frame.Metadata, "/imgdesc/Left") + frame.PixelWidth);
            height = Math.Max(height, GetMetadataInt(frame.Metadata, "/imgdesc/Top") + frame.PixelHeight);
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The GIF has invalid canvas dimensions.");
        }

        return new PixelSize(width, height);
    }

    private static SKRect GetScaledFrameRect(BitmapFrame frame, ImageMetadata metadata, PixelSize target)
    {
        var left = Math.Max(0, GetMetadataInt(frame.Metadata, "/imgdesc/Left"));
        var top = Math.Max(0, GetMetadataInt(frame.Metadata, "/imgdesc/Top"));
        var scaleX = (double)target.Width / metadata.Width;
        var scaleY = (double)target.Height / metadata.Height;
        var scaledLeft = Math.Clamp((int)Math.Floor(left * scaleX), 0, target.Width);
        var scaledTop = Math.Clamp((int)Math.Floor(top * scaleY), 0, target.Height);
        var scaledRight = Math.Clamp((int)Math.Ceiling((left + frame.PixelWidth) * scaleX), scaledLeft, target.Width);
        var scaledBottom = Math.Clamp((int)Math.Ceiling((top + frame.PixelHeight) * scaleY), scaledTop, target.Height);
        return new SKRect(scaledLeft, scaledTop, scaledRight, scaledBottom);
    }

    private static SKColor GetBackgroundColor(BitmapDecoder decoder)
    {
        var index = GetMetadataInt(decoder.Metadata, "/logscrdesc/BackgroundColorIndex");
        var colors = decoder.Palette?.Colors;
        if (colors is null || index < 0 || index >= colors.Count)
        {
            return SKColors.Transparent;
        }

        var color = colors[index];
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    private static bool IsTransparent(BitmapFrame frame)
    {
        return GetMetadataValue(frame.Metadata, "/grctlext/TransparencyFlag") is true;
    }

    private static TimeSpan GetFrameDelay(BitmapFrame frame)
    {
        var hundredths = GetMetadataInt(frame.Metadata, "/grctlext/Delay");
        return hundredths > 0
            ? TimeSpan.FromMilliseconds(Math.Max(20, hundredths * 10))
            : TimeSpan.FromMilliseconds(100);
    }

    private static int GetMetadataInt(object? metadata, string query)
    {
        var value = GetMetadataValue(metadata, query);
        try
        {
            return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static object? GetMetadataValue(object? metadata, string query)
    {
        if (metadata is not BitmapMetadata bitmapMetadata)
        {
            return null;
        }

        try
        {
            return bitmapMetadata.GetQuery(query);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
