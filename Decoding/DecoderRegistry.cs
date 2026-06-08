using System.IO;

namespace ClassicPhotoViewer.Decoding;

public sealed class DecoderRegistry : IDisposable
{
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

        foreach (var decoder in _decoders)
        {
            if (decoderFilter is not null && !decoderFilter(decoder))
            {
                continue;
            }

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
            decoderFilter);
    }

    public bool HasCandidate(string path, Func<IImageDecoder, bool>? decoderFilter = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _decoders.Any(decoder =>
            (decoderFilter is null || decoderFilter(decoder)) &&
            decoder.CanDecode(path));
    }

    private async Task<T> TryDecodersAsync<T>(
        string path,
        Func<IImageDecoder, Task<T>> operation,
        CancellationToken token,
        Func<IImageDecoder, bool>? decoderFilter = null)
    {
        var failures = new List<DecoderFailure>();
        var sawCandidate = false;

        foreach (var decoder in _decoders)
        {
            token.ThrowIfCancellationRequested();
            if (decoderFilter is not null && !decoderFilter(decoder))
            {
                continue;
            }

            if (!decoder.CanDecode(path))
            {
                continue;
            }

            sawCandidate = true;
            try
            {
                return await operation(decoder);
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
