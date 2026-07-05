using Mangosteen.Decoding;
using SkiaSharp;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class DecoderRegistryTests
{
    [TestMethod]
    public void SelectDecoder_Uses_Highest_Priority_Decoder()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("low", 1),
            new FakeDecoder("high", 10)
        ]);

        var decoder = registry.SelectDecoder("sample.fake");

        Assert.AreEqual("high", decoder.Name);
    }

    [TestMethod]
    public void GetDecoderPlan_Prefers_SkiaSharp_For_Common_Raster_Startup()
    {
        using var registry = CreateDefaultPlanRegistry();

        var plan = registry.GetDecoderPlan("sample.jpg");

        Assert.IsInstanceOfType<SkiaImageDecoder>(plan[0]);
        Assert.IsLessThan(IndexOf<VipsImageDecoder>(plan), IndexOf<SkiaImageDecoder>(plan));
    }

    [TestMethod]
    public void GetDecoderPlan_Prefers_Wic_For_Bmp()
    {
        using var registry = CreateDefaultPlanRegistry();

        var plan = registry.GetDecoderPlan("sample.bmp");

        Assert.IsInstanceOfType<WicImageDecoder>(plan[0]);
        Assert.IsLessThan(IndexOf<SkiaImageDecoder>(plan), IndexOf<WicImageDecoder>(plan));
        Assert.IsLessThan(IndexOf<VipsImageDecoder>(plan), IndexOf<WicImageDecoder>(plan));
    }

    [TestMethod]
    public void GetDecoderPlan_Prefers_Embedded_Raw_Preview_For_Raw_Preview()
    {
        using var registry = CreateDefaultPlanRegistry();

        var plan = registry.GetDecoderPlan(
            "sample.dng",
            new ImageDecodeRequest("sample.dng", FullResolution: false));

        Assert.IsInstanceOfType<WicRawPreviewImageDecoder>(plan[0]);
        Assert.IsLessThan(IndexOf<VipsImageDecoder>(plan), IndexOf<WicRawPreviewImageDecoder>(plan));
    }

    [TestMethod]
    public void GetDecoderPlan_Excludes_Embedded_Raw_Preview_For_Full_Raw_Decode()
    {
        using var registry = CreateDefaultPlanRegistry();

        var plan = registry.GetDecoderPlan(
            "sample.dng",
            new ImageDecodeRequest("sample.dng", FullResolution: true));

        Assert.IsFalse(plan.Any(static decoder => decoder is WicRawPreviewImageDecoder));
        Assert.IsInstanceOfType<VipsImageDecoder>(plan[0]);
    }

    [TestMethod]
    public async Task LoadMetadataAsync_Falls_Back_When_Higher_Priority_Decoder_Fails()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing", 10, failMetadata: true),
            new FakeDecoder("working", 1)
        ]);

        var metadata = await registry.LoadMetadataAsync("sample.fake", CancellationToken.None);

        Assert.AreEqual("working", metadata.DecoderName);
    }

    [TestMethod]
    public async Task LoadMetadataAsync_With_Filter_Falls_Back_Within_Allowed_Decoders()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing allowed", 30, failMetadata: true),
            new FakeDecoder("working allowed", 20),
            new FakeDecoder("working excluded", 10)
        ]);

        var metadata = await registry.LoadMetadataAsync(
            "sample.fake",
            CancellationToken.None,
            decoder => decoder.Name.EndsWith("allowed", StringComparison.Ordinal));

        Assert.AreEqual("working allowed", metadata.DecoderName);
    }

    [TestMethod]
    public async Task LoadMetadataAsync_With_Filter_Does_Not_Use_Excluded_Fallback()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing allowed", 30, failMetadata: true),
            new FakeDecoder("working excluded", 20)
        ]);

        var ex = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => registry.LoadMetadataAsync(
                "sample.fake",
                CancellationToken.None,
                decoder => decoder.Name.EndsWith("allowed", StringComparison.Ordinal)));

        StringAssert.Contains(ex.Message, "failing allowed");
        Assert.IsFalse(ex.Message.Contains("working excluded", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DecodeAsync_Falls_Back_When_Higher_Priority_Decoder_Fails()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing", 10, failDecode: true),
            new FakeDecoder("working", 1, failDecode: false)
        ]);

        using var image = await registry.DecodeAsync(new ImageDecodeRequest("sample.fake"), CancellationToken.None);

        Assert.AreEqual("working", image.Metadata.DecoderName);
    }

    [TestMethod]
    public async Task DecodeAsync_With_Filter_Falls_Back_Within_Allowed_Decoders()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing allowed", 30, failDecode: true),
            new FakeDecoder("working allowed", 20, failDecode: false),
            new FakeDecoder("working excluded", 10, failDecode: false)
        ]);

        using var image = await registry.DecodeAsync(
            new ImageDecodeRequest("sample.fake"),
            CancellationToken.None,
            decoder => decoder.Name.EndsWith("allowed", StringComparison.Ordinal));

        Assert.AreEqual("working allowed", image.Metadata.DecoderName);
    }

    [TestMethod]
    public async Task DecodeAsync_With_Filter_Does_Not_Use_Excluded_Fallback()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing allowed", 30, failDecode: true),
            new FakeDecoder("working excluded", 20, failDecode: false)
        ]);

        var ex = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => registry.DecodeAsync(
                new ImageDecodeRequest("sample.fake"),
                CancellationToken.None,
                decoder => decoder.Name.EndsWith("allowed", StringComparison.Ordinal)));

        StringAssert.Contains(ex.Message, "failing allowed");
        Assert.IsFalse(ex.Message.Contains("working excluded", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HasCandidate_Respects_Decoder_Filter()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("included", 20),
            new FakeDecoder("excluded", 10)
        ]);

        Assert.IsTrue(registry.HasCandidate("sample.fake", decoder => decoder.Name == "included"));
        Assert.IsFalse(registry.HasCandidate("sample.fake", decoder => decoder.Name == "missing"));
    }

    [TestMethod]
    public async Task DecodeAsync_Includes_Failing_Decoder_Names_In_Error()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("primary", 10, failDecode: true),
            new FakeDecoder("fallback", 1, failDecode: true)
        ]);

        InvalidDataException? ex = null;
        try
        {
            await registry.DecodeAsync(new ImageDecodeRequest("sample.fake"), CancellationToken.None);
        }
        catch (InvalidDataException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "primary: primary");
        StringAssert.Contains(ex.Message, "fallback: fallback");
    }

    [TestMethod]
    public async Task DecodeAsync_Propagates_Cancellation_Without_Fallback()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("canceling", 10, cancelDecode: true),
            new FakeDecoder("working", 1, failDecode: false)
        ]);

        OperationCanceledException? ex = null;
        try
        {
            await registry.DecodeAsync(new ImageDecodeRequest("sample.fake"), CancellationToken.None);
        }
        catch (OperationCanceledException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
    }

    [TestMethod]
    public async Task DecodeAsync_Treats_Failure_After_Token_Cancellation_As_Canceled()
    {
        using var cts = new CancellationTokenSource();
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("failing", 10, failDecode: true, beforeDecodeFailure: cts.Cancel)
        ]);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => registry.DecodeAsync(new ImageDecodeRequest("sample.fake"), cts.Token));
    }

    [TestMethod]
    public async Task DecodeAsync_Does_Not_Fall_Back_After_OutOfMemory()
    {
        var registry = new DecoderRegistry(
        [
            new FakeDecoder("memory exhausted", 10, throwOutOfMemory: true),
            new FakeDecoder("working", 1, failDecode: false)
        ]);

        await Assert.ThrowsExactlyAsync<OutOfMemoryException>(
            () => registry.DecodeAsync(new ImageDecodeRequest("sample.fake"), CancellationToken.None));
    }

    [TestMethod]
    public void Constructor_Rejects_Empty_Decoder_List()
    {
        ArgumentException? ex = null;
        try
        {
            _ = new DecoderRegistry([]);
        }
        catch (ArgumentException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "At least one image decoder is required.");
    }

    [TestMethod]
    public void Constructor_Rejects_Null_Decoder_Entries()
    {
        ArgumentException? ex = null;
        try
        {
            _ = new DecoderRegistry([new FakeDecoder("valid", 1), null!]);
        }
        catch (ArgumentException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        StringAssert.Contains(ex.Message, "cannot contain null entries");
    }

    [TestMethod]
    public void Constructor_Normalizes_Supported_Extensions()
    {
        var registry = new DecoderRegistry([new FakeDecoder("valid", 1, supportedExtensions: ["fake", ".PNG", ""])]);
        var extensions = registry.SupportedExtensions.ToList();

        CollectionAssert.Contains(extensions, ".fake");
        CollectionAssert.Contains(extensions, ".png");
        CollectionAssert.DoesNotContain(extensions, "fake");
        CollectionAssert.DoesNotContain(extensions, string.Empty);
    }

    [TestMethod]
    public async Task Public_Methods_Reject_Invalid_Inputs()
    {
        using var registry = new DecoderRegistry([new FakeDecoder("valid", 1, failDecode: false)]);

        Assert.ThrowsExactly<ArgumentException>(() => registry.SelectDecoder(""));

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => registry.LoadMetadataAsync(" ", CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => registry.DecodeAsync(null!, CancellationToken.None));
    }

    [TestMethod]
    public async Task Public_Methods_Reject_Use_After_Dispose()
    {
        var registry = new DecoderRegistry([new FakeDecoder("valid", 1, failDecode: false)]);
        registry.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => registry.SelectDecoder("sample.fake"));

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => registry.LoadMetadataAsync("sample.fake", CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => registry.DecodeAsync(new ImageDecodeRequest("sample.fake"), CancellationToken.None));
    }

    private static DecoderRegistry CreateDefaultPlanRegistry()
    {
        return new DecoderRegistry(
        [
            new WicRawPreviewImageDecoder(),
            new VipsImageDecoder(),
            new WicImageDecoder(),
            new SkiaImageDecoder(),
            new MagickImageDecoder()
        ]);
    }

    private static int IndexOf<TDecoder>(IReadOnlyList<IImageDecoder> decoders)
        where TDecoder : IImageDecoder
    {
        for (var i = 0; i < decoders.Count; i++)
        {
            if (decoders[i] is TDecoder)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class FakeDecoder(
        string name,
        int priority,
        bool failMetadata = false,
        bool failDecode = true,
        bool cancelDecode = false,
        IReadOnlyCollection<string>? supportedExtensions = null,
        Action? beforeDecodeFailure = null,
        bool throwOutOfMemory = false) : IImageDecoder
    {
        public string Name => name;

        public int Priority => priority;

        public IReadOnlyCollection<string> SupportedExtensions { get; } = supportedExtensions ?? [".fake"];

        public bool CanDecode(string path) => true;

        public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
        {
            if (failMetadata)
            {
                throw new InvalidDataException(Name);
            }

            return Task.FromResult(new ImageMetadata(path, 1, 1, 1, Name));
        }

        public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
        {
            if (cancelDecode)
            {
                throw new OperationCanceledException();
            }

            if (throwOutOfMemory)
            {
                throw new OutOfMemoryException(Name);
            }

            if (failDecode)
            {
                beforeDecodeFailure?.Invoke();
                throw new InvalidDataException(Name);
            }

            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Black);
            var frame = new DecodedFrame(surface.Snapshot(), TimeSpan.FromMilliseconds(100));
            var metadata = new ImageMetadata(request.Path, 1, 1, 1, Name);
            return Task.FromResult(new DecodedImage(metadata, [frame], isFullResolution: true));
        }
    }
}
