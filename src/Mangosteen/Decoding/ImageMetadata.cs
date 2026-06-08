namespace Mangosteen.Decoding;

public sealed record ImageMetadata
{
    private string _path = string.Empty;
    private int _width;
    private int _height;
    private int _frameCount;
    private string _decoderName = string.Empty;

    public ImageMetadata(string path, int width, int height, int frameCount, string decoderName)
    {
        Path = path;
        Width = width;
        Height = height;
        FrameCount = frameCount;
        DecoderName = decoderName;
    }

    public string Path
    {
        get => _path;
        init => _path = ValidateRequired(value, nameof(Path));
    }

    public int Width
    {
        get => _width;
        init => _width = ValidatePositive(value, nameof(Width));
    }

    public int Height
    {
        get => _height;
        init => _height = ValidatePositive(value, nameof(Height));
    }

    public int FrameCount
    {
        get => _frameCount;
        init => _frameCount = ValidatePositive(value, nameof(FrameCount));
    }

    public string DecoderName
    {
        get => _decoderName;
        init => _decoderName = ValidateRequired(value, nameof(DecoderName));
    }

    private static string ValidateRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value;
    }

    private static int ValidatePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be positive.");
        }

        return value;
    }
}
