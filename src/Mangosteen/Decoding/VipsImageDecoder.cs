using Mangosteen.Core;
using NetVips;
using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;
using VipsImage = NetVips.Image;

namespace Mangosteen.Decoding;

public sealed class VipsImageDecoder : IImageDecoder
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".3fr", ".ari", ".arw", ".avif", ".bay", ".bmp", ".cap", ".cr2",
            ".cr3", ".crw", ".dcr", ".dcs", ".dng", ".drf", ".erf", ".exif",
            ".exr", ".fff", ".gpr", ".hdr", ".heic", ".heif", ".jfif", ".jpe",
            ".jpeg", ".jpg", ".jxl", ".k25", ".kdc", ".mdc", ".mef", ".mos",
            ".mrw", ".nef", ".nrw", ".orf", ".pef", ".png", ".psb", ".psd",
            ".ptx", ".pxn", ".raf", ".raw", ".rw2", ".rwl", ".sr2", ".srf",
            ".srw", ".svg", ".svgz", ".tif", ".tiff", ".webp", ".x3f"
        };

    public string Name => "libvips";

    public int Priority => 250;

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
            using var image = Open(path, access: Enums.Access.Sequential);
            var normalized = NormalizeForViewer(image, decodeForPixels: false);
            try
            {
                token.ThrowIfCancellationRequested();
                return new ImageMetadata(path, normalized.Width, normalized.Height, 1, Name);
            }
            finally
            {
                if (!ReferenceEquals(normalized, image))
                {
                    normalized.Dispose();
                }
            }
        }, token);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var metadataImage = Open(request.Path, access: Enums.Access.Sequential);
            var normalizedMetadata = NormalizeForViewer(metadataImage, decodeForPixels: false);
            try
            {
                var metadata = new ImageMetadata(request.Path, normalizedMetadata.Width, normalizedMetadata.Height, 1, Name);
                var target = ImageDecodeSizing.GetTargetSize(metadata.Width, metadata.Height, request.TargetPreviewSize, request.FullResolution);
                var isFull = ImageDecodeSizing.IsFullResolution(request, target, metadata, animated: false);
                if (!isFull && ShouldDecodeFullInsteadOfPreview(request, metadataImage, metadata))
                {
                    // These loaders cannot shrink on load, so a thumbnail decodes everything and
                    // the full-resolution pass decodes it again. Show the full image now.
                    target = new PixelSize(metadata.Width, metadata.Height);
                    isFull = true;
                }

                ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                    metadata,
                    target,
                    decodesAllFrames: true,
                    request.MaxDecodedBytes);

                using var decodeImage = OpenForDecode(request.Path, target, isFull);
                using var normalized = NormalizeForViewer(decodeImage, decodeForPixels: true);
                using var sized = ResizeToTarget(normalized, target);
                var skiaImage = CreateSkiaImage(sized, request.MaxDecodedBytes, token);
                return new DecodedImage(metadata, [new DecodedFrame(skiaImage, TimeSpan.FromMilliseconds(100))], isFull);
            }
            finally
            {
                if (!ReferenceEquals(normalizedMetadata, metadataImage))
                {
                    normalizedMetadata.Dispose();
                }
            }
        }, token);
    }

    private static readonly HashSet<string> FullDecodeOnlyLoaders = new(StringComparer.Ordinal)
    {
        "pngload", "heifload", "tiffload"
    };

    private static bool ShouldDecodeFullInsteadOfPreview(ImageDecodeRequest request, VipsImage source, ImageMetadata metadata)
    {
        if (!request.PreferFullResolutionWhenUnscalable ||
            (long)metadata.Width * metadata.Height * 4 > SkiaImageDecoder.MaximumDirectFullDecodeBytes ||
            request.MaxDecodedBytes is { } limit && (long)metadata.Width * metadata.Height * 4 > limit ||
            source.GetTypeOf("vips-loader") == 0 ||
            !FullDecodeOnlyLoaders.Contains((string)source.Get("vips-loader")))
        {
            return false;
        }

        // Pyramidal TIFFs carry reduced subimages that thumbnails read cheaply.
        return source.GetTypeOf("n-subifds") == 0 || source.Get("n-subifds") is not int subifds || subifds == 0;
    }

    internal static bool UsesDecoderSidePreview(ImageDecodeRequest request, ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);

        if (request.FullResolution)
        {
            return false;
        }

        var target = ImageDecodeSizing.GetTargetSize(metadata.Width, metadata.Height, request.TargetPreviewSize, fullResolution: false);
        return target.Width < metadata.Width || target.Height < metadata.Height;
    }

    private static VipsImage OpenForDecode(string path, PixelSize target, bool fullResolution)
    {
        if (fullResolution)
        {
            return Open(path, access: Enums.Access.Sequential);
        }

        // libvips thumbnail uses loader-side shrink paths where the codec supports them
        // (notably large JPEG/TIFF), avoiding full BGRA materialization for previews.
        return VipsImage.Thumbnail(
            path,
            target.Width,
            height: target.Height,
            size: Enums.Size.Down,
            noRotate: false,
            failOn: Enums.FailOn.None);
    }

    private static VipsImage Open(string path, Enums.Access access)
    {
        return VipsImage.NewFromFile(
            path,
            access: access,
            failOn: Enums.FailOn.None);
    }

    private static VipsImage NormalizeForViewer(VipsImage image, bool decodeForPixels)
    {
        var current = image;
        var disposeCurrent = false;
        try
        {
            var autorot = current.Autorot();
            if (!ReferenceEquals(autorot, current) && disposeCurrent)
            {
                current.Dispose();
            }
            current = autorot;
            disposeCurrent = true;

            var colour = decodeForPixels
                ? VipsDisplayColor.ConvertToSrgb(current, preserveHighBitDepth: false)
                : current.Copy();

            if (!ReferenceEquals(colour, current) && disposeCurrent)
            {
                current.Dispose();
            }
            current = colour;

            if (decodeForPixels && current.Format != Enums.BandFormat.Uchar)
            {
                var cast = current.Cast(Enums.BandFormat.Uchar);
                current.Dispose();
                current = cast;
            }

            disposeCurrent = false;
            return current;
        }
        catch
        {
            if (disposeCurrent)
            {
                current.Dispose();
            }

            throw;
        }
    }

    private static VipsImage ResizeToTarget(VipsImage image, PixelSize target)
    {
        if (image.Width == target.Width && image.Height == target.Height)
        {
            return image.Copy();
        }

        var xScale = target.Width / (double)Math.Max(1, image.Width);
        var yScale = target.Height / (double)Math.Max(1, image.Height);
        return image.Resize(xScale, kernel: Enums.Kernel.Lanczos3, vscale: yScale);
    }

    private static SKImage CreateSkiaImage(VipsImage image, long? maxDecodedBytes, CancellationToken token)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException("libvips returned an empty image.");
        }

        ImageDecodeGuards.ThrowIfSingleFrameDecodedBytesExceedLimit(image.Width, image.Height, maxDecodedBytes);
        ImageDecodeGuards.ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(image.Width, image.Height);

        // Without a source alpha band the filled-in alpha is constant; Opaque spares Skia the
        // premultiply work on every paint.
        var alphaType = image.Bands == 3 ? SKAlphaType.Opaque : SKAlphaType.Unpremul;
        var rgba = EnsureRgba(image);
        try
        {
            // Wrap libvips' native buffer directly: copying it into a zero-filled, pinned managed
            // array cost an extra full-size allocation and copy (about 400 MB for 100 MP).
            var pixels = WriteToMemory(rgba, out var size, token);
            if (pixels == IntPtr.Zero)
            {
                throw new InvalidDataException("libvips could not write the decoded pixels.");
            }

            var ownsPixels = true;
            try
            {
                var expectedLength = checked((ulong)rgba.Width * (ulong)rgba.Height * 4);
                if (size != expectedLength)
                {
                    throw new InvalidDataException("libvips returned an unexpected pixel buffer size.");
                }

                var info = new SKImageInfo(rgba.Width, rgba.Height, SKColorType.Rgba8888, alphaType);
                using var pixmap = new SKPixmap(info, pixels, info.RowBytes);
                var skiaImage = SKImage.FromPixels(pixmap, static (address, _) => NetVips.NetVips.Free(address));
                ownsPixels = skiaImage is null;
                return skiaImage ?? throw new InvalidDataException("SkiaSharp could not wrap the decoded libvips pixels.");
            }
            finally
            {
                if (ownsPixels) NetVips.NetVips.Free(pixels);
            }
        }
        finally
        {
            if (!ReferenceEquals(rgba, image))
            {
                rgba.Dispose();
            }
        }
    }

    /// <summary>For tests: called just before libvips evaluates the decoded pixels.</summary>
    internal static readonly AsyncLocal<Action?> BeforePixelEvaluation = new();

    /// <summary>
    /// Evaluates the pipeline, aborting it promptly when the load is cancelled. libvips checks the
    /// image's kill flag between work units, so a stale 100 MP decode stops within a few tiles
    /// instead of holding the full-decode gate for a second or more.
    /// </summary>
    private static IntPtr WriteToMemory(VipsImage image, out ulong size, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        BeforePixelEvaluation.Value?.Invoke();
        var registration = token.Register(static state => ((VipsImage)state!).SetKill(true), image);
        try
        {
            var pixels = image.WriteToMemory(out size);
            if (token.IsCancellationRequested)
            {
                // Small images can finish before libvips looks at the flag.
                NetVips.NetVips.Free(pixels);
                throw new OperationCanceledException(token);
            }

            return pixels;
        }
        catch (VipsException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        finally
        {
            // Dispose waits for a running callback, so the flag cannot be set after it is cleared.
            // The image can be a cached libvips operation result, and a later decode of the same
            // file must not inherit the kill flag.
            registration.Dispose();
            if (token.IsCancellationRequested)
            {
                image.SetKill(false);
            }
        }
    }

    private static VipsImage EnsureRgba(VipsImage image)
    {
        if (image.Bands == 4)
        {
            return image.Copy();
        }

        if (image.Bands == 3)
        {
            return image.Bandjoin(255);
        }

        if (image.Bands > 4)
        {
            using var firstFourBands = image.ExtractBand(0, n: 4);
            return firstFourBands.Copy();
        }

        throw new InvalidDataException($"libvips returned an unsupported band count: {image.Bands}.");
    }
}
