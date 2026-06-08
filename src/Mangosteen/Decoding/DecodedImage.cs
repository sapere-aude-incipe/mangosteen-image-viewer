namespace Mangosteen.Decoding;

public sealed class DecodedImage : IDisposable
{
    private readonly long _estimatedBytes;
    private bool _disposed;

    public DecodedImage(ImageMetadata metadata, IReadOnlyList<DecodedFrame> frames, bool isFullResolution)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(frames);

        var frameSnapshot = frames.ToArray();
        if (frameSnapshot.Length == 0)
        {
            throw new ArgumentException("Decoded images must contain at least one frame.", nameof(frames));
        }

        if (frameSnapshot.Any(static frame => frame is null))
        {
            throw new ArgumentException("Decoded image frames cannot contain null entries.", nameof(frames));
        }

        Frames = frameSnapshot;
        Metadata = metadata.FrameCount == frameSnapshot.Length
            ? metadata
            : metadata with { FrameCount = frameSnapshot.Length };
        IsFullResolution = isFullResolution;
        _estimatedBytes = EstimateFrameBytes(frameSnapshot);
    }

    public ImageMetadata Metadata { get; }

    public int Width => Metadata.Width;

    public int Height => Metadata.Height;

    public int FrameCount => Frames.Count;

    public IReadOnlyList<DecodedFrame> Frames { get; }

    public bool IsFullResolution { get; }

    public long EstimatedBytes => _estimatedBytes;

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var frame in Frames)
        {
            frame.Dispose();
        }

        _disposed = true;
    }

    internal static long EstimateFrameBytes(int width, int height)
    {
        try
        {
            checked
            {
                return (long)Math.Max(1, width) * Math.Max(1, height) * 4L;
            }
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    internal static long AddSaturating(long left, long right)
    {
        if (left < 0 || right < 0)
        {
            return long.MaxValue;
        }

        return long.MaxValue - left < right ? long.MaxValue : left + right;
    }

    private static long EstimateFrameBytes(IReadOnlyList<DecodedFrame> frames)
    {
        var total = 0L;
        foreach (var frame in frames)
        {
            total = AddSaturating(total, EstimateFrameBytes(frame.Image.Width, frame.Image.Height));
        }

        return total;
    }
}
