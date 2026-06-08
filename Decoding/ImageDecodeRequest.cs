using ClassicPhotoViewer.Core;

namespace ClassicPhotoViewer.Decoding;

public sealed record ImageDecodeRequest
{
    private string _path = string.Empty;
    private long? _maxDecodedBytes;

    public ImageDecodeRequest(
        string path,
        PixelSize? targetPreviewSize = null,
        bool FullResolution = false,
        long? MaxDecodedBytes = null)
    {
        Path = path;
        TargetPreviewSize = targetPreviewSize;
        this.FullResolution = FullResolution;
        this.MaxDecodedBytes = MaxDecodedBytes;
    }

    public string Path
    {
        get => _path;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Path is required.", nameof(Path));
            }

            _path = value;
        }
    }

    public PixelSize? TargetPreviewSize { get; init; }

    public bool FullResolution { get; init; }

    public long? MaxDecodedBytes
    {
        get => _maxDecodedBytes;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxDecodedBytes), value, "MaxDecodedBytes cannot be negative.");
            }

            _maxDecodedBytes = value;
        }
    }
}
