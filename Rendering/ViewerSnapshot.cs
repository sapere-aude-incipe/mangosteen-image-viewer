using SkiaSharp;

namespace ClassicPhotoViewer.Rendering;

public readonly record struct ViewerSnapshot(double Zoom, SKPoint Offset, ViewerFitMode Mode);
