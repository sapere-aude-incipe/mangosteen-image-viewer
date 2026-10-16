using System.Windows.Media.Media3D;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class OrientationIndicatorTests
{
    [TestMethod]
    public void Camera_Basis_Is_Orthonormal_And_Independent_Of_Pan_And_Zoom()
    {
        var position = new Vector3D(4, 3, 7);
        var focal = new Vector3D(1, 0, 2);
        var up = new Vector3D(0, 1, 0);
        var basis = ModelViewOrientation.FromCamera(position, focal, up);
        Assert.AreEqual(1, basis.Right.Length, 1e-10);
        Assert.AreEqual(1, basis.Up.Length, 1e-10);
        Assert.AreEqual(1, basis.TowardViewer.Length, 1e-10);
        Assert.AreEqual(0, Vector3D.DotProduct(basis.Right, basis.Up), 1e-10);
        Assert.AreEqual(0, Vector3D.DotProduct(basis.Right, basis.TowardViewer), 1e-10);
        var pan = new Vector3D(1000, -500, 800);
        Assert.IsLessThan(1e-10, (basis.Right - ModelViewOrientation.FromCamera(position + pan, focal + pan, up).Right).Length);
        Assert.IsLessThan(1e-10, (basis.Up - ModelViewOrientation.FromCamera(focal + (position - focal) * 10, focal, up).Up).Length);
    }

    [TestMethod]
    public void Invalid_Camera_Basis_Falls_Back_To_A_Finite_Orientation()
    {
        foreach (var basis in new[]
        {
            ModelViewOrientation.FromCamera(default, default, new(0, 1, 0)),
            ModelViewOrientation.FromCamera(new(0, 0, 1), default, new(0, 0, 1)),
            ModelViewOrientation.FromCamera(new(double.NaN, 0, 1), default, new(0, 1, 0))
        }) Assert.AreEqual(ModelViewOrientation.Default, basis);
    }

    [TestMethod]
    [DataRow(true, 112)]
    [DataRow(false, 112)]
    [DataRow(true, 168)]
    [DataRow(false, 224)]
    public void Indicator_Has_Antialiased_Edges_And_Transparent_Padding(bool darkMode, int size)
    {
        var basis = ModelViewOrientation.FromCamera(new(3, 2, 5), default, new(0, 1, 0));
        using var bitmap = OrientationIndicator.Rasterize(basis, darkMode, size);
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.AreEqual(0, bitmap.GetPixel(size - 1, size - 1).Alpha);
        var edgePixels = 0;
        foreach (var pixel in bitmap.Pixels)
            if (pixel.Alpha > 0 && pixel.Alpha < 255) edgePixels++;
        Assert.IsGreaterThan(100, edgePixels, "Curves and glyphs need intermediate edge coverage.");
        for (var axis = 0; axis < 3; axis++)
        {
            var color = OrientationIndicator.AxisColor(axis, darkMode);
            Assert.IsTrue(bitmap.Pixels.Any(pixel => pixel.Alpha == 255 &&
                Math.Abs(pixel.Red - color.Red) < 2 && Math.Abs(pixel.Green - color.Green) < 2 && Math.Abs(pixel.Blue - color.Blue) < 2));
        }
    }
}
