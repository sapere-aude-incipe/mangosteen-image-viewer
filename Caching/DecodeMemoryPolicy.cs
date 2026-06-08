namespace ClassicPhotoViewer.Caching;

public static class DecodeMemoryPolicy
{
    private const int PreloadDecodeBudgetSafetyFactor = 3;
    private const int PreviewDecodeScratchFactor = 4;
    private const long PreviewDecodeScratchFloorBytes = 64L * ImageMemoryEstimator.Megabyte;

    public static long GetInteractiveDecodeLimit(long memoryBudgetBytes)
    {
        return Math.Max(0, memoryBudgetBytes);
    }

    public static long GetPreloadDecodeLimit(long memoryBudgetBytes)
    {
        var budget = Math.Max(0, memoryBudgetBytes);
        return Math.Max(128L * ImageMemoryEstimator.Megabyte, budget / PreloadDecodeBudgetSafetyFactor);
    }

    public static long GetFullPreloadDecodeLimit(long memoryBudgetBytes)
    {
        return Math.Max(0, memoryBudgetBytes);
    }

    public static long GetPreloadPreviewDecodeLimit(long storedPreviewBytes, long preloadDecodeLimitBytes)
    {
        var preloadLimit = Math.Max(0, preloadDecodeLimitBytes);
        var previewBytes = Math.Max(0, storedPreviewBytes);
        var scratchLimit = Math.Max(PreviewDecodeScratchFloorBytes, MultiplySaturating(previewBytes, PreviewDecodeScratchFactor));
        return Math.Min(preloadLimit, scratchLimit);
    }

    public static int GetLargeFullPreloadLimit(long memoryBudgetBytes)
    {
        if (memoryBudgetBytes >= 15L * ImageMemoryEstimator.Gigabyte) return 3;
        if (memoryBudgetBytes >= 10L * ImageMemoryEstimator.Gigabyte) return 2;
        if (memoryBudgetBytes >= 5L * ImageMemoryEstimator.Gigabyte) return 1;
        return 0;
    }

    private static long MultiplySaturating(long value, int multiplier)
    {
        try
        {
            checked
            {
                return value * multiplier;
            }
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
}
