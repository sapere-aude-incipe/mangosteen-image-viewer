namespace Mangosteen.Core;

internal static class ImageRotation
{
    public static int NormalizeQuarterTurns(int quarterTurns)
    {
        return ((quarterTurns % 4) + 4) % 4;
    }

    public static double GetClockwiseDegrees(int quarterTurns)
    {
        return NormalizeQuarterTurns(quarterTurns) * 90.0;
    }

    public static PixelSize GetRotatedSize(int width, int height, int quarterTurns)
    {
        return NormalizeQuarterTurns(quarterTurns) % 2 == 0
            ? new PixelSize(width, height)
            : new PixelSize(height, width);
    }
}
