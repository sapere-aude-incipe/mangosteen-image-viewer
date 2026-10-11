namespace Mangosteen.Rendering.Advanced;

internal readonly record struct ImagePyramidLevel(int Index, int Width, int Height, int Downsample)
{
    public double Scale => 1.0 / Downsample;
}

internal readonly record struct ImageTileKey(int Level, int X, int Y);

internal readonly record struct ImageTileRequest(ImageTileKey Key, int TileSize);

internal sealed class ImagePyramid
{
    private readonly IReadOnlyList<ImagePyramidLevel> _levels;

    public ImagePyramid(int width, int height, int tileSize = 512)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (tileSize is < 128 or > 2048) throw new ArgumentOutOfRangeException(nameof(tileSize));

        TileSize = tileSize;
        var levels = new List<ImagePyramidLevel>();
        var level = 0;
        var downsample = 1;
        while (true)
        {
            var levelWidth = DivideRoundUp(width, downsample);
            var levelHeight = DivideRoundUp(height, downsample);
            levels.Add(new ImagePyramidLevel(level, levelWidth, levelHeight, downsample));
            if (levelWidth <= tileSize && levelHeight <= tileSize)
            {
                break;
            }

            level++;
            downsample = checked(downsample * 2);
        }

        _levels = levels;
    }

    public int TileSize { get; }

    public IReadOnlyList<ImagePyramidLevel> Levels => _levels;

    public ImagePyramidLevel ChooseLevel(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            return _levels[^1];
        }

        var targetDownsample = Math.Max(1.0, 1.0 / zoom);
        var index = (int)Math.Round(Math.Log(targetDownsample, 2), MidpointRounding.AwayFromZero);
        return _levels[Math.Clamp(index, 0, _levels.Count - 1)];
    }

    public IEnumerable<ImageTileKey> GetTilesForSourceRect(
        ImagePyramidLevel level,
        double left,
        double top,
        double right,
        double bottom,
        int marginTiles = 1)
    {
        var tileSourceSpan = checked(TileSize * level.Downsample);
        var maxTileX = Math.Max(0, DivideRoundUp(_levels[0].Width, tileSourceSpan) - 1);
        var maxTileY = Math.Max(0, DivideRoundUp(_levels[0].Height, tileSourceSpan) - 1);
        var minX = Math.Clamp((int)Math.Floor(left / tileSourceSpan) - marginTiles, 0, maxTileX);
        var minY = Math.Clamp((int)Math.Floor(top / tileSourceSpan) - marginTiles, 0, maxTileY);
        var maxX = Math.Clamp((int)Math.Floor(Math.Max(left, right - 1) / tileSourceSpan) + marginTiles, 0, maxTileX);
        var maxY = Math.Clamp((int)Math.Floor(Math.Max(top, bottom - 1) / tileSourceSpan) + marginTiles, 0, maxTileY);

        var centerX = (left + right) / (2.0 * tileSourceSpan);
        var centerY = (top + bottom) / (2.0 * tileSourceSpan);
        return from y in Enumerable.Range(minY, maxY - minY + 1)
               from x in Enumerable.Range(minX, maxX - minX + 1)
               orderby SquaredDistance(x + 0.5, y + 0.5, centerX, centerY)
               select new ImageTileKey(level.Index, x, y);
    }

    private static double SquaredDistance(double x, double y, double centerX, double centerY)
    {
        var dx = x - centerX;
        var dy = y - centerY;
        return dx * dx + dy * dy;
    }

    private static int DivideRoundUp(int value, int divisor)
    {
        return checked((value + divisor - 1) / divisor);
    }
}
