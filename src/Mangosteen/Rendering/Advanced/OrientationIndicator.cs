using System.Windows.Media.Media3D;
using SkiaSharp;

namespace Mangosteen.Rendering.Advanced;

internal readonly record struct ModelViewOrientation(Vector3D Right, Vector3D Up, Vector3D TowardViewer)
{
    public static ModelViewOrientation FromCamera(Vector3D position, Vector3D focalPoint, Vector3D viewUp)
    {
        var toward = position - focalPoint;
        if (!Normalize(ref toward)) return Default;
        var right = Vector3D.CrossProduct(viewUp, toward);
        if (!Normalize(ref right)) return Default;
        var up = Vector3D.CrossProduct(toward, right);
        if (!Normalize(ref up)) return Default;
        return new(right, up, toward);
    }

    internal static ModelViewOrientation Default => new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));

    private static bool Normalize(ref Vector3D vector)
    {
        if (!double.IsFinite(vector.Length) || vector.Length < 1e-12) return false;
        vector.Normalize();
        return true;
    }
}

internal static class OrientationIndicator
{
    public const int LogicalSize = 112;

    internal static SKColor AxisColor(int axis, bool darkMode) => (axis, darkMode) switch
    {
        (0, true) => new(239, 133, 133),
        (1, true) => new(134, 207, 162),
        (2, true) => new(133, 183, 242),
        (0, false) => new(184, 64, 70),
        (1, false) => new(45, 126, 80),
        _ => new(48, 111, 186)
    };

    public static SKBitmap Rasterize(ModelViewOrientation orientation, bool darkMode, int pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 1);
        var bitmap = new SKBitmap(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(pixels / (float)LogicalSize);
        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 11) { Edging = SKFontEdging.Antialias };
        const float center = LogicalSize / 2f;
        const float radius = 33;
        var ends = new List<(int Axis, bool Positive, float X, float Y, double Depth)>(6);
        var axes = new[] { new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1) };
        for (var axis = 0; axis < axes.Length; axis++)
        {
            var x = (float)Vector3D.DotProduct(axes[axis], orientation.Right) * radius;
            var y = -(float)Vector3D.DotProduct(axes[axis], orientation.Up) * radius;
            var depth = Vector3D.DotProduct(axes[axis], orientation.TowardViewer);
            ends.Add((axis, false, center - x, center - y, -depth));
            ends.Add((axis, true, center + x, center + y, depth));
        }

        // Draw the far endpoints first, so the labels follow the model's depth ordering.
        foreach (var end in ends.OrderBy(end => end.Depth).ThenBy(end => end.Positive))
        {
            var color = AxisColor(end.Axis, darkMode);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = end.Positive ? 2 : 1.25f;
            paint.Color = end.Positive ? color : color.WithAlpha(100);
            canvas.DrawLine(center, center, end.X, end.Y, paint);
            if (!end.Positive)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = darkMode ? new SKColor(33, 33, 33) : new SKColor(244, 246, 248);
                canvas.DrawCircle(end.X, end.Y, 4, paint);
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = color.WithAlpha(140);
                canvas.DrawCircle(end.X, end.Y, 4, paint);
                continue;
            }

            paint.Style = SKPaintStyle.Fill;
            paint.Color = color;
            canvas.DrawCircle(end.X, end.Y, 10.5f, paint);
            paint.Color = darkMode ? new SKColor(27, 31, 36) : SKColors.White;
            var label = "XYZ"[end.Axis].ToString();
            font.MeasureText(label, out var bounds);
            canvas.DrawText(label, end.X - bounds.MidX, end.Y - bounds.MidY, font, paint);
        }
        return bitmap;
    }
}
