using NetVips;
using System.IO;
using System.Runtime.InteropServices;
using VipsImage = NetVips.Image;

namespace Mangosteen.Rendering.Advanced;

internal readonly record struct LargeImageMetadata(
    int Width,
    int Height,
    int Bands,
    int BitsPerChannel,
    bool HasEmbeddedColorProfile);

internal sealed class VipsLargeImageSource
{
    private const int LanczosFilterMarginPixels = 3;
    public const string DecoderVersion = "libvips-8.18-mangosteen-tiles-v1";

    public Task<LargeImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var source = Open(path);
            using var oriented = source.Autorot();
            return new LargeImageMetadata(
                oriented.Width,
                oriented.Height,
                oriented.Bands,
                GetBitsPerChannel(oriented.Format),
                oriented.GetTypeOf("icc-profile-data") != 0);
        }, token);
    }

    public Task<ImageTileData> DecodeTileAsync(
        string path,
        ImagePyramid pyramid,
        ImageTileKey key,
        CancellationToken token)
    {
        return Task.Run(() => DecodeTile(path, pyramid, key, token), token);
    }

    private static ImageTileData DecodeTile(
        string path,
        ImagePyramid pyramid,
        ImageTileKey key,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (key.Level < 0 || key.Level >= pyramid.Levels.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        var level = pyramid.Levels[key.Level];
        var sourceSpan = checked(pyramid.TileSize * level.Downsample);
        using var source = Open(path);
        using var oriented = source.Autorot();
        var left = checked(key.X * sourceSpan);
        var top = checked(key.Y * sourceSpan);
        if (left >= oriented.Width || top >= oriented.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "The tile lies outside the image.");
        }

        var sourceWidth = Math.Min(sourceSpan, oriented.Width - left);
        var sourceHeight = Math.Min(sourceSpan, oriented.Height - top);
        var sourceMargin = level.Downsample == 1
            ? 0
            : checked(LanczosFilterMarginPixels * level.Downsample);
        var expandedLeft = Math.Max(0, left - sourceMargin);
        var expandedTop = Math.Max(0, top - sourceMargin);
        var expandedRight = Math.Min(oriented.Width, checked(left + sourceWidth + sourceMargin));
        var expandedBottom = Math.Min(oriented.Height, checked(top + sourceHeight + sourceMargin));
        using var expanded = oriented.Crop(
            expandedLeft,
            expandedTop,
            expandedRight - expandedLeft,
            expandedBottom - expandedTop);
        using var colorManaged = ConvertToSrgb(expanded);
        using var sized = level.Downsample == 1
            ? colorManaged.Copy()
            : colorManaged.Resize(
                1.0 / level.Downsample,
                kernel: Enums.Kernel.Lanczos3,
                vscale: 1.0 / level.Downsample);
        var outputLeft = (left - expandedLeft) / level.Downsample;
        var outputTop = (top - expandedTop) / level.Downsample;
        var desiredWidth = Math.Max(1, (sourceWidth + level.Downsample - 1) / level.Downsample);
        var desiredHeight = Math.Max(1, (sourceHeight + level.Downsample - 1) / level.Downsample);
        var outputWidth = Math.Min(desiredWidth, sized.Width - outputLeft);
        var outputHeight = Math.Min(desiredHeight, sized.Height - outputTop);
        if (outputWidth <= 0 || outputHeight <= 0)
        {
            throw new InvalidDataException("libvips returned an invalid overlapped tile size.");
        }

        using var filteredTile = sized.Crop(outputLeft, outputTop, outputWidth, outputHeight);
        using var rgba = EnsureRgba(filteredTile);
        token.ThrowIfCancellationRequested();

        var highBitDepth = GetBitsPerChannel(rgba.Format) > 8;
        using var pixelsImage = rgba.Format == (highBitDepth ? Enums.BandFormat.Ushort : Enums.BandFormat.Uchar)
            ? rgba.Copy()
            : rgba.Cast(highBitDepth ? Enums.BandFormat.Ushort : Enums.BandFormat.Uchar);
        var pixels = highBitDepth
            ? MemoryMarshal.AsBytes(pixelsImage.WriteToMemory<ushort>().AsSpan()).ToArray()
            : pixelsImage.WriteToMemory<byte>();
        var bytesPerChannel = highBitDepth ? 2 : 1;
        var expectedLength = checked(pixelsImage.Width * pixelsImage.Height * 4 * bytesPerChannel);
        if (pixels.Length != expectedLength)
        {
            throw new InvalidDataException("libvips returned an unexpected large-image tile buffer size.");
        }

        return new ImageTileData(
            key,
            pixelsImage.Width,
            pixelsImage.Height,
            sourceWidth,
            sourceHeight,
            highBitDepth ? ImageTilePixelFormat.Rgba16 : ImageTilePixelFormat.Rgba8,
            pixels);
    }

    private static VipsImage Open(string path)
    {
        return VipsImage.NewFromFile(path, access: Enums.Access.Random, failOn: Enums.FailOn.None);
    }

    private static VipsImage ConvertToSrgb(VipsImage image)
    {
        if (image.GetTypeOf("icc-profile-data") != 0)
        {
            try
            {
                return image.IccTransform(
                    "srgb",
                    intent: Enums.Intent.Relative,
                    blackPointCompensation: true,
                    embedded: true,
                    depth: GetBitsPerChannel(image.Format) > 8 ? 16 : 8);
            }
            catch (VipsException)
            {
                // Some loaders expose an unusable profile. The regular color-space
                // conversion is still preferable to failing the entire viewer.
            }
        }

        var targetInterpretation = GetBitsPerChannel(image.Format) > 8
            ? Enums.Interpretation.Rgb16
            : Enums.Interpretation.Srgb;
        return image.Interpretation == targetInterpretation ||
            (targetInterpretation == Enums.Interpretation.Rgb16 &&
                image.Interpretation == Enums.Interpretation.Srgb &&
                image.Format == Enums.BandFormat.Ushort)
            ? image.Copy()
            : image.Colourspace(targetInterpretation);
    }

    private static VipsImage EnsureRgba(VipsImage image)
    {
        var maximum = GetBitsPerChannel(image.Format) > 8 ? 65535d : 255d;
        return image.Bands switch
        {
            4 => image.Copy(),
            3 => image.Bandjoin(maximum),
            > 4 => ExtractFirstFourBands(image),
            _ => ConvertMonochromeToRgba(image, maximum)
        };
    }

    private static VipsImage ExtractFirstFourBands(VipsImage image)
    {
        using var bands = image.ExtractBand(0, n: 4);
        return bands.Copy();
    }

    private static VipsImage ConvertMonochromeToRgba(VipsImage image, double maximum)
    {
        var targetInterpretation = GetBitsPerChannel(image.Format) > 8
            ? Enums.Interpretation.Rgb16
            : Enums.Interpretation.Srgb;
        using var srgb = image.Colourspace(targetInterpretation);
        return srgb.Bands == 3 ? srgb.Bandjoin(maximum) : ExtractFirstFourBands(srgb);
    }

    internal static int GetBitsPerChannel(Enums.BandFormat format)
    {
        return format switch
        {
            Enums.BandFormat.Uchar or Enums.BandFormat.Char => 8,
            Enums.BandFormat.Ushort or Enums.BandFormat.Short => 16,
            Enums.BandFormat.Uint or Enums.BandFormat.Int or Enums.BandFormat.Float => 32,
            Enums.BandFormat.Double or Enums.BandFormat.Complex or Enums.BandFormat.Dpcomplex => 64,
            _ => 8
        };
    }
}
