using ClassicPhotoViewer.Core;
using System.IO;

namespace ClassicPhotoViewer.Decoding;

internal static class ImageDecodeGuards
{
    internal const long MaxSingleFrameBgraBufferBytes = int.MaxValue;

    public static void ThrowIfEstimatedDecodedBytesExceedLimit(
        ImageMetadata metadata,
        PixelSize targetSize,
        bool decodesAllFrames,
        long? maxDecodedBytes)
    {
        if (maxDecodedBytes is not long limit)
        {
            return;
        }

        limit = Math.Max(0, limit);
        var frameCount = decodesAllFrames ? metadata.FrameCount : 1;
        var estimatedBytes = EstimateBytes(targetSize.Width, targetSize.Height, frameCount);
        if (estimatedBytes <= limit)
        {
            return;
        }

        throw new InvalidDataException(
            $"Estimated decoded image size ({FormatBytes(estimatedBytes)}) exceeds the decode limit ({FormatBytes(limit)}).");
    }

    public static void ThrowIfSingleFrameDecodedBytesExceedLimit(
        int width,
        int height,
        long? maxDecodedBytes)
    {
        if (maxDecodedBytes is not long limit)
        {
            return;
        }

        limit = Math.Max(0, limit);
        var estimatedBytes = EstimateBytes(width, height, frames: 1);
        if (estimatedBytes <= limit)
        {
            return;
        }

        throw new InvalidDataException(
            $"Estimated decoded image size ({FormatBytes(estimatedBytes)}) exceeds the decode limit ({FormatBytes(limit)}).");
    }

    internal static long EstimateBytes(int width, int height, int frames)
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

    internal static int GetBgraStride(int width)
    {
        var stride = EstimateBytes(width, height: 1, frames: 1);
        if (stride > int.MaxValue)
        {
            throw new InvalidDataException($"Decoded image row size ({FormatBytes(stride)}) is too large for this decoder.");
        }

        return (int)stride;
    }

    internal static int GetBgraBufferLength(int width, int height)
    {
        var bytes = EstimateBytes(width, height, frames: 1);
        ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(width, height);

        return (int)bytes;
    }

    internal static void ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(int width, int height)
    {
        var bytes = EstimateBytes(width, height, frames: 1);
        if (bytes > MaxSingleFrameBgraBufferBytes)
        {
            throw new InvalidDataException(
                $"Decoded image pixel buffer ({FormatBytes(bytes)}) exceeds this decoder's single-frame buffer limit ({FormatBytes(MaxSingleFrameBgraBufferBytes)}).");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == long.MaxValue)
        {
            return "more than 8192 PB";
        }

        const double kibibyte = 1024.0;
        var value = Math.Max(0, bytes);
        if (value >= kibibyte * kibibyte * kibibyte)
        {
            return $"{value / (kibibyte * kibibyte * kibibyte):0.##} GB";
        }

        if (value >= kibibyte * kibibyte)
        {
            return $"{value / (kibibyte * kibibyte):0.##} MB";
        }

        if (value >= kibibyte)
        {
            return $"{value / kibibyte:0.##} KB";
        }

        return $"{value} bytes";
    }
}
