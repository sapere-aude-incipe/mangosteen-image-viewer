using System.IO.Compression;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mangosteen.Rendering.Advanced;

internal sealed class PersistentTileCache
{
    private const int FormatVersion = 1;
    private const int MaximumTilePayloadBytes = 128 * 1024 * 1024;
    private const long TrimIntervalMilliseconds = 60_000;
    private static readonly byte[] Magic = "MGTILE01"u8.ToArray();
    private readonly string _root;
    private readonly long _maximumBytes;
    private readonly SemaphoreSlim _trimGate = new(1, 1);
    private long _nextTrimTick;
    private int _writesDisabled;

    public PersistentTileCache(string? root = null, long maximumBytes = 4L * 1024 * 1024 * 1024)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _root = Path.GetFullPath(root ?? GetDefaultRoot());
        _maximumBytes = maximumBytes;
    }

    public string CreateSourceKey(string path, string decoderVersion)
    {
        var info = new FileInfo(path);
        var identity = string.Join("\n", Path.GetFullPath(path), info.Length, info.LastWriteTimeUtc.Ticks, decoderVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public async Task<ImageTileData?> TryReadAsync(string sourceKey, ImageTileKey key, CancellationToken token)
    {
        var path = GetTilePath(sourceKey, key);
        try
        {
            if (!File.Exists(path)) return null;

            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) || reader.ReadInt32() != FormatVersion)
            {
                return null;
            }

            var storedKey = new ImageTileKey(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var sourceWidth = reader.ReadInt32();
            var sourceHeight = reader.ReadInt32();
            var pixelFormat = (ImageTilePixelFormat)reader.ReadByte();
            var payloadLength = reader.ReadInt32();
            if (storedKey != key || width <= 0 || height <= 0 || sourceWidth <= 0 || sourceHeight <= 0 ||
                !Enum.IsDefined(pixelFormat) || payloadLength <= 0 || payloadLength > MaximumTilePayloadBytes)
            {
                return null;
            }

            var bytesPerChannel = pixelFormat == ImageTilePixelFormat.Rgba16 ? 2 : 1;
            if (width > 2048 || height > 2048 ||
                payloadLength != checked((long)width * height * 4 * bytesPerChannel))
            {
                return null;
            }

            await using var deflate = new DeflateStream(file, CompressionMode.Decompress, leaveOpen: false);
            var pixels = new byte[payloadLength];
            await deflate.ReadExactlyAsync(pixels, token);
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return new ImageTileData(key, width, height, sourceWidth, sourceHeight, pixelFormat, pixels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    public async Task WriteAsync(string sourceKey, ImageTileData tile, CancellationToken token)
    {
        if (!tile.HasExpectedPixelLength() || tile.Pixels.Length > MaximumTilePayloadBytes)
        {
            throw new ArgumentException("The tile pixel payload does not match its dimensions and format.", nameof(tile));
        }

        var path = GetTilePath(sourceKey, tile.Key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                using var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(tile.Key.Level);
                writer.Write(tile.Key.X);
                writer.Write(tile.Key.Y);
                writer.Write(tile.Width);
                writer.Write(tile.Height);
                writer.Write(tile.SourceWidth);
                writer.Write(tile.SourceHeight);
                writer.Write((byte)tile.PixelFormat);
                writer.Write(tile.Pixels.Length);
                writer.Flush();
                await using var deflate = new DeflateStream(file, CompressionLevel.Fastest, leaveOpen: true);
                await deflate.WriteAsync(tile.Pixels, token);
            }

            File.Move(temporaryPath, path, overwrite: true);
            ScheduleTrim();
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    public async Task TryWriteAsync(string sourceKey, ImageTileData tile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _writesDisabled) != 0) return;
        try
        {
            await WriteAsync(sourceKey, tile, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache is optional; keep displaying decoded pixels when storage fails.
            Interlocked.Exchange(ref _writesDisabled, 1);
            System.Diagnostics.Trace.WriteLine($"Tile cache writes disabled: {ex.Message}");
        }
    }

    private void ScheduleTrim()
    {
        var now = Environment.TickCount64;
        var next = Volatile.Read(ref _nextTrimTick);
        if (now < next || Interlocked.CompareExchange(ref _nextTrimTick, now + TrimIntervalMilliseconds, next) != next)
        {
            return;
        }

        _ = TrimSafelyAsync();
    }

    private async Task TrimSafelyAsync()
    {
        try
        {
            await TrimAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    internal string GetTilePath(string sourceKey, ImageTileKey key)
    {
        if (sourceKey.Length != 64 || sourceKey.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The source key must be a SHA-256 hexadecimal value.", nameof(sourceKey));
        }

        return Path.Combine(_root, sourceKey[..2], sourceKey, $"l{key.Level}-x{key.X}-y{key.Y}.mgtile");
    }

    private async Task TrimAsync(CancellationToken token)
    {
        if (!await _trimGate.WaitAsync(0, token)) return;
        try
        {
            if (!Directory.Exists(_root)) return;
            var files = new DirectoryInfo(_root).EnumerateFiles("*.mgtile", SearchOption.AllDirectories).ToArray();
            var total = files.Sum(file => file.Length);
            if (total <= _maximumBytes) return;

            foreach (var file in files.OrderBy(file => file.LastAccessTimeUtc))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var fileLength = file.Length;
                    file.Delete();
                    total -= fileLength;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                if (total <= _maximumBytes * 9 / 10) break;
            }
        }
        finally
        {
            _trimGate.Release();
        }
    }

    private static string GetDefaultRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mangosteen Image Viewer",
            "Cache",
            "LargeImageTiles");
    }
}
