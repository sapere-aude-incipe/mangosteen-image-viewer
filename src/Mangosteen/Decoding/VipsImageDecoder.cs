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
                ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                    metadata,
                    target,
                    decodesAllFrames: true,
                    request.MaxDecodedBytes);

                using var decodeImage = OpenForDecode(request.Path, target, isFull);
                using var normalized = NormalizeForViewer(decodeImage, decodeForPixels: true);
                using var sized = ResizeToTarget(normalized, target);
                var skiaImage = CreateSkiaImage(sized, request.MaxDecodedBytes);
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

            VipsImage colour;
            if (current.Bands <= 1)
            {
                colour = current.Colourspace(Enums.Interpretation.Srgb);
            }
            else if (current.Interpretation != Enums.Interpretation.Srgb)
            {
                colour = current.Colourspace(Enums.Interpretation.Srgb);
            }
            else
            {
                colour = current.Copy();
            }

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

    private static SKImage CreateSkiaImage(VipsImage image, long? maxDecodedBytes)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException("libvips returned an empty image.");
        }

        ImageDecodeGuards.ThrowIfSingleFrameDecodedBytesExceedLimit(image.Width, image.Height, maxDecodedBytes);
        ImageDecodeGuards.ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(image.Width, image.Height);

        var rgba = EnsureRgba(image);
        try
        {
            var pixels = rgba.WriteToMemory<byte>();
            var bgra = ConvertRgbaToBgra(pixels, rgba.Width, rgba.Height);
            var info = new SKImageInfo(rgba.Width, rgba.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            PinnedPixelBuffer? pinnedPixels = new(bgra);
            try
            {
                using var pixmap = new SKPixmap(info, pinnedPixels.Pointer, info.RowBytes);
                var skiaImage = SKImage.FromPixels(pixmap, ReleasePinnedPixels, pinnedPixels);
                pinnedPixels = null;
                return skiaImage ?? throw new InvalidDataException("SkiaSharp could not wrap the decoded libvips pixels.");
            }
            finally
            {
                pinnedPixels?.Dispose();
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

    private static byte[] ConvertRgbaToBgra(byte[] rgba, int width, int height)
    {
        var expectedLength = checked(width * height * 4);
        if (rgba.Length != expectedLength)
        {
            throw new InvalidDataException("libvips returned an unexpected pixel buffer size.");
        }

        var bgra = new byte[rgba.Length];
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            bgra[offset] = rgba[offset + 2];
            bgra[offset + 1] = rgba[offset + 1];
            bgra[offset + 2] = rgba[offset];
            bgra[offset + 3] = rgba[offset + 3];
        }

        return bgra;
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
}
