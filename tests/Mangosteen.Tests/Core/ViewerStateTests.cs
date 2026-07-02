using Mangosteen.Core;
using Mangosteen.Rendering;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ViewerStateTests
{
    [TestMethod]
    public void ActualPixels_Uses_One_Image_Pixel_Per_Screen_Pixel()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(800, 600));
        state.SetImage(1600, 1200, fitToWindow: true);

        state.SetActualPixels();

        Assert.AreEqual(1.0, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.ActualPixels, state.Mode);
    }

    [TestMethod]
    public void FitZoom_Is_One_When_Image_Is_Smaller_Than_Viewport()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(800, 600));
        state.SetImage(320, 240, fitToWindow: true);

        Assert.IsTrue(state.FitsAtActualPixels);
        Assert.AreEqual(1.0, state.FitZoom, 0.0001);
        Assert.AreEqual(1.0, state.Zoom, 0.0001);
    }

    [TestMethod]
    public void ActualPixels_On_Small_Zoomed_Image_Returns_To_One_To_One()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(800, 600));
        state.SetImage(320, 240, fitToWindow: true);

        state.ZoomAt(14.56, new SKPoint(400, 300));
        state.SetActualPixels();

        Assert.AreEqual(1.0, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.ActualPixels, state.Mode);
    }

    [TestMethod]
    public void MouseWheel_Cannot_Zoom_Out_Below_FitZoom()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(720, 720));
        state.SetImage(1000, 1000, fitToWindow: true);

        state.ZoomAt(1.5, new SKPoint(360, 360));
        state.ZoomAt(0.01, new SKPoint(360, 360));

        Assert.AreEqual(0.72, state.FitZoom, 0.0001);
        Assert.AreEqual(state.FitZoom, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.Fit, state.Mode);
    }

    [TestMethod]
    public void MouseWheel_Zoom_In_From_Tiny_FitZoom_Does_Not_Jump_To_MinZoom()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(1000, 1000));
        state.SetImage(100_000, 100_000, fitToWindow: true);

        var fitZoom = state.FitZoom;
        state.ZoomAt(1.15, new SKPoint(500, 500));

        Assert.AreEqual(0.01, fitZoom, 0.0001);
        Assert.AreEqual(fitZoom * 1.15, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.Custom, state.Mode);
    }

    [TestMethod]
    public void MouseWheel_Zoom_Out_To_Fit_Reenters_Fit_Mode()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(1000, 1000));
        state.SetImage(100_000, 100_000, fitToWindow: true);

        state.ZoomAt(1.15, new SKPoint(500, 500));
        state.ZoomAt(0.01, new SKPoint(500, 500));

        Assert.AreEqual(state.FitZoom, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.Fit, state.Mode);
    }

    [TestMethod]
    public void Snapshot_Restores_Previous_View_After_ActualPixels()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(800, 600));
        state.SetImage(1600, 1200, fitToWindow: true);
        var snapshot = state.Capture();

        state.SetActualPixels();
        state.Restore(snapshot);

        Assert.AreEqual(snapshot.Zoom, state.Zoom, 0.0001);
        Assert.AreEqual(snapshot.Offset.X, state.Offset.X, 0.001);
        Assert.AreEqual(snapshot.Offset.Y, state.Offset.Y, 0.001);
        Assert.AreEqual(snapshot.Mode, state.Mode);
    }

    [TestMethod]
    public void ZoomAt_Keeps_Target_Image_Point_Under_Cursor()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(1000, 800));
        state.SetImage(1000, 800, fitToWindow: true);
        var cursor = new SKPoint(500, 400);
        var before = ImagePointAt(state, cursor);

        state.ZoomAt(2.0, cursor);
        var after = ImagePointAt(state, cursor);

        Assert.AreEqual(before.X, after.X, 0.001);
        Assert.AreEqual(before.Y, after.Y, 0.001);
    }

    [TestMethod]
    public void Replacing_Preview_With_Full_Image_Preserves_Custom_View_Relative_To_Fit()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(1000, 800));
        state.SetImage(1000, 750, fitToWindow: true);
        state.ZoomAt(1.15, new SKPoint(500, 400));
        var before = RelativeCenter(state);

        state.SetImage(4000, 3000, fitToWindow: false);
        var after = RelativeCenter(state);

        Assert.AreEqual(0.25 * 1.15, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.Custom, state.Mode);
        Assert.AreEqual(before.X, after.X, 0.001);
        Assert.AreEqual(before.Y, after.Y, 0.001);
    }

    [TestMethod]
    public void Replacing_Loading_Preview_With_Full_Image_Preserves_ActualPixels_View()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(1000, 800));
        state.SetImage(4000, 3000, fitToWindow: true);
        state.SetActualPixels();
        var before = state.GetDestinationRect();

        state.SetImage(4000, 3000, fitToWindow: false);
        var after = state.GetDestinationRect();

        Assert.AreEqual(1.0, state.Zoom, 0.0001);
        Assert.AreEqual(ViewerFitMode.ActualPixels, state.Mode);
        Assert.AreEqual(before.Left, after.Left, 0.001);
        Assert.AreEqual(before.Top, after.Top, 0.001);
        Assert.AreEqual(before.Width, after.Width, 0.001);
        Assert.AreEqual(before.Height, after.Height, 0.001);
    }

    [TestMethod]
    public void Pan_Is_Clamped_To_Viewport()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(500, 500));
        state.SetImage(1000, 1000, fitToWindow: false);
        state.SetActualPixels();

        state.PanBy(new SKPoint(-10_000, -10_000));
        var rect = state.GetDestinationRect();

        Assert.AreEqual(-500, rect.Left, 0.001);
        Assert.AreEqual(-500, rect.Top, 0.001);
    }

    [TestMethod]
    public void Pan_Does_Not_Leave_ActualPixels_Mode()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(500, 500));
        state.SetImage(1000, 1000, fitToWindow: true);
        state.SetActualPixels();

        state.PanBy(new SKPoint(-100, -50));

        Assert.AreEqual(ViewerFitMode.ActualPixels, state.Mode);
        Assert.AreEqual(1.0, state.Zoom, 0.0001);
    }

    [TestMethod]
    public void Pan_Does_Not_Leave_Fit_Mode_When_Image_Is_Fully_Visible()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(800, 600));
        state.SetImage(1600, 1200, fitToWindow: true);
        var before = state.GetDestinationRect();

        state.PanBy(new SKPoint(50, 30));
        var after = state.GetDestinationRect();

        Assert.AreEqual(ViewerFitMode.Fit, state.Mode);
        Assert.AreEqual(before.Left, after.Left, 0.001);
        Assert.AreEqual(before.Top, after.Top, 0.001);
    }

    [TestMethod]
    public void ClearImage_Resets_Image_Zoom_Offset_And_Mode()
    {
        var state = new ViewerState();
        state.SetViewport(new PixelSize(800, 600));
        state.SetImage(1600, 1200, fitToWindow: true);
        state.SetActualPixels();
        state.PanBy(new SKPoint(-100, -50));

        state.ClearImage();

        Assert.IsFalse(state.HasImage);
        Assert.AreEqual(PixelSize.Empty, state.ImageSize);
        Assert.AreEqual(1.0, state.Zoom, 0.0001);
        Assert.AreEqual(0, state.Offset.X, 0.001);
        Assert.AreEqual(0, state.Offset.Y, 0.001);
        Assert.AreEqual(ViewerFitMode.Fit, state.Mode);
    }

    private static SKPoint ImagePointAt(ViewerState state, SKPoint viewportPoint)
    {
        return new SKPoint(
            (float)((viewportPoint.X - state.Offset.X) / state.Zoom),
            (float)((viewportPoint.Y - state.Offset.Y) / state.Zoom));
    }

    private static SKPoint RelativeCenter(ViewerState state)
    {
        var viewportCenter = new SKPoint(state.ViewportSize.Width / 2f, state.ViewportSize.Height / 2f);
        var imagePoint = ImagePointAt(state, viewportCenter);
        return new SKPoint(
            imagePoint.X / state.ImageSize.Width,
            imagePoint.Y / state.ImageSize.Height);
    }
}
