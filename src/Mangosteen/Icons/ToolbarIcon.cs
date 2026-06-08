using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mangosteen.Icons;

internal enum ToolbarIconKind
{
    Previous,
    Next,
    ActualPixels,
    FitToWindow
}

internal static class ToolbarIcon
{
    private const double ViewBoxSize = 24.0;
    private const double StrokeThickness = 1.85;

    private static readonly IReadOnlyDictionary<ToolbarIconKind, string> PathData =
        new Dictionary<ToolbarIconKind, string>
        {
            [ToolbarIconKind.Previous] = "M14.75,6 L8.75,12 L14.75,18",
            [ToolbarIconKind.Next] = "M9.25,6 L15.25,12 L9.25,18",
            [ToolbarIconKind.ActualPixels] = "M8.25,8.75 H15.75 V16.25 H8.25 Z M8.5,8.5 L5.5,5.5 M5.5,8 V5.5 H8 M15.5,8.5 L18.5,5.5 M18.5,8 V5.5 H16 M8.5,16.5 L5.5,19.5 M5.5,16 V19.5 H8 M15.5,16.5 L18.5,19.5 M18.5,16 V19.5 H16",
            [ToolbarIconKind.FitToWindow] = "M7.25,7.5 H16.75 V16.5 H7.25 Z M12,4.5 V7.5 M10.5,6 L12,7.5 L13.5,6 M12,19.5 V16.5 M10.5,18 L12,16.5 L13.5,18 M4.5,12 H7.25 M5.95,10.6 L7.45,12 L5.95,13.4 M19.5,12 H16.75 M18.05,10.6 L16.55,12 L18.05,13.4"
        };

    internal static Viewbox Create(ToolbarIconKind kind, Brush stroke, double size = 19.0)
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
                        StrokeThickness = StrokeThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        SnapsToDevicePixels = true
                    }
                }
            }
        };
    }

    internal static Geometry GetGeometry(ToolbarIconKind kind)
    {
        return Geometry.Parse(PathData[kind]);
    }

    internal static IEnumerable<ToolbarIconKind> AllKinds => PathData.Keys;
}
