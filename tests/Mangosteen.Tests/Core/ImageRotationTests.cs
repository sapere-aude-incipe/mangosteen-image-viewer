using Mangosteen.Core;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ImageRotationTests
{
    [TestMethod]
    public void NormalizeQuarterTurns_Wraps_Both_Directions()
    {
        Assert.AreEqual(3, ImageRotation.NormalizeQuarterTurns(-1));
        Assert.AreEqual(0, ImageRotation.NormalizeQuarterTurns(4));
        Assert.AreEqual(1, ImageRotation.NormalizeQuarterTurns(5));
    }

    [TestMethod]
    public void GetRotatedSize_Swaps_Dimensions_For_Odd_QuarterTurns()
    {
        Assert.AreEqual(new PixelSize(640, 480), ImageRotation.GetRotatedSize(640, 480, 0));
        Assert.AreEqual(new PixelSize(480, 640), ImageRotation.GetRotatedSize(640, 480, 1));
        Assert.AreEqual(new PixelSize(640, 480), ImageRotation.GetRotatedSize(640, 480, 2));
        Assert.AreEqual(new PixelSize(480, 640), ImageRotation.GetRotatedSize(640, 480, 3));
    }
}
