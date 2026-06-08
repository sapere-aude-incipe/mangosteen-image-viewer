using Mangosteen.Caching;
using Mangosteen.Decoding;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class PreloadDecoderPolicyTests
{
    [TestMethod]
    public void IsFullPreloadDecoder_Allows_Fast_Decoders()
    {
        Assert.IsTrue(PreloadDecoderPolicy.IsFullPreloadDecoder(new VipsImageDecoder()));
        Assert.IsTrue(PreloadDecoderPolicy.IsFullPreloadDecoder(new WicImageDecoder()));
        Assert.IsTrue(PreloadDecoderPolicy.IsFullPreloadDecoder(new SkiaImageDecoder()));
    }

    [TestMethod]
    public void IsPreloadDecoder_Allows_Fast_Decoders()
    {
        Assert.IsTrue(PreloadDecoderPolicy.IsPreloadDecoder(new WicRawPreviewImageDecoder()));
        Assert.IsTrue(PreloadDecoderPolicy.IsPreloadDecoder(new VipsImageDecoder()));
        Assert.IsTrue(PreloadDecoderPolicy.IsPreloadDecoder(new WicImageDecoder()));
        Assert.IsTrue(PreloadDecoderPolicy.IsPreloadDecoder(new SkiaImageDecoder()));
    }

    [TestMethod]
    public void IsFullPreloadDecoder_Rejects_Preview_Only_Raw_Decoder()
    {
        Assert.IsFalse(PreloadDecoderPolicy.IsFullPreloadDecoder(new WicRawPreviewImageDecoder()));
    }

    [TestMethod]
    public void IsFullPreloadDecoder_Rejects_Magick_Fallback()
    {
        Assert.IsFalse(PreloadDecoderPolicy.IsFullPreloadDecoder(new MagickImageDecoder()));
    }

    [TestMethod]
    public void IsPreloadDecoder_Rejects_Magick_Fallback()
    {
        Assert.IsFalse(PreloadDecoderPolicy.IsPreloadDecoder(new MagickImageDecoder()));
    }

    [TestMethod]
    public void HasFullPreloadCandidate_Uses_Filtered_Decoder_Set()
    {
        var path = CreateTempFilePath(".png");
        try
        {
            File.WriteAllBytes(path, []);
            using var registry = new DecoderRegistry([new SkiaImageDecoder()]);

            Assert.IsTrue(PreloadDecoderPolicy.HasFullPreloadCandidate(
                registry,
                path,
                isClosing: false,
                CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void HasFullPreloadCandidate_Does_Not_Use_Magick_Only_Candidate()
    {
        var path = CreateTempFilePath(".unknownimage");
        try
        {
            File.WriteAllBytes(path, []);
            using var registry = new DecoderRegistry([new MagickImageDecoder()]);

            Assert.IsFalse(PreloadDecoderPolicy.HasFullPreloadCandidate(
                registry,
                path,
                isClosing: false,
                CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void HasPreloadCandidate_Does_Not_Use_Magick_Only_Candidate()
    {
        var path = CreateTempFilePath(".unknownimage");
        try
        {
            File.WriteAllBytes(path, []);
            using var registry = new DecoderRegistry([new MagickImageDecoder()]);

            Assert.IsFalse(PreloadDecoderPolicy.HasPreloadCandidate(
                registry,
                path,
                isClosing: false,
                CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void HasFullPreloadCandidate_Returns_False_For_Disposed_Registry_During_Close()
    {
        using var registry = new DecoderRegistry([new SkiaImageDecoder()]);
        registry.Dispose();

        Assert.IsFalse(PreloadDecoderPolicy.HasFullPreloadCandidate(
            registry,
            "photo.png",
            isClosing: true,
            CancellationToken.None));
    }

    [TestMethod]
    public void HasFullPreloadCandidate_Does_Not_Hide_Unexpected_Disposed_Registry()
    {
        using var registry = new DecoderRegistry([new SkiaImageDecoder()]);
        registry.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            PreloadDecoderPolicy.HasFullPreloadCandidate(
                registry,
                "photo.png",
                isClosing: false,
            CancellationToken.None));
    }

    private static string CreateTempFilePath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
    }
}
