using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Windows.Shapes;

namespace Mangosteen.Icons;

internal enum ToolbarIconKind
{
    Previous,
    Next,
    ActualPixels,
    FitToWindow,
    Zoom,
    RotateLeft,
    RotateRight,
    Folder,
    Delete
}

internal static class ToolbarIcon
{
    private const double ViewBoxSize = 24.0;
    private const double FluentViewBoxSize = 20.0;
    private const double StrokeThickness = 2.1;
    private const double FolderStrokeThickness = 1.7;

    private static readonly IReadOnlyDictionary<ToolbarIconKind, string> PathData =
        new Dictionary<ToolbarIconKind, string>
        {
            [ToolbarIconKind.Previous] = "M14.4,5.8 L8.1,12 L14.4,18.2",
            [ToolbarIconKind.Next] = "M9.6,5.8 L15.9,12 L9.6,18.2",
            [ToolbarIconKind.ActualPixels] = "M8.5,4.75 H4.75 V8.5 M15.5,4.75 H19.25 V8.5 M19.25,15.5 V19.25 H15.5 M8.5,19.25 H4.75 V15.5 M9.6,9.6 H14.4 V14.4 H9.6 Z",
            [ToolbarIconKind.FitToWindow] = "M7.2,4.2 V7.2 H4.2 M16.8,4.2 V7.2 H19.8 M19.8,16.8 H16.8 V19.8 M7.2,19.8 V16.8 H4.2 M9.6,9.6 H14.4 V14.4 H9.6 Z",
            [ToolbarIconKind.Zoom] = "M10.5,5.2 A5.3,5.3 0 1 0 10.5,15.8 A5.3,5.3 0 1 0 10.5,5.2 M14.35,14.35 L18.7,18.7",
            [ToolbarIconKind.RotateLeft] = "M16,10 C16,6.68629 13.3137,4 10,4 C8.2234,4 6.62683,4.77191 5.52772,6 H7.5 C7.77614,6 8,6.22386 8,6.5 C8,6.77614 7.77614,7 7.5,7 H4.5 C4.22386,7 4,6.77614 4,6.5 V3.5 C4,3.22386 4.22386,3 4.5,3 C4.77614,3 5,3.22386 5,3.5 V5.10109 C6.27012,3.80499 8.04094,3 10,3 C13.866,3 17,6.13401 17,10 C17,13.866 13.866,17 10,17 C6.13401,17 3,13.866 3,10 C3,9.8191 3.00687,9.6397 3.02038,9.46207 C3.04133,9.18673 3.28152,8.98049 3.55687,9.00144 C3.83222,9.02239 4.03845,9.26258 4.0175,9.53793 C4.00591,9.69034 4,9.84443 4,10 C4,13.3137 6.68629,16 10,16 C13.3137,16 16,13.3137 16,10 Z",
            [ToolbarIconKind.RotateRight] = "M4,10 C4,6.68629 6.68629,4 10,4 C11.7766,4 13.3732,4.77191 14.4723,6 H12.5 C12.2239,6 12,6.22386 12,6.5 C12,6.77614 12.2239,7 12.5,7 H15.5 C15.7761,7 16,6.77614 16,6.5 V3.5 C16,3.22386 15.7761,3 15.5,3 C15.2239,3 15,3.22386 15,3.5 V5.10109 C13.7299,3.80499 11.9591,3 10,3 C6.13401,3 3,6.13401 3,10 C3,13.866 6.13401,17 10,17 C13.866,17 17,13.866 17,10 C17,9.8191 16.9931,9.6397 16.9796,9.46207 C16.9587,9.18673 16.7185,8.98049 16.4431,9.00144 C16.1678,9.02239 15.9615,9.26258 15.9825,9.53793 C15.9941,9.69034 16,9.84443 16,10 C16,13.3137 13.3137,16 10,16 C6.68629,16 4,13.3137 4,10 Z",
            [ToolbarIconKind.Folder] = "M4.3,7.4 H9.4 L11.2,9.2 H19.7 V17.5 Q19.7,19.1 18.1,19.1 H5.9 Q4.3,19.1 4.3,17.5 Z M4.3,7.4 V6.5 Q4.3,5.4 5.4,5.4 H8.8 L10.6,7.4 H18.5 Q19.7,7.4 19.7,8.6 V9.2",
            [ToolbarIconKind.Delete] = "M5.25,7.4 H18.75 M9.6,7.4 V6 Q9.6,4.9 10.7,4.9 H13.3 Q14.4,4.9 14.4,6 V7.4 M6.9,7.4 L7.7,17.7 Q7.8,19.1 9.25,19.1 H14.75 Q16.2,19.1 16.3,17.7 L17.1,7.4 M10.4,10.5 V15.9 M13.6,10.5 V15.9"
        };

    private static readonly ConcurrentDictionary<ToolbarIconKind, Geometry> GeometryCache = new();

    internal static Viewbox Create(ToolbarIconKind kind, Brush stroke, double size = 20.0)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        var usesFill = UsesFill(kind);
        var viewBoxSize = GetViewBoxSize(kind);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = new Canvas
            {
                Width = viewBoxSize,
                Height = viewBoxSize,
                Children =
                {
                    new Path
                    {
                        Data = GetGeometry(kind),
                        Fill = usesFill ? stroke : Brushes.Transparent,
                        Stroke = usesFill ? null : stroke,
                        StrokeThickness = usesFill ? 0 : GetStrokeThickness(kind),
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        SnapsToDevicePixels = true
                    }
                }
            }
        };
    }

    private static bool UsesFill(ToolbarIconKind kind)
    {
        return kind is ToolbarIconKind.RotateLeft or ToolbarIconKind.RotateRight;
    }

    private static double GetViewBoxSize(ToolbarIconKind kind)
    {
        return UsesFill(kind) ? FluentViewBoxSize : ViewBoxSize;
    }

    private static double GetStrokeThickness(ToolbarIconKind kind)
    {
        return kind == ToolbarIconKind.Folder
            ? FolderStrokeThickness
            : StrokeThickness;
    }

    internal static Geometry GetGeometry(ToolbarIconKind kind)
    {
        return GeometryCache.GetOrAdd(kind, static iconKind =>
        {
            var geometry = Geometry.Parse(PathData[iconKind]);
            if (geometry.CanFreeze)
            {
                geometry.Freeze();
            }

            return geometry;
        });
    }

    internal static IEnumerable<ToolbarIconKind> AllKinds => PathData.Keys;
}
