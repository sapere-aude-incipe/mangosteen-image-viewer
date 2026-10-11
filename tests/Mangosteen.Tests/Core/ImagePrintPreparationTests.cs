using Mangosteen.Decoding;
using Mangosteen.Printing;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ImagePrintPreparationTests
{
    [TestMethod]
    public Task Preview_Print_Uses_Full_Decode_And_Survives_Navigation() => StaTest.RunAsync(async () =>
    {
        using var preview = Image(20, 15, 100, 75, false);
        var completion = new TaskCompletionSource<DecodedImage>();
        var task = ImagePrintPreparation.PrepareAsync(preview, 0, 1, (request, token) =>
        {
            Assert.IsTrue(request.FullResolution);
            Assert.AreEqual("original.webp", request.Path);
            Assert.AreEqual(1024L * 1024, request.MaxDecodedBytes);
            return completion.Task;
        }, 1024 * 1024, CancellationToken.None);
        preview.Dispose();
        completion.SetResult(Image(100, 75, 100, 75, true));
        var snapshot = await task;
        Assert.AreEqual(100, snapshot.Source.PixelWidth);
        Assert.AreEqual(75, snapshot.Source.PixelHeight);
        Assert.AreEqual(1, snapshot.QuarterTurns);
        Assert.AreEqual("original.webp", snapshot.JobName);
        Assert.IsTrue(snapshot.Source.IsFrozen);
    });

    [TestMethod]
    public Task Full_Image_Does_Not_Decode_Again() => StaTest.RunAsync(async () =>
    {
        using var full = Image(100, 75, 100, 75, true);
        var snapshot = await ImagePrintPreparation.PrepareAsync(full, 0, 0,
            (_, _) => throw new AssertFailedException("Already full resolution"), 1024 * 1024, CancellationToken.None);
        Assert.AreEqual(100, snapshot.Source.PixelWidth);
    });

    [TestMethod]
    public Task Cancellation_Does_Not_Return_A_Print_Snapshot() => StaTest.RunAsync(async () =>
    {
        using var preview = Image(20, 15, 100, 75, false);
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<DecodedImage>();
        var task = ImagePrintPreparation.PrepareAsync(preview, 0, 0, (_, _) => completion.Task, 1024 * 1024, cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(Image(100, 75, 100, 75, true));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await task);
    });

    [TestMethod]
    public Task A_Decoder_That_Returns_A_Preview_Is_Not_Silently_Printed() => StaTest.RunAsync(async () =>
    {
        using var preview = Image(20, 15, 100, 75, false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await ImagePrintPreparation.PrepareAsync(preview, 0, 0,
                (_, _) => Task.FromResult(Image(20, 15, 100, 75, false)), 1024 * 1024, CancellationToken.None));
    });

    private static DecodedImage Image(int width, int height, int originalWidth, int originalHeight, bool full)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        return new DecodedImage(new ImageMetadata("original.webp", originalWidth, originalHeight, 1, "test"),
            [new DecodedFrame(SKImage.FromBitmap(bitmap), TimeSpan.Zero)], full);
    }
}
