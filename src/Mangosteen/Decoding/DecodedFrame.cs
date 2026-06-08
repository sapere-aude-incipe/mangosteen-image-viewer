using SkiaSharp;

namespace Mangosteen.Decoding;

public sealed class DecodedFrame : IDisposable
{
    private bool _disposed;

    public DecodedFrame(SKImage image, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(image);

        Image = image;
        Delay = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(100) : delay;
    }

    public SKImage Image { get; }

    public TimeSpan Delay { get; }

    public void Dispose()
    {
        if (_disposed) return;

        Image.Dispose();
        _disposed = true;
    }
}
