namespace ClassicPhotoViewer.Decoding;

internal static class DecodedFrameOwnership
{
    public static DecodedImage CreateImageOrDisposeFrames(
        ImageMetadata metadata,
        IReadOnlyList<DecodedFrame>? frames,
        bool isFullResolution)
    {
        try
        {
            return new DecodedImage(metadata, frames!, isFullResolution);
        }
        catch
        {
            DisposeAll(frames);
            throw;
        }
    }

    public static void DisposeAll(IEnumerable<DecodedFrame>? frames)
    {
        if (frames is null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            frame?.Dispose();
        }
    }
}
