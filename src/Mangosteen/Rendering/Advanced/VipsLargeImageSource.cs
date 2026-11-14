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
        var lease = LeaseLevel(path, level, token);
        try
        {
            return DecodeTileFromLevel(lease.Image.Value, pyramid, level, key, token);
        }
        finally
        {
            ReleaseLevel(lease);
        }
    }

    private static ImageTileData DecodeTileFromLevel(
        VipsImage levelImage,
        ImagePyramid pyramid,
        ImagePyramidLevel level,
        ImageTileKey key,
        CancellationToken token)
    {
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

    /// <summary>
    /// Drops every cached reduced level. Called when the image is closed so a parked
    /// background window does not keep up to 192 MB of decoded pixels alive.
    /// </summary>
    public void ReleaseCachedData() => ClearLevels();

    public void ClearLevels()
    {
        lock (_levelGate)
        {
            for (var node = _levels.First; node is not null; node = node.Next) Evict(node.Value);
            _levels.Clear();
        }
    }

    internal int CachedLevelCount
    {
        get
        {
            lock (_levelGate) return _levels.Count;
        }
    }

    private CachedLevel LeaseLevel(string path, ImagePyramidLevel level, CancellationToken token)
    {
        var file = new FileInfo(path);
        var identity = (Path.GetFullPath(path), file.Length, file.LastWriteTimeUtc, level.Index, level.Width, level.Height);
        CachedLevel entry;
        lock (_levelGate)
        {
            var node = _levels.First;
            while (node is not null && node.Value.Identity != identity) node = node.Next;
            if (node is not null)
            {
                _levels.Remove(node);
                _levels.AddFirst(node);
                entry = node.Value;
            }
            else
            {
                entry = new CachedLevel(
                    identity,
                    new Lazy<VipsImage>(() => CreateLevel(path, level), LazyThreadSafetyMode.ExecutionAndPublication),
                    EstimateLevelBytes(level));
                _levels.AddFirst(entry);
                TrimLevels();
            }

            entry.Leases++;
        }

        try
        {
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            ReleaseLevel(entry);
            throw;
        }

        try
        {
            _ = entry.Image.Value;
            return entry;
        }
        catch
        {
            lock (_levelGate)
            {
                // Lazy caches the exception, so a failed level must leave the cache to be retried.
                _levels.Remove(entry);
            }

            ReleaseLevel(entry);
            throw;
        }
    }

    private void ReleaseLevel(CachedLevel entry)
    {
        lock (_levelGate)
        {
            entry.Leases--;
            if (entry.Evicted) DisposeIfUnused(entry);
        }
    }

    private static void Evict(CachedLevel entry)
    {
        entry.Evicted = true;
        DisposeIfUnused(entry);
    }

    // Tiles still cropping on another thread hold a lease; the image is freed when the last one ends.
    private static void DisposeIfUnused(CachedLevel entry)
    {
        if (entry.Leases == 0 && entry.Image.IsValueCreated) entry.Image.Value.Dispose();
    }

    internal static VipsImage CreateLevel(string path, ImagePyramidLevel level)
    {
        using (var header = VipsImage.NewFromFile(path, access: Enums.Access.Sequential, failOn: Enums.FailOn.None))
        {
            // Thumbnail always returns 8-bit sRGB-style pixels and converts CMYK with a different
            // rendering intent than the full-resolution tiles. Those sources are reduced here
            // instead, so every zoom level keeps the source bit depth and colour handling.
            if (GetBitsPerChannel(header.Format) > 8 || header.Interpretation == Enums.Interpretation.Cmyk)
            {
                return CreateLevelPreservingFormat(path, level);
            }
        }

        // Thumbnail auto-rotates like the full-resolution path and keeps the ICC profile.
        using var thumbnail = VipsImage.Thumbnail(
            path,
            level.Width,
            height: level.Height,
            size: Enums.Size.Force,
            failOn: Enums.FailOn.None);
        return thumbnail.CopyMemory();
    }

    private static VipsImage CreateLevelPreservingFormat(string path, ImagePyramidLevel level)
    {
        // Random access: rotating a sideways image reads it out of order.
        using var source = Open(path);
        using var oriented = source.Autorot();
        var format = oriented.Format;
        using var resized = oriented.Resize(
            level.Width / (double)oriented.Width,
            kernel: Enums.Kernel.Lanczos3,
            vscale: level.Height / (double)oriented.Height);
        // Lanczos can change the band format of integer images; restore the source format.
        using var cast = resized.Format == format ? resized.Copy() : resized.Cast(format);
        using var fitted = cast.Width > level.Width || cast.Height > level.Height
            ? cast.Crop(0, 0, Math.Min(cast.Width, level.Width), Math.Min(cast.Height, level.Height))
            : cast.Copy();
        return fitted.CopyMemory();
    }

    private void TrimLevels()
    {
        var total = 0L;
        for (var node = _levels.First; node is not null;)
        {
            var next = node.Next;
            total += node.Value.EstimatedBytes;
            if (total > MaximumCachedLevelsBytes && node != _levels.First)
            {
                Evict(node.Value);
                _levels.Remove(node);
            }

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

    private sealed class CachedLevel(
        (string Path, long Length, DateTime LastWrite, int Level, int Width, int Height) identity,
        Lazy<VipsImage> image,
        long estimatedBytes)
    {
        public (string Path, long Length, DateTime LastWrite, int Level, int Width, int Height) Identity { get; } = identity;
        public Lazy<VipsImage> Image { get; } = image;
        public long EstimatedBytes { get; } = estimatedBytes;
        // Both guarded by _levelGate.
        public int Leases { get; set; }
        public bool Evicted { get; set; }
    }

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
