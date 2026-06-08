using SkiaSharp;

namespace Mangosteen.Rendering;

public readonly record struct ViewerSnapshot(double Zoom, SKPoint Offset, ViewerFitMode Mode);
