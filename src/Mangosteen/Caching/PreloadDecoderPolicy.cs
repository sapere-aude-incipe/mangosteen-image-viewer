using Mangosteen.Decoding;

namespace Mangosteen.Caching;

internal static class PreloadDecoderPolicy
{
    public static bool IsPreloadDecoder(IImageDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        // Magick is the broad fallback and uses process-wide resource limits; keep it free for interactive loads.
        return decoder is not MagickImageDecoder;
    }

    public static bool IsFullPreloadDecoder(IImageDecoder decoder)
    {
        return IsPreloadDecoder(decoder) && decoder is not WicRawPreviewImageDecoder;
    }

    public static bool HasPreloadCandidate(
        DecoderRegistry registry,
        string path,
        bool isClosing,
        CancellationToken token)
    {
        return HasCandidate(registry, path, isClosing, token, IsPreloadDecoder);
    }

    public static bool HasFullPreloadCandidate(
        DecoderRegistry registry,
        string path,
        bool isClosing,
        CancellationToken token)
    {
        return HasCandidate(registry, path, isClosing, token, IsFullPreloadDecoder);
    }

    private static bool HasCandidate(
        DecoderRegistry registry,
        string path,
        bool isClosing,
        CancellationToken token,
        Func<IImageDecoder, bool> decoderFilter)
    {
        ArgumentNullException.ThrowIfNull(registry);

        try
        {
            return registry.HasCandidate(path, decoderFilter);
        }
        catch (Exception ex) when (BackgroundExceptionPolicy.IsExpectedShutdownOrCancellation(ex, isClosing, token))
        {
            return false;
        }
    }
}
