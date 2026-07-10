using Mangosteen.Icons;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ToolbarIconTests
{
    [TestMethod]
    public void All_Toolbar_Icon_Geometries_Parse()
    {
        foreach (var icon in ToolbarIcon.AllKinds)
        {
            var geometry = ToolbarIcon.GetGeometry(icon);

            Assert.IsFalse(geometry.Bounds.IsEmpty, $"{icon} should have non-empty geometry.");
        }
    }

    [TestMethod]
    public void Toolbar_Icon_Geometries_Are_Frozen_And_Reused()
    {
        var first = ToolbarIcon.GetGeometry(ToolbarIconKind.Zoom);
        var second = ToolbarIcon.GetGeometry(ToolbarIconKind.Zoom);

        Assert.AreSame(first, second);
        Assert.IsTrue(first.IsFrozen);
    }
}
