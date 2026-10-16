using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ModelOrbitInputTests
{
    [TestMethod]
    [DataRow(50.0, 0.0, 1000, 800, -10.0, 0.0)]
    [DataRow(-50.0, 0.0, 1000, 800, 10.0, 0.0)]
    [DataRow(0.0, 40.0, 1000, 800, 0.0, 10.0)]
    [DataRow(0.0, -40.0, 1000, 800, 0.0, -10.0)]
    [DataRow(100.0, 80.0, 2000, 1600, -10.0, 10.0)]
    [DataRow(25.0, 20.0, 500, 400, -10.0, 10.0)]
    public void Drag_Follows_The_Pointer_With_Viewport_Relative_Sensitivity(double x, double y, int width, int height, double azimuth, double elevation)
    {
        var rotation = F3dModelRenderer.GetDragRotation(x, y, width, height);
        Assert.AreEqual(azimuth, rotation.Azimuth, 1e-10);
        Assert.AreEqual(elevation, rotation.Elevation, 1e-10);
    }

    [TestMethod]
    [DataRow(0, 800)]
    [DataRow(1000, 0)]
    [DataRow(1, 1)]
    public void Hidden_Or_Empty_Surface_Does_Not_Rotate(int width, int height) =>
        Assert.AreEqual((0.0, 0.0), F3dModelRenderer.GetDragRotation(100, 100, width, height));

    [TestMethod]
    public void Invalid_Drag_Deltas_Are_Ignored()
    {
        Assert.AreEqual((0.0, 0.0), F3dModelRenderer.GetDragRotation(double.NaN, 10, 1000, 800));
        Assert.AreEqual((0.0, 0.0), F3dModelRenderer.GetDragRotation(10, double.PositiveInfinity, 1000, 800));
    }
}
