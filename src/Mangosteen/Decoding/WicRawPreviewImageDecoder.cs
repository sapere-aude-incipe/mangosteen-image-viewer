using Mangosteen.Core;
using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace Mangosteen.Decoding;

public sealed class WicRawPreviewImageDecoder : IImageDecoder
{
    private const double EmbeddedPreviewAspectRatioTolerance = 1.10;

    public string Name => "WIC embedded RAW preview";

    public int Priority => 300;

    public IReadOnlyCollection<string> SupportedExtensions => ImageFileExtensions.RawImageExtensions;

    public bool CanDecode(string path)
    {
        return File.Exists(path) && SupportedExtensions.Contains(ImageFileExtensions.NormalizeExtension(path));
    }

    public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = WicImageDecoder.OpenRead(path);
            var decoder = WicImageDecoder.CreateDecoder(stream, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidDataException("WIC did not return any RAW image frames.");
            var origin = WicImageDecoder.GetOrientation(frame);
            var size = ImageOrientation.GetOrientedSize(frame.PixelWidth, frame.PixelHeight, origin);

            return new ImageMetadata(path, size.Width, size.Height, 1, Name);
        }, token);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (request.FullResolution)
            {
                throw new InvalidDataException("WIC embedded RAW preview decoder does not perform full-resolution RAW decode.");
            }

            using var stream = WicImageDecoder.OpenRead(request.Path);
            var decoder = WicImageDecoder.CreateDecoder(stream, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames.FirstOrDefault()
                ?? throw new InvalidDataException("WIC did not return any RAW image frames.");
            var origin = WicImageDecoder.GetOrientation(frame);
            var metadataSize = ImageOrientation.GetOrientedSize(frame.PixelWidth, frame.PixelHeight, origin);
            var metadata = new ImageMetadata(request.Path, metadataSize.Width, metadataSize.Height, 1, Name);
            var target = ImageDecodeSizing.GetTargetSize(metadata.Width, metadata.Height, request.TargetPreviewSize, fullResolution: false);
            var embedded = CreateEmbeddedPreview(decoder, target, origin);
            var image = WicImageDecoder.CreateSkiaImage(embedded, request.MaxDecodedBytes);
            image = ImageOrientation.ApplyAndDisposeSource(image, origin);

            return new DecodedImage(metadata, [new DecodedFrame(image, TimeSpan.FromMilliseconds(100))], isFullResolution: false);
        }, token);
    }

    private static BitmapSource CreateEmbeddedPreview(BitmapDecoder decoder, PixelSize target, SKEncodedOrigin origin)
    {
        var source = decoder.Thumbnail ?? decoder.Frames.FirstOrDefault()?.Thumbnail
            ?? throw new InvalidDataException("WIC did not expose an embedded RAW preview.");
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            throw new InvalidDataException("WIC exposed an empty embedded RAW preview.");
        }

        var sourceSize = ImageOrientation.GetOrientedSize(source.PixelWidth, source.PixelHeight, origin);
        if (!IsEmbeddedRawPreviewUsable(sourceSize, target))
        {
            throw new InvalidDataException("WIC embedded RAW preview was too small or had a mismatched aspect ratio.");
        }

        return WicImageDecoder.FitEmbeddedPreviewToTarget(source, target, origin);
    }

    internal static bool IsEmbeddedRawPreviewUsable(PixelSize sourceSize, PixelSize target)
    {
        if (sourceSize.IsEmpty || target.IsEmpty)
        {
            return false;
        }

        var tooSmall = sourceSize.Width < target.Width / 4 || sourceSize.Height < target.Height / 4;
        var sourceAspect = (double)sourceSize.Width / sourceSize.Height;
        var targetAspect = (double)target.Width / target.Height;
        var aspectRatio = Math.Max(sourceAspect, targetAspect) / Math.Min(sourceAspect, targetAspect);
        return !tooSmall && aspectRatio <= EmbeddedPreviewAspectRatioTolerance;
    }
}
