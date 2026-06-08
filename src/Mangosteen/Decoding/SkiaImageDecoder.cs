using Mangosteen.Core;
using SkiaSharp;
using System.IO;

namespace Mangosteen.Decoding;

public sealed class SkiaImageDecoder : IImageDecoder
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp", ".ico", ".jfif", ".jpe", ".jpeg", ".jpg", ".png", ".webp"
        };

    public string Name => "SkiaSharp";

    public int Priority => 100;

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanDecode(string path)
    {
        return File.Exists(path) && Extensions.Contains(ImageFileExtensions.NormalizeExtension(path));
    }

    public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("SkiaSharp could not read image metadata.");
            var size = ImageOrientation.GetOrientedSize(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin);

            return new ImageMetadata(path, size.Width, size.Height, 1, Name);
        }, token);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var codec = SKCodec.Create(request.Path) ?? throw new InvalidDataException("SkiaSharp could not decode the image.");
            var size = ImageOrientation.GetOrientedSize(codec.Info.Width, codec.Info.Height, codec.EncodedOrigin);
            var metadata = new ImageMetadata(request.Path, size.Width, size.Height, 1, Name);
            var target = ImageDecodeSizing.GetTargetSize(metadata.Width, metadata.Height, request.TargetPreviewSize, request.FullResolution);
            var isFull = ImageDecodeSizing.IsFullResolution(request, target, metadata, animated: false);
            ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                metadata,
                target,
                decodesAllFrames: true,
                request.MaxDecodedBytes);
            var decodeTarget = ImageOrientation.GetDecodeTarget(target, codec.EncodedOrigin);
            var skiaDecodeSize = GetSkiaDecodeSize(codec, decodeTarget, isFull);
            ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                metadata,
                skiaDecodeSize,
                decodesAllFrames: true,
                request.MaxDecodedBytes);
            var image = DecodeImage(codec, request.Path, decodeTarget, skiaDecodeSize, allowFullBitmapFallback: isFull);
            image = ImageOrientation.ApplyAndDisposeSource(image, codec.EncodedOrigin);

            return new DecodedImage(metadata, [new DecodedFrame(image, TimeSpan.FromMilliseconds(100))], isFull);
        }, token);
    }

    internal static PixelSize GetSkiaDecodeSize(SKCodec codec, PixelSize target, bool fullResolution)
    {
        var source = new PixelSize(codec.Info.Width, codec.Info.Height);
        if (fullResolution || target.Width >= source.Width && target.Height >= source.Height)
        {
            return source;
        }

        var desiredScale = Math.Min(
            target.Width / (float)Math.Max(1, source.Width),
            target.Height / (float)Math.Max(1, source.Height));
        if (desiredScale <= 0)
        {
            return source;
        }

        var scaled = codec.GetScaledDimensions(desiredScale);
        return scaled.Width <= 0 || scaled.Height <= 0
            ? source
            : new PixelSize(
                Math.Clamp(scaled.Width, 1, source.Width),
                Math.Clamp(scaled.Height, 1, source.Height));
    }

    private static SKImage DecodeImage(
        SKCodec codec,
        string path,
        PixelSize target,
        PixelSize decodeSize,
        bool allowFullBitmapFallback)
    {
        var info = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = CreateBitmapOrThrow(info, "the decoded Skia image");
        var result = codec.GetPixels(info, bitmap.GetPixels());

        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
        {
            return ScaleToTarget(bitmap, target);
        }

        if (!allowFullBitmapFallback)
        {
            throw new InvalidDataException($"SkiaSharp could not decode a scaled preview ({result}).");
        }

        using var full = SKBitmap.Decode(path) ?? throw new InvalidDataException("SkiaSharp could not decode the image.");
        return ScaleToTarget(full, target);
    }

    private static SKImage ScaleToTarget(SKBitmap source, PixelSize target)
    {
        if (source.Width == target.Width && source.Height == target.Height)
        {
            return SKImage.FromBitmap(source);
        }

        var info = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var scaled = CreateBitmapOrThrow(info, "the scaled Skia image");
        if (!source.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
        {
            throw new InvalidDataException("SkiaSharp could not scale the decoded image.");
        }

        return SKImage.FromBitmap(scaled);
    }

    internal static SKBitmap CreateBitmapOrThrow(SKImageInfo info, string context)
    {
        ImageDecodeGuards.ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(info.Width, info.Height);

        var bitmap = new SKBitmap(info);
        if (bitmap.GetPixels() != IntPtr.Zero)
        {
            return bitmap;
        }

        bitmap.Dispose();
        throw new InvalidDataException($"SkiaSharp could not allocate a pixel buffer for {context}.");
    }
}
