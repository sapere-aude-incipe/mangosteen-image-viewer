using ClassicPhotoViewer.Core;

namespace ClassicPhotoViewer.Decoding;

internal static class ImageDecodeSizing
{
    public static PixelSize GetTargetSize(int width, int height, PixelSize? previewSize, bool fullResolution)
    {
        if (fullResolution || previewSize is not { IsEmpty: false } target)
        {
            return new PixelSize(width, height);
        }

        var scale = Math.Min((double)target.Width / Math.Max(1, width), (double)target.Height / Math.Max(1, height));
        if (scale >= 1.0)
        {
            return new PixelSize(width, height);
        }

        return new PixelSize(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static bool IsFullResolution(ImageDecodeRequest request, PixelSize targetSize, ImageMetadata metadata, bool animated)
    {
        return request.FullResolution ||
            (!animated && targetSize.Width == metadata.Width && targetSize.Height == metadata.Height);
    }
}
