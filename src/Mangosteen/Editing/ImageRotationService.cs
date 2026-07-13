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
            PngCopyOnlyExtensions.Contains(ImageFileExtensions.NormalizeExtension(fullPath)))
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
        images.Read(sourcePath);
        if (images.Count == 0)
        {
            throw new InvalidDataException("Magick.NET did not return any image frames.");
        }

        var originalFormat = images[0].Format;
        if (images.Count > 1)
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
            File.Move(temporaryPath, outputPath, overwrite: true);
            return outputPath;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
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
