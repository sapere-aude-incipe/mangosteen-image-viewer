using Mangosteen.Core;
using Mangosteen.Decoding;

namespace Mangosteen.Caching;

public static class ImageMemoryEstimator
{
    public const long Megabyte = 1024L * 1024L;
    public const long Gigabyte = 1024L * Megabyte;

    public static long GetRemainingCacheBudget(long selectedBudgetBytes, long activeImageBytes)
    {
        selectedBudgetBytes = Math.Max(0, selectedBudgetBytes);
        activeImageBytes = Math.Max(0, activeImageBytes);
        if (activeImageBytes >= selectedBudgetBytes)
        {
            return 0;
        }

        return selectedBudgetBytes - activeImageBytes;
    }

    public static long EstimateFullDecodeBytes(ImageMetadata metadata)
    {
        return EstimateBytes(metadata.Width, metadata.Height, metadata.FrameCount);
    }

    public static long EstimatePreviewBytes(ImageMetadata metadata, PixelSize previewSize)
    {
        if (previewSize.IsEmpty)
        {
            return EstimateBytes(metadata.Width, metadata.Height, frames: 1);
        }

        var scale = Math.Min((double)previewSize.Width / metadata.Width, (double)previewSize.Height / metadata.Height);
        if (scale >= 1.0)
        {
            return EstimateBytes(metadata.Width, metadata.Height, frames: 1);
        }

        var width = Math.Max(1, (int)Math.Round(metadata.Width * scale));
        var height = Math.Max(1, (int)Math.Round(metadata.Height * scale));
        return EstimateBytes(width, height, frames: 1);
    }

    private static long EstimateBytes(int width, int height, int frames)
    {
        try
        {
            checked
            {
                return (long)Math.Max(1, width) * Math.Max(1, height) * Math.Max(1, frames) * 4L;
            }
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
}
