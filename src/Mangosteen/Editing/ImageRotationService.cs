using ImageMagick;
using Mangosteen.Core;
using Mangosteen.Decoding;
using System.IO;

namespace Mangosteen.Editing;

internal enum RotationWriteRisk
{
    Lossless,
    Lossy,
    PngCopyOnly
}

internal enum RotationSaveMode
{
    ReplaceOriginal,
    PngCopy
}

internal sealed class ImageRotationService
{
    private static readonly HashSet<string> PotentiallyLossyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".heic", ".heif", ".jfif", ".jpe", ".jpeg", ".jpg", ".jxl", ".webp"
    };

    private static readonly HashSet<string> PngCopyOnlyExtensions = new(
        ImageFileExtensions.RawImageExtensions,
        StringComparer.OrdinalIgnoreCase)
    {
        ".psb", ".psd", ".svg", ".svgz", ".wmf"
    };

    public async Task<RotationWriteRisk> GetWriteRiskAsync(string path, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = ImageFileExtensions.NormalizeExtension(path);
        if (PngCopyOnlyExtensions.Contains(extension))
        {
            return RotationWriteRisk.PngCopyOnly;
        }

        if (PotentiallyLossyExtensions.Contains(extension))
        {
            return RotationWriteRisk.Lossy;
        }

        if (IsAnimatedPng(path))
        {
            return RotationWriteRisk.PngCopyOnly;
        }

        await MagickRuntime.OperationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                MagickRuntime.EnsureInitialized();
                var info = new MagickImageInfo(path);
                return IsPotentiallyLossyCompression(info.Compression)
                    ? RotationWriteRisk.Lossy
                    : RotationWriteRisk.Lossless;
            }, token).ConfigureAwait(false);
        }
        finally
        {
            MagickRuntime.OperationGate.Release();
        }
    }

    public async Task<string> RotateAsync(
        string path,
        int clockwiseQuarterTurns,
        RotationSaveMode saveMode,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedTurns = ImageRotation.NormalizeQuarterTurns(clockwiseQuarterTurns);
        if (normalizedTurns == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clockwiseQuarterTurns));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The image to rotate could not be found.", fullPath);
        }

        if (saveMode == RotationSaveMode.ReplaceOriginal &&
            (PngCopyOnlyExtensions.Contains(ImageFileExtensions.NormalizeExtension(fullPath)) || IsAnimatedPng(fullPath)))
        {
            throw new InvalidOperationException("This image format can only be saved as a PNG copy.");
        }

        await MagickRuntime.OperationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => RotateCore(fullPath, normalizedTurns, saveMode, token),
                token).ConfigureAwait(false);
        }
        finally
        {
            MagickRuntime.OperationGate.Release();
        }
    }

    internal static string GetDefaultPngCopyPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The image does not have a containing folder.");
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fullPath)}_rotated.png");
    }

    // Magick reads only the default image of an APNG named .png, so writing it back drops the animation.
    internal static bool IsAnimatedPng(string path)
    {
        if (!ImageFileExtensions.NormalizeExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[8];
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) != 8 ||
                !header.SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            {
                return false;
            }

            // acTL must precede the first IDAT, so only the leading chunks need reading.
            while (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8)
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
                var type = header[4..8];
                if (type.SequenceEqual("acTL"u8)) return true;
                if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return false;
                stream.Seek(length + 4L, SeekOrigin.Current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    internal static bool IsPotentiallyLossyCompression(CompressionMethod compression)
    {
        return compression is
            CompressionMethod.B44 or
            CompressionMethod.B44A or
            CompressionMethod.BC5 or
            CompressionMethod.BC7 or
            CompressionMethod.DWAA or
            CompressionMethod.DWAB or
            CompressionMethod.DXT1 or
            CompressionMethod.DXT3 or
            CompressionMethod.DXT5 or
            CompressionMethod.JPEG or
            CompressionMethod.JPEG2000 or
            CompressionMethod.LERC or
            CompressionMethod.Pxr24 or
            CompressionMethod.WebP;
    }

    private static string RotateCore(
        string sourcePath,
        int clockwiseQuarterTurns,
        RotationSaveMode saveMode,
        CancellationToken token)
    {
        MagickRuntime.EnsureInitialized();
        token.ThrowIfCancellationRequested();

        using var images = new MagickImageCollection();
        if (saveMode == RotationSaveMode.PngCopy)
        {
            // Only the first image is written; layered PSB/PSD and multi-page files can be huge.
            images.Read(sourcePath, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        }
        else
        {
            images.Read(sourcePath);
        }
        if (images.Count == 0)
        {
            throw new InvalidDataException("Magick.NET did not return any image frames.");
        }

        var originalFormat = images[0].Format;
        // Independent document pages must retain their own canvas dimensions.
        if (images.Count > 1 && originalFormat is MagickFormat.Gif or MagickFormat.WebP or MagickFormat.APng)
        {
            images.Coalesce();
        }

        var degrees = ImageRotation.GetClockwiseDegrees(clockwiseQuarterTurns);
        foreach (var frame in images)
        {
            token.ThrowIfCancellationRequested();
            frame.AutoOrient();
            frame.Rotate(degrees);
            frame.ResetPage();
        }

        if (saveMode == RotationSaveMode.ReplaceOriginal &&
            originalFormat == MagickFormat.Gif &&
            images.Count > 1)
        {
            images.Optimize();
        }

        var outputPath = saveMode == RotationSaveMode.PngCopy
            ? GetDefaultPngCopyPath(sourcePath)
            : sourcePath;
        var extension = saveMode == RotationSaveMode.PngCopy
            ? ".png"
            : Path.GetExtension(sourcePath);
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("The image does not have a containing folder.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.mangosteen-{Guid.NewGuid():N}{extension}");

        try
        {
            if (saveMode == RotationSaveMode.PngCopy)
            {
                images[0].Write(temporaryPath, MagickFormat.Png);
            }
            else
            {
                images.Write(temporaryPath, originalFormat);
            }

            token.ThrowIfCancellationRequested();
            ReplaceFile(temporaryPath, outputPath);
            return outputPath;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void ReplaceFile(string temporaryPath, string outputPath)
    {
        if (File.Exists(outputPath))
        {
            try
            {
                // Replace keeps the original's creation time, ACLs and attributes.
                File.Replace(temporaryPath, outputPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Some network and FAT volumes do not support ReplaceFile.
                if (!File.Exists(temporaryPath)) throw;
            }
        }

        File.Move(temporaryPath, outputPath, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
