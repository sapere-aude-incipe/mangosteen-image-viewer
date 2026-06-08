using ClassicPhotoViewer.Core;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class PixelSizeTests
{
    [TestMethod]
    public void FromDipsWithFallback_Uses_Measured_Size_When_Available()
    {
        var size = PixelSize.FromDipsWithFallback(
            width: 640,
            height: 480,
            fallbackWidth: 1024,
            fallbackHeight: 768,
            dpiScaleX: 1,
            dpiScaleY: 1);

        Assert.AreEqual(new PixelSize(640, 480), size);
    }

    [TestMethod]
    public void FromDipsWithFallback_Uses_Fallback_For_Unmeasured_Size()
    {
        var size = PixelSize.FromDipsWithFallback(
            width: 0,
            height: double.NaN,
            fallbackWidth: 1024,
            fallbackHeight: 682,
            dpiScaleX: 1,
            dpiScaleY: 1);

        Assert.AreEqual(new PixelSize(1024, 682), size);
    }

    [TestMethod]
    public void FromDipsWithFallback_Applies_Dpi_Scaling()
    {
        var size = PixelSize.FromDipsWithFallback(
            width: 100,
            height: 50,
            fallbackWidth: 200,
            fallbackHeight: 100,
            dpiScaleX: 1.5,
            dpiScaleY: 2.0);

        Assert.AreEqual(new PixelSize(150, 100), size);
    }

    [TestMethod]
    public void FromDips_Uses_OneToOne_Scale_When_DpiScale_Is_Invalid()
    {
        var size = PixelSize.FromDips(
            width: 100,
            height: 50,
            dpiScaleX: 0,
            dpiScaleY: double.NaN);

        Assert.AreEqual(new PixelSize(100, 50), size);
    }

    [TestMethod]
    public void FromDips_Clamps_Unusable_Dimensions_To_One_Pixel()
    {
        var size = PixelSize.FromDips(
            width: double.NaN,
            height: -10,
            dpiScaleX: 1,
            dpiScaleY: 1);

        Assert.AreEqual(new PixelSize(1, 1), size);
    }

    [TestMethod]
    public void FromDips_Saturates_Overflowing_Pixel_Lengths()
    {
        var size = PixelSize.FromDips(
            width: double.MaxValue,
            height: int.MaxValue,
            dpiScaleX: 2,
            dpiScaleY: 2);

        Assert.AreEqual(new PixelSize(int.MaxValue, int.MaxValue), size);
    }
}
