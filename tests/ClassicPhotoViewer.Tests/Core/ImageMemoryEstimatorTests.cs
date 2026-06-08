using ClassicPhotoViewer.Caching;
using ClassicPhotoViewer.Core;
using ClassicPhotoViewer.Decoding;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class ImageMemoryEstimatorTests
{
    [TestMethod]
    public void GetRemainingCacheBudget_Subtracts_Active_Image_Bytes()
    {
        var remaining = ImageMemoryEstimator.GetRemainingCacheBudget(
            selectedBudgetBytes: 5 * ImageMemoryEstimator.Gigabyte,
            activeImageBytes: 2 * ImageMemoryEstimator.Gigabyte);

        Assert.AreEqual(3 * ImageMemoryEstimator.Gigabyte, remaining);
    }

    [TestMethod]
    public void GetRemainingCacheBudget_Returns_Zero_When_Active_Image_Exceeds_Budget()
    {
        var remaining = ImageMemoryEstimator.GetRemainingCacheBudget(
            selectedBudgetBytes: ImageMemoryEstimator.Gigabyte,
            activeImageBytes: 2 * ImageMemoryEstimator.Gigabyte);

        Assert.AreEqual(0, remaining);
    }

    [TestMethod]
    public void GetRemainingCacheBudget_Clamps_Negative_Inputs()
    {
        Assert.AreEqual(0, ImageMemoryEstimator.GetRemainingCacheBudget(-1, 0));
        Assert.AreEqual(10, ImageMemoryEstimator.GetRemainingCacheBudget(10, -1));
    }

    [TestMethod]
    public void EstimatePreviewBytes_Uses_One_Frame_For_Scaled_Previews()
    {
        var metadata = new ImageMetadata("animated.gif", 1000, 1000, 20, "test");

        var bytes = ImageMemoryEstimator.EstimatePreviewBytes(metadata, new PixelSize(100, 100));

        Assert.AreEqual(100 * 100 * 4L, bytes);
    }

    [TestMethod]
    public void EstimatePreviewBytes_Uses_One_Frame_When_Preview_Is_Full_Size()
    {
        var metadata = new ImageMetadata("animated.gif", 100, 100, 20, "test");

        var bytes = ImageMemoryEstimator.EstimatePreviewBytes(metadata, new PixelSize(200, 200));

        Assert.AreEqual(100 * 100 * 4L, bytes);
    }

    [TestMethod]
    public void EstimatePreviewBytes_Uses_One_Frame_When_Preview_Size_Is_Empty()
    {
        var metadata = new ImageMetadata("animated.gif", 100, 100, 20, "test");

        var bytes = ImageMemoryEstimator.EstimatePreviewBytes(metadata, PixelSize.Empty);

        Assert.AreEqual(100 * 100 * 4L, bytes);
    }

    [TestMethod]
    public void DecodeGuard_Throws_When_All_Frame_Estimate_Exceeds_Limit()
    {
        var metadata = new ImageMetadata("animated.gif", 100, 100, 3, "test");

        InvalidDataException? ex = null;
        try
        {
            ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
                metadata,
                new PixelSize(100, 100),
                decodesAllFrames: true,
                maxDecodedBytes: 100 * 100 * 2 * 4L);
        }
        catch (InvalidDataException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "exceeds the decode limit");
    }

    [TestMethod]
    public void DecodeGuard_Uses_One_Frame_For_Scaled_Preview_Estimate()
    {
        var metadata = new ImageMetadata("animated.gif", 100, 100, 30, "test");

        ImageDecodeGuards.ThrowIfEstimatedDecodedBytesExceedLimit(
            metadata,
            new PixelSize(100, 100),
            decodesAllFrames: false,
            maxDecodedBytes: 100 * 100 * 4L);
    }

    [TestMethod]
    public void DecodeGuard_Throws_When_Actual_Single_Frame_Exceeds_Limit()
    {
        InvalidDataException? ex = null;
        try
        {
            ImageDecodeGuards.ThrowIfSingleFrameDecodedBytesExceedLimit(
                width: 100,
                height: 100,
                maxDecodedBytes: 100 * 100 * 4L - 1);
        }
        catch (InvalidDataException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "exceeds the decode limit");
    }

    [TestMethod]
    public void DecodeGuard_Rejects_Buffers_That_Do_Not_Fit_Decoder_Api_Length()
    {
        InvalidDataException? ex = null;
        try
        {
            ImageDecodeGuards.ThrowIfSingleFrameBgraBufferExceedsDecoderLimit(50_000, 50_000);
        }
        catch (InvalidDataException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "single-frame buffer limit");
    }

    [TestMethod]
    public void DecodeGuard_Returns_Int_Buffer_Length_Below_Decoder_Api_Limit()
    {
        var length = ImageDecodeGuards.GetBgraBufferLength(23_170, 23_170);

        Assert.IsGreaterThan(0, length);
        Assert.IsLessThanOrEqualTo(ImageDecodeGuards.MaxSingleFrameBgraBufferBytes, length);
    }
}
