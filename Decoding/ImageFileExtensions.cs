using System.IO;

namespace ClassicPhotoViewer.Decoding;

public static class ImageFileExtensions
{
    public static readonly IReadOnlyCollection<string> RawImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".3fr", ".ari", ".arw", ".bay", ".cap", ".cr2", ".cr3", ".crw",
            ".dcr", ".dng", ".erf", ".fff", ".gpr", ".k25", ".kdc", ".mdc",
            ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf", ".pef", ".ptx",
            ".pxn", ".raf", ".raw", ".rw2", ".rwl", ".sr2", ".srf", ".srw",
            ".x3f"
        };

    public static readonly IReadOnlyCollection<string> BroadImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".3fr", ".ari", ".arw", ".avif", ".bay", ".bmp", ".cap", ".cr2", ".cr3",
            ".crw", ".cur", ".dcr", ".dcs", ".dds", ".dib", ".dng", ".drf", ".erf",
            ".exif", ".exr", ".fff", ".gif", ".gpr", ".hdr", ".heic", ".heif",
            ".ico", ".iiq", ".jfif", ".jpe", ".jpeg", ".jpg", ".jxl", ".k25",
            ".kdc", ".mdc", ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf",
            ".pbm", ".pcx", ".pef", ".pgm", ".png", ".ppm", ".psb", ".psd",
            ".ptx", ".pxn", ".qoi", ".raf", ".raw", ".rw2", ".rwl", ".sr2",
            ".srf", ".srw", ".svg", ".svgz", ".tga", ".tif", ".tiff", ".webp",
            ".wmf", ".x3f", ".xbm", ".xpm"
        };

    public static string NormalizeExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext) ? string.Empty : ext.ToLowerInvariant();
    }

    public static IReadOnlyCollection<string> BuildFolderScanExtensions(
        IEnumerable<string> supportedExtensions,
        string explicitPath)
    {
        ArgumentNullException.ThrowIfNull(supportedExtensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(explicitPath);

        return supportedExtensions
            .Select(NormalizeExtensionToken)
            .Where(static extension => !string.IsNullOrEmpty(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string NormalizeExtensionToken(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? trimmed.ToLowerInvariant()
            : "." + trimmed.ToLowerInvariant();
    }
}
