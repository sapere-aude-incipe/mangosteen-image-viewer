namespace Mangosteen.Rendering.Advanced;

internal enum ImageTilePixelFormat : byte
{
    Rgba8 = 1,
    Rgba16 = 2
}

internal sealed record ImageTileData(
    ImageTileKey Key,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    ImageTilePixelFormat PixelFormat,
    byte[] Pixels)
{
    public long EstimatedBytes => Pixels.LongLength;

    public bool HasExpectedPixelLength()
    {
        try
        {
            var bytesPerChannel = PixelFormat switch
            {
                ImageTilePixelFormat.Rgba8 => 1,
                ImageTilePixelFormat.Rgba16 => 2,
                _ => 0
            };
            return bytesPerChannel != 0 &&
                Width > 0 &&
                Height > 0 &&
                Pixels.LongLength == checked((long)Width * Height * 4 * bytesPerChannel);
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
