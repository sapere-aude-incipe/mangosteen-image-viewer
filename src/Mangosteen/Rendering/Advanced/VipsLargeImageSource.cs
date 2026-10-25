using NetVips;
using Mangosteen.Decoding;
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

internal sealed class VipsLargeImageSource : ILargeImageSource
{
    private const int LanczosFilterMarginPixels = 3;
    // v2: reduced levels come from a shrink-on-load thumbnail instead of per-tile Lanczos.
    public const string DecoderVersion = "libvips-8.18-mangosteen-tiles-v2";
    /// <summary>Reduced levels up to this decoded size are built once and kept in memory.</summary>
    internal const long MaximumCachedLevelBytes = 96L * 1024 * 1024;
    private const long MaximumCachedLevelsBytes = 192L * 1024 * 1024;

    private readonly object _levelGate = new();
    private readonly LinkedList<CachedLevel> _levels = new();

    public Task<LargeImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var source = Open(path);
            using var oriented = source.Autorot();
            token.ThrowIfCancellationRequested();
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

    private ImageTileData DecodeTile(
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
        if (level.Downsample > 1 && EstimateLevelBytes(level) <= MaximumCachedLevelBytes)
        {
            return DecodeTileFromCachedLevel(path, pyramid, level, key, token);
        }

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
        using var colorManaged = VipsDisplayColor.ConvertToSrgb(expanded, preserveHighBitDepth: true);
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
        return ToTileData(filteredTile, key, sourceWidth, sourceHeight, token);
    }

    /// <summary>
    /// Opening the full image per tile made each fit-zoom tile of a 100 MP JPEG cost ~250 ms.
    /// A reduced level is decoded once with libvips' shrink-on-load thumbnail (JPEG, WebP,
    /// HEIF and pyramidal TIFF decode directly at the smaller size) and tiles are cropped from it.
    /// </summary>
    private ImageTileData DecodeTileFromCachedLevel(
        string path,
        ImagePyramid pyramid,
        ImagePyramidLevel level,
        ImageTileKey key,
        CancellationToken token)
    {
        var levelImage = GetOrCreateLevel(path, level, token);
        var left = checked(key.X * pyramid.TileSize);
        var top = checked(key.Y * pyramid.TileSize);
        if (left >= levelImage.Width || top >= levelImage.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "The tile lies outside the image.");
        }

        var width = Math.Min(pyramid.TileSize, levelImage.Width - left);
        var height = Math.Min(pyramid.TileSize, levelImage.Height - top);
        var sourceSpan = checked(pyramid.TileSize * level.Downsample);
        var fullWidth = pyramid.Levels[0].Width;
        var fullHeight = pyramid.Levels[0].Height;
        var sourceWidth = Math.Min(sourceSpan, fullWidth - left * level.Downsample);
        var sourceHeight = Math.Min(sourceSpan, fullHeight - top * level.Downsample);
        using var tile = levelImage.Crop(left, top, width, height);
        using var colorManaged = VipsDisplayColor.ConvertToSrgb(tile, preserveHighBitDepth: true);
        return ToTileData(colorManaged, key, sourceWidth, sourceHeight, token);
    }

    private VipsImage GetOrCreateLevel(string path, ImagePyramidLevel level, CancellationToken token)
    {
        var file = new FileInfo(path);
        var identity = (Path.GetFullPath(path), file.Length, file.LastWriteTimeUtc, level.Index, level.Width, level.Height);
        Lazy<VipsImage> entry;
        lock (_levelGate)
        {
            var node = _levels.First;
            while (node is not null && node.Value.Identity != identity) node = node.Next;
            if (node is not null)
            {
                _levels.Remove(node);
                _levels.AddFirst(node);
                entry = node.Value.Image;
            }
            else
            {
                entry = new Lazy<VipsImage>(() => CreateLevel(path, level), LazyThreadSafetyMode.ExecutionAndPublication);
                _levels.AddFirst(new CachedLevel(identity, entry, EstimateLevelBytes(level)));
                TrimLevels();
            }
        }

        token.ThrowIfCancellationRequested();
        try
        {
            return entry.Value;
        }
        catch
        {
            lock (_levelGate)
            {
                var node = _levels.First;
                while (node is not null && !ReferenceEquals(node.Value.Image, entry)) node = node.Next;
                if (node is not null) _levels.Remove(node);
            }
            throw;
        }
    }

    private static VipsImage CreateLevel(string path, ImagePyramidLevel level)
    {
        // Thumbnail auto-rotates like the full-resolution path and keeps the ICC profile.
        using var thumbnail = VipsImage.Thumbnail(
            path,
            level.Width,
            height: level.Height,
            size: Enums.Size.Force,
            failOn: Enums.FailOn.None);
        return thumbnail.CopyMemory();
    }

    private void TrimLevels()
    {
        // Evicted images may still be cropping on another thread; let the finalizer release them.
        var total = 0L;
        for (var node = _levels.First; node is not null;)
        {
            var next = node.Next;
            total += node.Value.EstimatedBytes;
            if (total > MaximumCachedLevelsBytes && node != _levels.First) _levels.Remove(node);
            node = next;
        }
    }

    internal static long EstimateLevelBytes(ImagePyramidLevel level)
    {
        // Up to four 16-bit channels.
        return (long)level.Width * level.Height * 8;
    }

    private static ImageTileData ToTileData(VipsImage filteredTile, ImageTileKey key, int sourceWidth, int sourceHeight, CancellationToken token)
    {
        using var rgba = EnsureRgba(filteredTile);
        token.ThrowIfCancellationRequested();

        var highBitDepth = GetBitsPerChannel(rgba.Format) > 8;
        using var pixelsImage = rgba.Format == (highBitDepth ? Enums.BandFormat.Ushort : Enums.BandFormat.Uchar)
            ? rgba.Copy()
            : rgba.Cast(highBitDepth ? Enums.BandFormat.Ushort : Enums.BandFormat.Uchar);
        var pixels = highBitDepth
            ? MemoryMarshal.AsBytes(pixelsImage.WriteToMemory<ushort>().AsSpan()).ToArray()
            : pixelsImage.WriteToMemory<byte>();
        token.ThrowIfCancellationRequested();
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

    private sealed record CachedLevel(
        (string Path, long Length, DateTime LastWrite, int Level, int Width, int Height) Identity,
        Lazy<VipsImage> Image,
        long EstimatedBytes);

    private static VipsImage Open(string path)
    {
        return VipsImage.NewFromFile(path, access: Enums.Access.Random, failOn: Enums.FailOn.None);
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
