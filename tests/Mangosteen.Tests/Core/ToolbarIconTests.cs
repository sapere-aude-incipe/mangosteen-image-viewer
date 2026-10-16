using Mangosteen.Icons;

using System.Windows.Controls;
using System.Windows.Media;
using IconPath = System.Windows.Shapes.Path;

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

    [TestMethod]
    public void Square_Rotation_Pair_Has_Opposite_Arrows_And_No_Center_Dot()
    {
        var left = ToolbarIcon.GetGeometry(ToolbarIconKind.RotateLeft);
        var right = ToolbarIcon.GetGeometry(ToolbarIconKind.RotateRight);
        var pen = new Pen(Brushes.Black, 2.1);
        var leftStroke = left.GetWidenedPathGeometry(pen);
        var rightStroke = right.GetWidenedPathGeometry(pen);
        Assert.IsTrue(leftStroke.FillContains(new System.Windows.Point(14, 3)));
        Assert.IsFalse(rightStroke.FillContains(new System.Windows.Point(14, 3)));
        Assert.IsTrue(rightStroke.FillContains(new System.Windows.Point(10, 3)));
        Assert.IsFalse(leftStroke.FillContains(new System.Windows.Point(10, 3)));
        Assert.AreNotEqual(left.ToString(), right.ToString());
        Assert.IsFalse(leftStroke.FillContains(new System.Windows.Point(12, 12)), "Rotation glyphs must not contain a center dot.");
        Assert.IsFalse(rightStroke.FillContains(new System.Windows.Point(12, 12)));
        Assert.AreNotEqual(left.ToString(), ToolbarIcon.GetGeometry(ToolbarIconKind.ResetView).ToString());
    }

    [TestMethod]
    [DataRow("RotateLeft")]
    [DataRow("RotateRight")]
    [DataRow("ResetView")]
    public Task Glyphs_Are_Themed_And_Rotation_Icons_Are_Smaller(string name) => StaTest.RunAsync(() =>
    {
        var kind = Enum.Parse<ToolbarIconKind>(name);
        foreach (var brush in new[] { Brushes.White, Brushes.Black })
        {
            var icon = ToolbarIcon.Create(kind, brush);
            var isReset = kind == ToolbarIconKind.ResetView;
            Assert.AreEqual(isReset ? 20 : 17, icon.Width);
            Assert.AreEqual(isReset ? 20 : 17, icon.Height);
            var canvas = (Canvas)icon.Child;
            Assert.AreEqual(isReset ? 20 : 24, canvas.Width);
            var path = (IconPath)canvas.Children[0];
            Assert.AreSame(isReset ? brush : Brushes.Transparent, path.Fill);
            Assert.AreSame(isReset ? null : brush, path.Stroke);
            Assert.AreEqual(isReset ? 0 : 2.1, path.StrokeThickness);
            Assert.AreSame(ToolbarIcon.GetGeometry(kind), path.Data);
        }
        return Task.CompletedTask;
    });
}
