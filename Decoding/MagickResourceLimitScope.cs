using ImageMagick;

namespace ClassicPhotoViewer.Decoding;

internal sealed class MagickResourceLimitScope : IDisposable
{
    private readonly MagickResourceLimitProfile? _previous;
    private bool _disposed;

    private MagickResourceLimitScope(MagickResourceLimitProfile? previous)
    {
        _previous = previous;
    }

    public static MagickResourceLimitScope Enter(long? maxDecodedBytes)
    {
        var profile = MagickResourceLimitPolicy.Create(maxDecodedBytes);
        if (profile is null)
        {
            return new MagickResourceLimitScope(null);
        }

        var previous = Capture();
        Apply(profile.Value);
        return new MagickResourceLimitScope(previous);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_previous is MagickResourceLimitProfile previous)
        {
            Apply(previous);
        }

        _disposed = true;
    }

    private static MagickResourceLimitProfile Capture()
    {
        return new MagickResourceLimitProfile(
            ResourceLimits.Memory,
            ResourceLimits.Disk,
            ResourceLimits.Area,
            ResourceLimits.MaxMemoryRequest);
    }

    private static void Apply(MagickResourceLimitProfile profile)
    {
        ResourceLimits.Memory = profile.Memory;
        ResourceLimits.Disk = profile.Disk;
        ResourceLimits.Area = profile.Area;
        ResourceLimits.MaxMemoryRequest = profile.MaxMemoryRequest;
    }
}
