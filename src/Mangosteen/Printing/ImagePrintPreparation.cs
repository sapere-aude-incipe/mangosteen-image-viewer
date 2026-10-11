using System.IO;
using System.Windows.Media.Imaging;
using Mangosteen.Decoding;
using SkiaSharp;

namespace Mangosteen.Printing;

internal sealed record PrintSnapshot(BitmapSource Source, int QuarterTurns, string JobName);

internal static class ImagePrintPreparation
{
    public static async Task<PrintSnapshot> PrepareAsync(DecodedImage displayed, int frameIndex, int turns,
        Func<ImageDecodeRequest, CancellationToken, Task<DecodedImage>> decode, long byteLimit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = displayed.Metadata.Path;
        var jobName = Path.GetFileName(path);
        if (displayed.IsFullResolution) return Capture(displayed, frameIndex, turns, jobName);
        using var full = await decode(new ImageDecodeRequest(path, FullResolution: true, MaxDecodedBytes: byteLimit), token);
        token.ThrowIfCancellationRequested();
        return Capture(full, frameIndex, turns, jobName);
    }

    public static PrintSnapshot Capture(DecodedImage image, int frameIndex, int turns, string jobName)
    {
        if (!image.IsFullResolution)
            throw new InvalidOperationException("Printing requires a full-resolution image, not a preview.");
        var index = Math.Clamp(frameIndex, 0, image.Frames.Count - 1);
        return new PrintSnapshot(CreateBitmapSource(image.Frames[index].Image), turns, jobName);
    }

    internal static BitmapSource CreateBitmapSource(SKImage image)
    {
        using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidDataException("Could not prepare the image for Windows.");
        using var stream = new MemoryStream(encoded.ToArray());
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
