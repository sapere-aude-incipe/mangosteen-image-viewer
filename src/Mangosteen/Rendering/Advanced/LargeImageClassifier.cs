using Mangosteen.Decoding;
using System.IO;

namespace Mangosteen.Rendering.Advanced;

internal readonly record struct LargeImageClassification(
    bool UseAdvancedRenderer,
    long EstimatedDecodedBytes,
    string Reason);

internal static class LargeImageClassifier
{
    internal const long DefaultDecodedByteThreshold = 256L * 1024 * 1024;
    private static readonly HashSet<string> AlwaysAdvancedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".psb" };

    public static LargeImageClassification Classify(
        ImageMetadata metadata,
        string path,
        long decodedByteThreshold = DefaultDecodedByteThreshold)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (decodedByteThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decodedByteThreshold));
        }

        long estimatedBytes;
        try
        {
            estimatedBytes = checked((long)metadata.Width * metadata.Height * 4L);
        }
        catch (OverflowException)
        {
            estimatedBytes = long.MaxValue;
        }

        var extension = Path.GetExtension(path);
        if (AlwaysAdvancedExtensions.Contains(extension))
        {
            return new LargeImageClassification(true, estimatedBytes, "large-document format");
        }

        if (estimatedBytes >= decodedByteThreshold)
        {
            return new LargeImageClassification(true, estimatedBytes, "decoded image exceeds threshold");
        }

        return new LargeImageClassification(false, estimatedBytes, "ordinary image");
    }
}
