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
}
