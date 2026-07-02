using Mangosteen.Core;
using SkiaSharp;

namespace Mangosteen.Rendering;

public sealed class ViewerState
{
    public const double MaximumZoom = 64.0;

    public PixelSize ViewportSize { get; private set; }

    public PixelSize ImageSize { get; private set; }

    public double Zoom { get; private set; } = 1.0;

    public SKPoint Offset { get; private set; }

    public ViewerFitMode Mode { get; private set; } = ViewerFitMode.Fit;

    public bool HasImage => !ImageSize.IsEmpty;

    public bool FitsAtActualPixels =>
        HasImage
        && ImageSize.Width <= ViewportSize.Width
        && ImageSize.Height <= ViewportSize.Height;

    public double FitZoom => GetFitZoom();

    public void SetViewport(PixelSize viewport)
    {
        ViewportSize = viewport;
        if (!HasImage) return;

        if (Mode == ViewerFitMode.Fit)
        {
            FitToWindow();
        }
        else
        {
            ClampOffset();
        }
    }

    public void SetImage(int width, int height, bool fitToWindow)
    {
        var previousImageSize = ImageSize;
        var previousZoom = Zoom;
        var previousOffset = Offset;
        var previousMode = Mode;
        var previousFitZoom = GetFitZoom();
        var hadImage = HasImage;

        ImageSize = new PixelSize(width, height);
        if (fitToWindow || previousMode == ViewerFitMode.Fit)
        {
            FitToWindow();
        }
        else if (hadImage)
        {
            PreserveViewForNewImage(previousImageSize, previousZoom, previousOffset, previousMode, previousFitZoom);
        }
        else
        {
            ClampOffset();
        }
    }

    public void ClearImage()
    {
        ImageSize = PixelSize.Empty;
        Zoom = 1.0;
        Offset = default;
        Mode = ViewerFitMode.Fit;
    }

    public void FitToWindow()
    {
        if (!HasImage || ViewportSize.IsEmpty) return;

        Zoom = GetFitZoom();
        Mode = ViewerFitMode.Fit;
        CenterImage();
    }

    public void SetActualPixels()
    {
        if (!HasImage) return;

        Zoom = Math.Max(1.0, GetFitZoom());
        Mode = ViewerFitMode.ActualPixels;
        CenterImage();
        ClampOffset();
    }

    public ViewerSnapshot Capture()
    {
        return new ViewerSnapshot(Zoom, Offset, Mode);
    }

    public void Restore(ViewerSnapshot snapshot)
    {
        if (!HasImage) return;

        Zoom = snapshot.Zoom;
        Offset = snapshot.Offset;
        Mode = snapshot.Mode;
        ClampOffset();
    }

    public void ZoomAt(double factor, SKPoint viewportPoint)
    {
        if (!HasImage || factor <= 0) return;

        var oldZoom = Zoom;
        var fitZoom = GetFitZoom();
        var newZoom = Math.Clamp(oldZoom * factor, fitZoom, MaximumZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        if (Math.Abs(newZoom - fitZoom) < 0.0001)
        {
            FitToWindow();
            return;
        }

        var imagePointX = (viewportPoint.X - Offset.X) / oldZoom;
        var imagePointY = (viewportPoint.Y - Offset.Y) / oldZoom;

        Zoom = newZoom;
        Offset = new SKPoint(
            (float)(viewportPoint.X - imagePointX * newZoom),
            (float)(viewportPoint.Y - imagePointY * newZoom));
        Mode = ViewerFitMode.Custom;
        ClampOffset();
    }

    public void PanBy(SKPoint delta)
    {
        if (!HasImage) return;
        if (!CanPan())
        {
            CenterImage();
            return;
        }

        Offset = new SKPoint(Offset.X + delta.X, Offset.Y + delta.Y);
        if (Mode is not ViewerFitMode.ActualPixels)
        {
            Mode = ViewerFitMode.Custom;
        }
        ClampOffset();
    }

    public SKRect GetDestinationRect()
    {
        var width = (float)(ImageSize.Width * Zoom);
        var height = (float)(ImageSize.Height * Zoom);
        return new SKRect(Offset.X, Offset.Y, Offset.X + width, Offset.Y + height);
    }

    private void CenterImage()
    {
        var scaledWidth = ImageSize.Width * Zoom;
        var scaledHeight = ImageSize.Height * Zoom;
        Offset = new SKPoint(
            (float)((ViewportSize.Width - scaledWidth) / 2.0),
            (float)((ViewportSize.Height - scaledHeight) / 2.0));
    }

    private void PreserveViewForNewImage(
        PixelSize previousImageSize,
        double previousZoom,
        SKPoint previousOffset,
        ViewerFitMode previousMode,
        double previousFitZoom)
    {
        if (!HasImage ||
            ViewportSize.IsEmpty ||
            previousImageSize.IsEmpty ||
            previousZoom <= 0 ||
            previousFitZoom <= 0)
        {
            ClampOffset();
            return;
        }

        var viewportCenter = new SKPoint(ViewportSize.Width / 2f, ViewportSize.Height / 2f);
        var previousImageCenter = new SKPoint(
            (float)((viewportCenter.X - previousOffset.X) / previousZoom),
            (float)((viewportCenter.Y - previousOffset.Y) / previousZoom));
        var relativeCenterX = Math.Clamp(previousImageCenter.X / previousImageSize.Width, 0.0, 1.0);
        var relativeCenterY = Math.Clamp(previousImageCenter.Y / previousImageSize.Height, 0.0, 1.0);

        var fitZoom = GetFitZoom();
        if (previousMode == ViewerFitMode.ActualPixels)
        {
            Zoom = Math.Max(1.0, fitZoom);
            Mode = ViewerFitMode.ActualPixels;
        }
        else
        {
            var zoomRatio = previousZoom / previousFitZoom;
            Zoom = Math.Clamp(fitZoom * zoomRatio, fitZoom, MaximumZoom);
            Mode = ViewerFitMode.Custom;
        }

        Offset = new SKPoint(
            (float)(viewportCenter.X - relativeCenterX * ImageSize.Width * Zoom),
            (float)(viewportCenter.Y - relativeCenterY * ImageSize.Height * Zoom));
        ClampOffset();
    }

    private void ClampOffset()
    {
        var scaledWidth = ImageSize.Width * Zoom;
        var scaledHeight = ImageSize.Height * Zoom;
        Offset = new SKPoint(
            ClampAxis(Offset.X, scaledWidth, ViewportSize.Width),
            ClampAxis(Offset.Y, scaledHeight, ViewportSize.Height));
    }

    private bool CanPan()
    {
        return ImageSize.Width * Zoom > ViewportSize.Width ||
            ImageSize.Height * Zoom > ViewportSize.Height;
    }

    private static float ClampAxis(float offset, double scaledLength, int viewportLength)
    {
        if (scaledLength <= viewportLength)
        {
            return (float)((viewportLength - scaledLength) / 2.0);
        }

        var min = viewportLength - scaledLength;
        return (float)Math.Clamp(offset, min, 0);
    }

    private double GetFitZoom()
    {
        if (!HasImage || ViewportSize.IsEmpty) return 1.0;

        var scaleX = (double)ViewportSize.Width / ImageSize.Width;
        var scaleY = (double)ViewportSize.Height / ImageSize.Height;
        return Math.Min(1.0, Math.Min(scaleX, scaleY));
    }
}
