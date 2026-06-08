using ClassicPhotoViewer.Core;
using SkiaSharp;
using System.IO;

namespace ClassicPhotoViewer.Decoding;

internal static class ImageOrientation
{
    public static PixelSize GetOrientedSize(int width, int height, SKEncodedOrigin origin)
    {
        return SwapsAxes(origin)
            ? new PixelSize(height, width)
            : new PixelSize(width, height);
    }

    public static PixelSize GetDecodeTarget(PixelSize orientedTarget, SKEncodedOrigin origin)
    {
        return SwapsAxes(origin)
            ? new PixelSize(orientedTarget.Height, orientedTarget.Width)
            : orientedTarget;
    }

    public static SKImage ApplyAndDisposeSource(SKImage source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft)
        {
            return source;
        }

        var orientedSize = GetOrientedSize(source.Width, source.Height, origin);
        var info = new SKImageInfo(orientedSize.Width, orientedSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var surface = CreateSurfaceOrThrow(info);
            var canvas = surface.Canvas;

            ApplyCanvasTransform(canvas, source.Width, source.Height, origin);
            canvas.DrawImage(source, 0, 0);
            canvas.Flush();

            return surface.Snapshot();
        }
        finally
        {
            source.Dispose();
        }
    }

    internal static SKSurface CreateSurfaceOrThrow(SKImageInfo info)
    {
        ImageDecodeGuards.ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(info.Width, info.Height);

        return SKSurface.Create(info)
            ?? throw new InvalidDataException("SkiaSharp could not allocate an oriented image surface.");
    }

    public static SKEncodedOrigin FromExifOrientation(int orientation)
    {
        return orientation switch
        {
            2 => SKEncodedOrigin.TopRight,
            3 => SKEncodedOrigin.BottomRight,
            4 => SKEncodedOrigin.BottomLeft,
            5 => SKEncodedOrigin.LeftTop,
            6 => SKEncodedOrigin.RightTop,
            7 => SKEncodedOrigin.RightBottom,
            8 => SKEncodedOrigin.LeftBottom,
            _ => SKEncodedOrigin.TopLeft
        };
    }

    private static bool SwapsAxes(SKEncodedOrigin origin)
    {
        return origin is
            SKEncodedOrigin.LeftTop or
            SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or
            SKEncodedOrigin.LeftBottom;
    }

    private static void ApplyCanvasTransform(SKCanvas canvas, int width, int height, SKEncodedOrigin origin)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(height, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(height, width);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, width);
                canvas.RotateDegrees(270);
                break;
        }
    }
}
