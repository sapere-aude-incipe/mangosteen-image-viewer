namespace Mangosteen.Core;

public readonly record struct PixelSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelSize Empty { get; } = new(0, 0);

    public static PixelSize FromDips(double width, double height, double dpiScaleX, double dpiScaleY)
    {
        return new(
            ToPixelLength(width, dpiScaleX),
            ToPixelLength(height, dpiScaleY));
    }

    public static PixelSize FromDipsWithFallback(
        double width,
        double height,
        double fallbackWidth,
        double fallbackHeight,
        double dpiScaleX,
        double dpiScaleY)
    {
        var resolvedWidth = IsUsableDimension(width) ? width : fallbackWidth;
        var resolvedHeight = IsUsableDimension(height) ? height : fallbackHeight;
        return FromDips(resolvedWidth, resolvedHeight, dpiScaleX, dpiScaleY);
    }

    private static bool IsUsableDimension(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 1.0;
    }

    private static int ToPixelLength(double dips, double dpiScale)
    {
        var safeDips = IsUsableDimension(dips) ? dips : 1.0;
        var safeScale = IsUsableScale(dpiScale) ? dpiScale : 1.0;
        var pixels = safeDips * safeScale;
        if (double.IsPositiveInfinity(pixels) || pixels >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (double.IsNaN(pixels) || double.IsNegativeInfinity(pixels) || pixels <= 1.0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(pixels));
    }

    private static bool IsUsableScale(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
    }
}
