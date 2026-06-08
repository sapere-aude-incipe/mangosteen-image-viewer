using Mangosteen.Decoding;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class DecodedImageTests
{
    [TestMethod]
    public void EstimateFrameBytes_Saturates_On_Overflow()
    {
        var bytes = DecodedImage.EstimateFrameBytes(int.MaxValue, int.MaxValue);

        Assert.AreEqual(long.MaxValue, bytes);
    }

    [TestMethod]
    public void AddSaturating_Saturates_On_Overflow()
    {
        var bytes = DecodedImage.AddSaturating(long.MaxValue - 10, 11);

        Assert.AreEqual(long.MaxValue, bytes);
    }

    [TestMethod]
    public void EstimatedBytes_Remains_Available_After_Dispose()
    {
        using var image = CreateDecodedImage(10, 20);
        var beforeDispose = image.EstimatedBytes;

        image.Dispose();

        Assert.AreEqual(10 * 20 * 4L, beforeDispose);
        Assert.AreEqual(beforeDispose, image.EstimatedBytes);
    }

    [TestMethod]
    public void Dispose_Can_Be_Called_More_Than_Once()
    {
        using var image = CreateDecodedImage(10, 20);

        image.Dispose();
        image.Dispose();

        Assert.AreEqual(10 * 20 * 4L, image.EstimatedBytes);
    }

    [TestMethod]
    public void Constructor_Rejects_Empty_Frame_List()
    {
        var metadata = new ImageMetadata("test.png", 10, 20, 1, "test");

        ArgumentException? ex = null;
        try
        {
            _ = new DecodedImage(metadata, [], isFullResolution: true);
        }
        catch (ArgumentException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public void Constructor_Rejects_Null_Frame_Entries()
    {
        var metadata = new ImageMetadata("test.png", 10, 20, 1, "test");

        ArgumentException? ex = null;
        try
        {
            _ = new DecodedImage(metadata, [null!], isFullResolution: true);
        }
        catch (ArgumentException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public void ImageMetadata_Rejects_Invalid_Values()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ImageMetadata("", 10, 20, 1, "test"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ImageMetadata("test.png", 0, 20, 1, "test"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ImageMetadata("test.png", 10, -1, 1, "test"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ImageMetadata("test.png", 10, 20, 0, "test"));
        Assert.ThrowsExactly<ArgumentException>(() => new ImageMetadata("test.png", 10, 20, 1, " "));
    }

    [TestMethod]
    public void ImageMetadata_WithExpression_Rejects_Invalid_Values()
    {
        var metadata = new ImageMetadata("test.png", 10, 20, 1, "test");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => metadata with { FrameCount = 0 });
    }

    [TestMethod]
    public void DecodedFrame_Rejects_Null_Image()
    {
        ArgumentNullException? ex = null;
        try
        {
            _ = new DecodedFrame(null!, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentNullException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public void Constructor_Copies_Frame_List()
    {
        var metadata = new ImageMetadata("test.png", 10, 20, 1, "test");
        var frames = new List<DecodedFrame>
        {
            CreateFrame(10, 20)
        };
        using var image = new DecodedImage(metadata, frames, isFullResolution: true);
        using var extraFrame = CreateFrame(5, 5);

        frames.Add(extraFrame);

        Assert.AreEqual(1, image.FrameCount);
        Assert.AreEqual(10 * 20 * 4L, image.EstimatedBytes);
    }

    [TestMethod]
    public void Constructor_Normalizes_Metadata_FrameCount_To_Decoded_Frame_Count()
    {
        var metadata = new ImageMetadata("test.gif", 10, 20, 99, "test");
        using var first = CreateFrame(10, 20);
        using var second = CreateFrame(10, 20);
        using var image = new DecodedImage(metadata, [first, second], isFullResolution: true);

        Assert.AreEqual(2, image.FrameCount);
        Assert.AreEqual(2, image.Metadata.FrameCount);
    }

    [TestMethod]
    public void CreateImageOrDisposeFrames_Disposes_Frames_When_Handoff_Fails()
    {
        var frame = CreateFrame(10, 20);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => DecodedFrameOwnership.CreateImageOrDisposeFrames(null!, [frame], isFullResolution: true));

        Assert.AreEqual(IntPtr.Zero, frame.Image.Handle);
    }

    [TestMethod]
    public void DisposeAll_Disposes_All_Frames()
    {
        var first = CreateFrame(10, 20);
        var second = CreateFrame(5, 5);

        DecodedFrameOwnership.DisposeAll([first, second]);

        Assert.AreEqual(IntPtr.Zero, first.Image.Handle);
        Assert.AreEqual(IntPtr.Zero, second.Image.Handle);
    }

    [TestMethod]
    public void CreateImageOrDisposeFrames_Preserves_Null_Frame_List_Exception()
    {
        var metadata = new ImageMetadata("test.png", 10, 20, 1, "test");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => DecodedFrameOwnership.CreateImageOrDisposeFrames(metadata, null, isFullResolution: true));
    }

    [TestMethod]
    public void DisposeAll_Ignores_Null_Frame_List()
    {
        DecodedFrameOwnership.DisposeAll(null);
    }

    private static DecodedImage CreateDecodedImage(int width, int height)
    {
        var frame = CreateFrame(width, height);
        var metadata = new ImageMetadata("test.png", width, height, 1, "test");
        return new DecodedImage(metadata, [frame], isFullResolution: true);
    }

    private static DecodedFrame CreateFrame(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        return new DecodedFrame(surface.Snapshot(), TimeSpan.FromMilliseconds(100));
    }
}
