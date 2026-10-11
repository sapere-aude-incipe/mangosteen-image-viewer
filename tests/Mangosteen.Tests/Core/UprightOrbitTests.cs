using System.Windows.Media.Media3D;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class UprightOrbitTests
{
    [TestMethod]
    public void Horizontal_Orbit_Preserves_Height_And_Distance()
    {
        var offset = new Vector3D(3, 4, 5);
        Assert.IsTrue(UprightOrbit.TryRotate(offset, -90, 0, out var rotated));
        Assert.AreEqual(-5, rotated.X, 1e-10);
        Assert.AreEqual(4, rotated.Y, 1e-10);
        Assert.AreEqual(3, rotated.Z, 1e-10);
        Assert.AreEqual(offset.Length, rotated.Length, 1e-10);
    }

    [TestMethod]
    [DataRow(10000.0, 89.0)]
    [DataRow(-10000.0, -89.0)]
    [DataRow(double.MaxValue, 89.0)]
    public void Pitch_Stops_Short_Of_The_Poles(double delta, double expected)
    {
        Assert.IsTrue(UprightOrbit.TryRotate(new(0, 0, 10), 0, delta, out var rotated));
        Assert.AreEqual(expected, Math.Asin(rotated.Y / rotated.Length) * 180 / Math.PI, 1e-8);
        Assert.IsGreaterThan(0, rotated.Z);
        Assert.AreEqual(10, rotated.Length, 1e-10);
        Assert.IsTrue(UprightOrbit.TryRotate(rotated, 20, -Math.Sign(delta) * 10, out var back));
        Assert.AreEqual(expected - Math.Sign(delta) * 10, Math.Asin(back.Y / back.Length) * 180 / Math.PI, 1e-8);
    }

    [TestMethod]
    public void Reverse_Drag_Restores_Offset_And_Yaw_Wraps()
    {
        var offset = new Vector3D(3, 4, 5);
        Assert.IsTrue(UprightOrbit.TryRotate(offset, 45, 10, out var rotated));
        Assert.IsTrue(UprightOrbit.TryRotate(rotated, -45, -10, out var restored));
        Assert.AreEqual(0, (restored - offset).Length, 1e-10);
        Assert.IsTrue(UprightOrbit.TryRotate(offset, 360 * 1000, 0, out var wrapped));
        Assert.AreEqual(0, (wrapped - offset).Length, 1e-10);
    }

    [TestMethod]
    public void Invalid_Inputs_Do_Not_Move_The_Camera()
    {
        Assert.IsFalse(UprightOrbit.TryRotate(new(), 10, 10, out _));
        Assert.IsFalse(UprightOrbit.TryRotate(new(double.NaN, 0, 10), 10, 10, out _));
        Assert.IsFalse(UprightOrbit.TryRotate(new(0, double.PositiveInfinity, 10), 10, 10, out _));
        var offset = new Vector3D(0, 0, 10);
        Assert.IsFalse(UprightOrbit.TryRotate(offset, double.NaN, 0, out var unchanged));
        Assert.AreEqual(offset, unchanged);
        Assert.IsFalse(UprightOrbit.TryRotate(offset, 0, double.PositiveInfinity, out _));
    }
}
