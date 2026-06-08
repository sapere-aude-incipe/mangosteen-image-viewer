using Mangosteen.Caching;

namespace Mangosteen.Decoding;

internal static class MagickResourceLimitPolicy
{
    private const long MinimumMemoryLimitBytes = 128L * ImageMemoryEstimator.Megabyte;
    private const long MaximumMemoryRequestBytes = 256L * ImageMemoryEstimator.Megabyte;
    private const long DiskLimitMultiplier = 2L;
    private const long BytesPerPixel = 4L;

    public static MagickResourceLimitProfile? Create(long? maxDecodedBytes)
    {
        if (maxDecodedBytes is not long limit)
        {
            return null;
        }

        var memoryBytes = Math.Max(0, limit);
        memoryBytes = Math.Max(memoryBytes, MinimumMemoryLimitBytes);
        var diskBytes = MultiplySaturating(memoryBytes, DiskLimitMultiplier);
        var maxMemoryRequestBytes = Math.Min(memoryBytes, MaximumMemoryRequestBytes);
        var areaPixels = Math.Max(1, memoryBytes / BytesPerPixel);

        return new MagickResourceLimitProfile(
            ToUInt64(memoryBytes),
            ToUInt64(diskBytes),
            ToUInt64(areaPixels),
            ToUInt64(maxMemoryRequestBytes));
    }

    private static long MultiplySaturating(long value, long multiplier)
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

    private static ulong ToUInt64(long value)
    {
        return value <= 0 ? 0UL : (ulong)value;
    }
}

internal readonly record struct MagickResourceLimitProfile(
    ulong Memory,
    ulong Disk,
    ulong Area,
    ulong MaxMemoryRequest);
