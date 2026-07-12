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
            [ToolbarIconKind.RotateLeft] = "M9.2,5.2 L4.5,8.2 L9.2,11.2 M4.9,8.2 H13.7 C17.3,8.2 19.5,10.5 19.5,13.7 C19.5,16.9 17.1,19.2 13.8,19.2",
            [ToolbarIconKind.RotateRight] = "M14.8,5.2 L19.5,8.2 L14.8,11.2 M19.1,8.2 H10.3 C6.7,8.2 4.5,10.5 4.5,13.7 C4.5,16.9 6.9,19.2 10.2,19.2",
            [ToolbarIconKind.Folder] = "M4.3,7.4 H9.4 L11.2,9.2 H19.7 V17.5 Q19.7,19.1 18.1,19.1 H5.9 Q4.3,19.1 4.3,17.5 Z M4.3,7.4 V6.5 Q4.3,5.4 5.4,5.4 H8.8 L10.6,7.4 H18.5 Q19.7,7.4 19.7,8.6 V9.2",
            [ToolbarIconKind.Delete] = "M5.25,7.4 H18.75 M9.6,7.4 V6 Q9.6,4.9 10.7,4.9 H13.3 Q14.4,4.9 14.4,6 V7.4 M6.9,7.4 L7.7,17.7 Q7.8,19.1 9.25,19.1 H14.75 Q16.2,19.1 16.3,17.7 L17.1,7.4 M10.4,10.5 V15.9 M13.6,10.5 V15.9"
        };

    private static readonly ConcurrentDictionary<ToolbarIconKind, Geometry> GeometryCache = new();

    internal static Viewbox Create(ToolbarIconKind kind, Brush stroke, double size = 20.0)
    {
        ArgumentNullException.ThrowIfNull(stroke);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = new Canvas
            {
                Width = ViewBoxSize,
                Height = ViewBoxSize,
                Children =
                {
                    new Path
                    {
                        Data = GetGeometry(kind),
                        Fill = Brushes.Transparent,
                        Stroke = stroke,
                        StrokeThickness = GetStrokeThickness(kind),
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        SnapsToDevicePixels = true
                    }
                }
            }
        };
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
