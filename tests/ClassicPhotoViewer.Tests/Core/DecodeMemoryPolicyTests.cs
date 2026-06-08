using ClassicPhotoViewer.Caching;
using ClassicPhotoViewer.Decoding;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class DecodeMemoryPolicyTests
{
    [TestMethod]
    public void GetInteractiveDecodeLimit_Uses_Selected_Budget_As_Strict_Limit()
    {
        var limit = DecodeMemoryPolicy.GetInteractiveDecodeLimit(2L * ImageMemoryEstimator.Gigabyte);

        Assert.AreEqual(2L * ImageMemoryEstimator.Gigabyte, limit);
    }

    [TestMethod]
    public void GetInteractiveDecodeLimit_Honors_Larger_Selected_Budget()
    {
        var limit = DecodeMemoryPolicy.GetInteractiveDecodeLimit(10L * ImageMemoryEstimator.Gigabyte);

        Assert.AreEqual(10L * ImageMemoryEstimator.Gigabyte, limit);
    }

    [TestMethod]
    public void GetPreloadDecodeLimit_Leaves_Room_For_Transient_Decoder_Copies()
    {
        var limit = DecodeMemoryPolicy.GetPreloadDecodeLimit(2L * ImageMemoryEstimator.Gigabyte);

        Assert.AreEqual(2L * ImageMemoryEstimator.Gigabyte / 3, limit);
    }

    [TestMethod]
    public void GetPreloadDecodeLimit_Has_Minimum_Safety_Floor()
    {
        var limit = DecodeMemoryPolicy.GetPreloadDecodeLimit(64L * ImageMemoryEstimator.Megabyte);

        Assert.AreEqual(128L * ImageMemoryEstimator.Megabyte, limit);
    }

    [TestMethod]
    public void GetPreloadDecodeLimit_Clamps_Negative_Budget()
    {
        var limit = DecodeMemoryPolicy.GetPreloadDecodeLimit(-1);

        Assert.AreEqual(128L * ImageMemoryEstimator.Megabyte, limit);
    }

    [TestMethod]
    public void GetFullPreloadDecodeLimit_Uses_Selected_Budget()
    {
        var limit = DecodeMemoryPolicy.GetFullPreloadDecodeLimit(15L * ImageMemoryEstimator.Gigabyte);

        Assert.AreEqual(15L * ImageMemoryEstimator.Gigabyte, limit);
    }

    [TestMethod]
    public void GetFullPreloadDecodeLimit_Clamps_Negative_Budget()
    {
        var limit = DecodeMemoryPolicy.GetFullPreloadDecodeLimit(-1);

        Assert.AreEqual(0, limit);
    }

    [TestMethod]
    public void GetPreloadPreviewDecodeLimit_Uses_Scratch_Floor_For_Small_Previews()
    {
        var limit = DecodeMemoryPolicy.GetPreloadPreviewDecodeLimit(
            2L * ImageMemoryEstimator.Megabyte,
            512L * ImageMemoryEstimator.Megabyte);

        Assert.AreEqual(64L * ImageMemoryEstimator.Megabyte, limit);
    }

    [TestMethod]
    public void GetPreloadPreviewDecodeLimit_Allows_Scratch_Above_Stored_Preview_Size()
    {
        var limit = DecodeMemoryPolicy.GetPreloadPreviewDecodeLimit(
            100L * ImageMemoryEstimator.Megabyte,
            512L * ImageMemoryEstimator.Megabyte);

        Assert.AreEqual(400L * ImageMemoryEstimator.Megabyte, limit);
    }

    [TestMethod]
    public void GetPreloadPreviewDecodeLimit_Does_Not_Exceed_Preload_Limit()
    {
        var limit = DecodeMemoryPolicy.GetPreloadPreviewDecodeLimit(
            200L * ImageMemoryEstimator.Megabyte,
            512L * ImageMemoryEstimator.Megabyte);

        Assert.AreEqual(512L * ImageMemoryEstimator.Megabyte, limit);
    }

    [TestMethod]
    public void GetLargeFullPreloadLimit_Follows_Memory_Budget_Tiers()
    {
        Assert.AreEqual(0, DecodeMemoryPolicy.GetLargeFullPreloadLimit(1L * ImageMemoryEstimator.Gigabyte));
        Assert.AreEqual(0, DecodeMemoryPolicy.GetLargeFullPreloadLimit(2L * ImageMemoryEstimator.Gigabyte));
        Assert.AreEqual(1, DecodeMemoryPolicy.GetLargeFullPreloadLimit(5L * ImageMemoryEstimator.Gigabyte));
        Assert.AreEqual(2, DecodeMemoryPolicy.GetLargeFullPreloadLimit(10L * ImageMemoryEstimator.Gigabyte));
        Assert.AreEqual(3, DecodeMemoryPolicy.GetLargeFullPreloadLimit(15L * ImageMemoryEstimator.Gigabyte));
    }

    [TestMethod]
    public void MagickResourceLimitPolicy_Leaves_Unbounded_Decodes_Unbounded()
    {
        var profile = MagickResourceLimitPolicy.Create(null);

        Assert.IsNull(profile);
    }

    [TestMethod]
    public void MagickResourceLimitPolicy_Uses_Decode_Limit_For_Pixel_Cache()
    {
        var profile = MagickResourceLimitPolicy.Create(512L * ImageMemoryEstimator.Megabyte);

        Assert.IsNotNull(profile);
        Assert.AreEqual((ulong)(512L * ImageMemoryEstimator.Megabyte), profile.Value.Memory);
        Assert.AreEqual((ulong)(1024L * ImageMemoryEstimator.Megabyte), profile.Value.Disk);
        Assert.AreEqual((ulong)(128L * ImageMemoryEstimator.Megabyte), profile.Value.Area);
    }

    [TestMethod]
    public void MagickResourceLimitPolicy_Keeps_Small_Limits_At_Safety_Floor()
    {
        var profile = MagickResourceLimitPolicy.Create(1);

        Assert.IsNotNull(profile);
        Assert.AreEqual((ulong)(128L * ImageMemoryEstimator.Megabyte), profile.Value.Memory);
        Assert.AreEqual((ulong)(256L * ImageMemoryEstimator.Megabyte), profile.Value.Disk);
        Assert.AreEqual((ulong)(128L * ImageMemoryEstimator.Megabyte), profile.Value.MaxMemoryRequest);
    }

    [TestMethod]
    public void MagickResourceLimitPolicy_Caps_Large_Single_Requests()
    {
        var profile = MagickResourceLimitPolicy.Create(2L * ImageMemoryEstimator.Gigabyte);

        Assert.IsNotNull(profile);
        Assert.AreEqual((ulong)(2L * ImageMemoryEstimator.Gigabyte), profile.Value.Memory);
        Assert.AreEqual((ulong)(256L * ImageMemoryEstimator.Megabyte), profile.Value.MaxMemoryRequest);
    }
}
