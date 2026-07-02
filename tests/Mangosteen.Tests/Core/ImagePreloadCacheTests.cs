using Mangosteen.Caching;
using Mangosteen.Decoding;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ImagePreloadCacheTests
{
    [TestMethod]
    public void Store_Evicts_Oldest_Image_To_Stay_Under_Budget()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 800
        };

        cache.Store("a.jpg", CreateImage("a.jpg", 10, 10, isFull: true));
        cache.Store("b.jpg", CreateImage("b.jpg", 10, 10, isFull: true));
        cache.Store("c.jpg", CreateImage("c.jpg", 10, 10, isFull: true));

        Assert.IsFalse(cache.TryTake("a.jpg", out _));
        Assert.IsTrue(cache.TryTake("b.jpg", out var b));
        Assert.IsTrue(cache.TryTake("c.jpg", out var c));

        b?.Dispose();
        c?.Dispose();
    }

    [TestMethod]
    public void Store_Replaces_Preview_With_FullResolution_Image()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 2_000
        };

        cache.Store("photo.jpg", CreateImage("photo.jpg", 10, 10, isFull: false));
        cache.Store("photo.jpg", CreateImage("photo.jpg", 20, 20, isFull: true));

        Assert.IsTrue(cache.TryTake("photo.jpg", out var image));
        Assert.IsNotNull(image);
        Assert.IsTrue(image.IsFullResolution);
        Assert.AreEqual(1_600, image.EstimatedBytes);

        image.Dispose();
    }

    [TestMethod]
    public void Store_Preserves_Existing_Preview_When_Full_Replacement_Cannot_Fit()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 2_000
        };

        cache.Store("photo.jpg", CreateImage("photo.jpg", 10, 10, isFull: false), evictionPriority: 0);
        cache.Store("next.jpg", CreateImage("next.jpg", 20, 20, isFull: true), evictionPriority: 0);
        var storedFull = cache.Store("photo.jpg", CreateImage("photo.jpg", 20, 20, isFull: true), evictionPriority: 20);

        Assert.IsFalse(storedFull);
        Assert.IsTrue(cache.TryTake("photo.jpg", out var preview));
        Assert.IsNotNull(preview);
        Assert.IsFalse(preview.IsFullResolution);
        Assert.AreEqual(400, preview.EstimatedBytes);
        Assert.IsTrue(cache.TryTake("next.jpg", out var next));

        preview.Dispose();
        next?.Dispose();
    }

    [TestMethod]
    public void CanStore_With_Priority_Returns_False_When_Only_Protected_Images_Could_Make_Room()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 800
        };

        cache.Store("next.jpg", CreateImage("next.jpg", 10, 10, isFull: true), evictionPriority: 0);
        cache.Store("previous.jpg", CreateImage("previous.jpg", 10, 10, isFull: true), evictionPriority: 1);

        Assert.IsFalse(cache.CanStore("far.jpg", 400, evictionPriority: 20));
    }

    [TestMethod]
    public void CanStore_With_Priority_Returns_True_When_Lower_Priority_Image_Can_Be_Evicted()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 800
        };

        cache.Store("far.jpg", CreateImage("far.jpg", 10, 10, isFull: true), evictionPriority: 20);
        cache.Store("near.jpg", CreateImage("near.jpg", 10, 10, isFull: true), evictionPriority: 1);

        Assert.IsTrue(cache.CanStore("next.jpg", 400, evictionPriority: 0));
    }

    [TestMethod]
    public void CanStore_With_Priority_Accounts_For_Replacing_Current_Path()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 2_000
        };

        cache.Store("photo.jpg", CreateImage("photo.jpg", 10, 10, isFull: false), evictionPriority: 0);
        cache.Store("next.jpg", CreateImage("next.jpg", 20, 20, isFull: true), evictionPriority: 0);

        Assert.IsFalse(cache.CanStore("photo.jpg", 1_600, evictionPriority: 20));
        Assert.IsTrue(cache.CanStore("photo.jpg", 1_600, evictionPriority: 0));
    }

    [TestMethod]
    public void Store_Does_Not_Replace_FullResolution_Image_With_Preview()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 2_000
        };

        cache.Store("photo.jpg", CreateImage("photo.jpg", 20, 20, isFull: true));
        var storedPreview = cache.Store("photo.jpg", CreateImage("photo.jpg", 10, 10, isFull: false));

        Assert.IsFalse(storedPreview);
        Assert.IsTrue(cache.TryTake("photo.jpg", out var image));
        Assert.IsNotNull(image);
        Assert.IsTrue(image.IsFullResolution);
        Assert.AreEqual(1_600, image.EstimatedBytes);

        image.Dispose();
    }

    [TestMethod]
    public void Remove_Evicts_Only_Requested_Image()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 2_000
        };

        cache.Store("edited.jpg", CreateImage("edited.jpg", 10, 10, isFull: true));
        cache.Store("next.jpg", CreateImage("next.jpg", 10, 10, isFull: true));

        Assert.IsTrue(cache.Remove("edited.jpg"));

        Assert.IsFalse(cache.TryTake("edited.jpg", out _));
        Assert.IsTrue(cache.TryTake("next.jpg", out var next));
        Assert.AreEqual(0, cache.UsedBytes);

        next?.Dispose();
    }

    [TestMethod]
    public void Remove_Returns_False_For_Missing_Image()
    {
        using var cache = new ImagePreloadCache();

        Assert.IsFalse(cache.Remove("missing.jpg"));
    }

    [TestMethod]
    public void Store_Does_Not_Evict_Higher_Priority_Images_For_Lower_Priority_Preload()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 800
        };

        cache.Store("next.jpg", CreateImage("next.jpg", 10, 10, isFull: true), evictionPriority: 0);
        cache.Store("previous.jpg", CreateImage("previous.jpg", 10, 10, isFull: true), evictionPriority: 1);
        var storedFarImage = cache.Store("far.jpg", CreateImage("far.jpg", 10, 10, isFull: true), evictionPriority: 20);

        Assert.IsFalse(storedFarImage);
        Assert.IsTrue(cache.TryTake("next.jpg", out var next));
        Assert.IsTrue(cache.TryTake("previous.jpg", out var previous));
        Assert.IsFalse(cache.TryTake("far.jpg", out _));

        next?.Dispose();
        previous?.Dispose();
    }

    [TestMethod]
    public void Store_Evicts_Lower_Priority_Image_For_Higher_Priority_Preload()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 800
        };

        cache.Store("far.jpg", CreateImage("far.jpg", 10, 10, isFull: true), evictionPriority: 20);
        cache.Store("near.jpg", CreateImage("near.jpg", 10, 10, isFull: true), evictionPriority: 1);
        var storedNextImage = cache.Store("next.jpg", CreateImage("next.jpg", 10, 10, isFull: true), evictionPriority: 0);

        Assert.IsTrue(storedNextImage);
        Assert.IsFalse(cache.TryTake("far.jpg", out _));
        Assert.IsTrue(cache.TryTake("near.jpg", out var near));
        Assert.IsTrue(cache.TryTake("next.jpg", out var next));

        near?.Dispose();
        next?.Dispose();
    }

    [TestMethod]
    public void CanStore_Is_Based_On_Item_Size_Not_Current_Occupancy()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 800
        };

        cache.Store("a.jpg", CreateImage("a.jpg", 10, 10, isFull: true));
        cache.Store("b.jpg", CreateImage("b.jpg", 10, 10, isFull: true));

        Assert.IsTrue(cache.CanStore(800));
        Assert.IsFalse(cache.CanFit(800));
        Assert.IsFalse(cache.CanStore(801));
    }

    [TestMethod]
    public void CanFit_Returns_False_When_Byte_Addition_Would_Overflow()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = long.MaxValue
        };

        cache.Store("a.jpg", CreateImage("a.jpg", 10, 10, isFull: true));

        Assert.IsFalse(cache.CanFit(long.MaxValue));
    }

    [TestMethod]
    public void CanFit_For_Path_Allows_Replacing_Current_Image()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 1_000
        };

        cache.Store("a.jpg", CreateImage("a.jpg", 10, 10, isFull: true));

        Assert.IsTrue(cache.CanFit("a.jpg", 1_000));
    }

    [TestMethod]
    public void BudgetBytes_Shrink_Trims_Cache_And_Updates_UsedBytes()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 1_200
        };
        cache.Store("a.jpg", CreateImage("a.jpg", 10, 10, isFull: true));
        cache.Store("b.jpg", CreateImage("b.jpg", 10, 10, isFull: true));
        cache.Store("c.jpg", CreateImage("c.jpg", 10, 10, isFull: true));

        cache.BudgetBytes = 400;

        Assert.AreEqual(400, cache.UsedBytes);
        Assert.IsFalse(cache.TryTake("a.jpg", out _));
        Assert.IsFalse(cache.TryTake("b.jpg", out _));
        Assert.IsTrue(cache.TryTake("c.jpg", out var c));
        c?.Dispose();
    }

    [TestMethod]
    public void Dispose_Prevents_Late_Background_Stores()
    {
        using var cache = new ImagePreloadCache
        {
            BudgetBytes = 2_000
        };
        cache.Dispose();

        var stored = cache.Store("late.jpg", CreateImage("late.jpg", 10, 10, isFull: true));

        Assert.IsFalse(stored);
        Assert.IsFalse(cache.Contains("late.jpg"));
        Assert.AreEqual(0, cache.UsedBytes);
    }

    [TestMethod]
    public void Public_Path_Methods_Reject_Blank_Paths()
    {
        using var cache = new ImagePreloadCache();
        using var image = CreateImage("photo.jpg", 10, 10, isFull: true);

        Assert.ThrowsExactly<ArgumentException>(() => cache.Contains(""));
        Assert.ThrowsExactly<ArgumentException>(() => cache.ContainsFullResolution(" "));
        Assert.ThrowsExactly<ArgumentException>(() => cache.CanStore("", 400, evictionPriority: 0));
        Assert.ThrowsExactly<ArgumentException>(() => cache.CanFit(" ", 400));
        Assert.ThrowsExactly<ArgumentException>(() => cache.TryTake("", out _));
        Assert.ThrowsExactly<ArgumentException>(() => cache.Remove(" "));
        Assert.ThrowsExactly<ArgumentException>(() => cache.Store(" ", image));
    }

    [TestMethod]
    public void Store_Rejects_Null_Image()
    {
        using var cache = new ImagePreloadCache();

        Assert.ThrowsExactly<ArgumentNullException>(() => cache.Store("photo.jpg", null!));
    }

    private static DecodedImage CreateImage(string path, int width, int height, bool isFull)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        var image = surface.Snapshot();
        var metadata = new ImageMetadata(path, width, height, 1, "test");
        return new DecodedImage(metadata, [new DecodedFrame(image, TimeSpan.FromMilliseconds(100))], isFull);
    }
}
