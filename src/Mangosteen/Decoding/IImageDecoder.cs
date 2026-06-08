namespace Mangosteen.Decoding;

public interface IImageDecoder
{
    string Name { get; }

    int Priority { get; }

    IReadOnlyCollection<string> SupportedExtensions { get; }

    bool CanDecode(string path);

    Task<ImageMetadata> LoadMetadataAsync(string path, CancellationToken token);

    Task<DecodedImage> DecodeAsync(ImageDecodeRequest request, CancellationToken token);
}
