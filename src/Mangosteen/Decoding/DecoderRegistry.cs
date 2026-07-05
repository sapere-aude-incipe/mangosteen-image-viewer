using System.IO;

namespace Mangosteen.Decoding;

public sealed class DecoderRegistry : IDisposable
{
    private static readonly IReadOnlySet<string> SkiaPreferredExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ico", ".jfif", ".jpe", ".jpeg", ".jpg", ".png", ".webp"
        };

    private static readonly IReadOnlySet<string> WicPreferredExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp", ".cur", ".dib", ".jxr", ".tif", ".tiff", ".wdp"
        };

    private static readonly IReadOnlySet<string> VipsPreferredExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ari", ".avif", ".bay", ".cap", ".dcs", ".dds", ".drf", ".exif",
            ".exr", ".fff", ".gpr", ".hdr", ".heic", ".heif", ".iiq", ".jxl",
            ".mdc", ".pbm", ".pcx", ".pgm", ".ppm", ".psb", ".psd", ".qoi",
            ".svg", ".svgz", ".tga", ".wmf", ".xbm", ".xpm"
        };

    private readonly IImageDecoder[] _decoders;
    private bool _disposed;

    public DecoderRegistry(IEnumerable<IImageDecoder> decoders)
    {
        ArgumentNullException.ThrowIfNull(decoders);

        var decoderSnapshot = decoders.ToArray();
        if (decoderSnapshot.Length == 0)
        {
            throw new ArgumentException("At least one image decoder is required.", nameof(decoders));
        }

        if (decoderSnapshot.Any(static decoder => decoder is null))
        {
            throw new ArgumentException("Image decoder collection cannot contain null entries.", nameof(decoders));
        }

        _decoders = decoderSnapshot.OrderByDescending(static d => d.Priority).ToArray();
        SupportedExtensions = _decoders
            .SelectMany(static d => d.SupportedExtensions)
            .Select(ImageFileExtensions.NormalizeExtensionToken)
            .Where(static extension => !string.IsNullOrEmpty(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; }

    public static DecoderRegistry CreateDefault()
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

    public IImageDecoder SelectDecoder(string path)
    {
        return SelectDecoder(path, decoderFilter: null);
    }

    public IImageDecoder SelectDecoder(string path, Func<IImageDecoder, bool>? decoderFilter)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach (var decoder in GetDecoderPlan(path, request: null, decoderFilter))
        {
            if (decoder.CanDecode(path))
            {
                return decoder;
            }
        }

        throw new NotSupportedException($"No decoder can open '{path}'.");
    }

    public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token)
    {
        return LoadMetadataAsync(path, token, decoderFilter: null);
    }

    public Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token, Func<IImageDecoder, bool>? decoderFilter)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return TryDecodersAsync(
            path,
            decoder => decoder.LoadMetadataAsync(path, token),
            token,
            decoderFilter);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token)
    {
        return DecodeAsync(request, token, decoderFilter: null);
    }

    public Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token, Func<IImageDecoder, bool>? decoderFilter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        return TryDecodersAsync(
            request.Path,
            decoder => decoder.DecodeAsync(request, token),
            token,
            decoderFilter,
            request);
    }

    public bool HasCandidate(string path, Func<IImageDecoder, bool>? decoderFilter = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return GetDecoderPlan(path, request: null, decoderFilter)
            .Any(decoder => decoder.CanDecode(path));
    }

    internal IReadOnlyList<IImageDecoder> GetDecoderPlan(
        string path,
        ImageDecodeRequest? request = null,
        Func<IImageDecoder, bool>? decoderFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _decoders
            .Where(decoder => IsDecoderEligible(decoder, request) && (decoderFilter is null || decoderFilter(decoder)))
            .OrderByDescending(decoder => GetEffectivePriority(decoder, path, request))
            .ToArray();
    }

    private async Task<T> TryDecodersAsync<T>(
        string path,
        Func<IImageDecoder, Task<T>> operation,
        CancellationToken token,
        Func<IImageDecoder, bool>? decoderFilter = null,
        ImageDecodeRequest? request = null)
    {
        var failures = new List<DecoderFailure>();
        var sawCandidate = false;

        foreach (var decoder in GetDecoderPlan(path, request, decoderFilter))
        {
            token.ThrowIfCancellationRequested();
            if (!decoder.CanDecode(path))
            {
                continue;
            }

            sawCandidate = true;
            try
            {
                StartupDiagnostics.Mark("decoder.begin", $"{decoder.Name} {Path.GetFileName(path)}");
                var result = await operation(decoder);
                StartupDiagnostics.Mark("decoder.end", $"{decoder.Name} {Path.GetFileName(path)}");
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                StartupDiagnostics.Mark("decoder.failed", $"{decoder.Name} {Path.GetFileName(path)} {ex.GetType().Name}");
                failures.Add(new DecoderFailure(decoder.Name, ex));
            }
        }

        if (!sawCandidate)
        {
            throw new NotSupportedException($"No decoder can open '{path}'.");
        }

        throw new InvalidDataException(
            $"No decoder could read '{path}'. {FormatFailures(failures)}",
            new AggregateException(failures.Select(static failure => failure.Exception)));
    }

    private static string FormatFailures(IReadOnlyList<DecoderFailure> failures)
    {
        if (failures.Count == 0)
        {
            return string.Empty;
        }

        var details = failures
            .Select(static failure => $"{failure.DecoderName}: {GetUsefulMessage(failure.Exception)}")
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return "Tried " + string.Join("; ", details);
    }

    private static string GetUsefulMessage(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            exception = aggregate.InnerExceptions[0];
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
    }

    private static bool IsDecoderEligible(IImageDecoder decoder, ImageDecodeRequest? request)
    {
        return request is not { FullResolution: true } || decoder is not WicRawPreviewImageDecoder;
    }

    private static int GetEffectivePriority(IImageDecoder decoder, string path, ImageDecodeRequest? request)
    {
        var extension = ImageFileExtensions.NormalizeExtension(path);
        if (ImageFileExtensions.RawImageExtensions.Contains(extension))
        {
            return request is { FullResolution: true }
                ? GetFullRawPriority(decoder)
                : GetPreviewRawPriority(decoder);
        }

        if (SkiaPreferredExtensions.Contains(extension))
        {
            return decoder switch
            {
                SkiaImageDecoder => 50_000 + decoder.Priority,
                WicImageDecoder => 40_000 + decoder.Priority,
                VipsImageDecoder => 30_000 + decoder.Priority,
                MagickImageDecoder => 10_000 + decoder.Priority,
                _ => decoder.Priority
            };
        }

        if (WicPreferredExtensions.Contains(extension))
        {
            return decoder switch
            {
                WicImageDecoder => 50_000 + decoder.Priority,
                SkiaImageDecoder => 40_000 + decoder.Priority,
                VipsImageDecoder => 30_000 + decoder.Priority,
                MagickImageDecoder => 10_000 + decoder.Priority,
                _ => decoder.Priority
            };
        }

        if (VipsPreferredExtensions.Contains(extension))
        {
            return decoder switch
            {
                VipsImageDecoder => 50_000 + decoder.Priority,
                WicImageDecoder => 40_000 + decoder.Priority,
                SkiaImageDecoder => 30_000 + decoder.Priority,
                MagickImageDecoder => 10_000 + decoder.Priority,
                _ => decoder.Priority
            };
        }

        return decoder.Priority;
    }

    private static int GetPreviewRawPriority(IImageDecoder decoder)
    {
        return decoder switch
        {
            WicRawPreviewImageDecoder => 60_000 + decoder.Priority,
            WicImageDecoder => 50_000 + decoder.Priority,
            VipsImageDecoder => 40_000 + decoder.Priority,
            MagickImageDecoder => 10_000 + decoder.Priority,
            _ => decoder.Priority
        };
    }

    private static int GetFullRawPriority(IImageDecoder decoder)
    {
        return decoder switch
        {
            VipsImageDecoder => 50_000 + decoder.Priority,
            WicImageDecoder => 40_000 + decoder.Priority,
            MagickImageDecoder => 10_000 + decoder.Priority,
            _ => decoder.Priority
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var decoder in _decoders.OfType<IDisposable>())
        {
            decoder.Dispose();
        }
        _disposed = true;
    }

    private sealed record DecoderFailure(string DecoderName, Exception Exception);
}
