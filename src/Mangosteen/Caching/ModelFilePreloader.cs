using System.Buffers;
using System.IO;
using Mangosteen.Rendering.Advanced;

namespace Mangosteen.Caching;

internal sealed class ModelFilePreloader
{
    internal const int MaximumModels = 2;
    internal const long MaximumFileBytes = 64L * 1024 * 1024;
    internal const long MaximumPassBytes = 128L * 1024 * 1024;
    private const int BufferSize = 64 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, WarmFile> _recent = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;

    // Native imports need a graphics context. Read ahead without holding that context,
    // leaving Windows to manage the file cache rather than retaining a second model copy.
    public async Task<long> PrefetchAsync(IReadOnlyList<string> paths, long budgetBytes, CancellationToken token)
    {
        var limit = Math.Clamp(budgetBytes, 0, MaximumPassBytes);
        var generation = Volatile.Read(ref _generation);
        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in paths)
        {
            token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _generation) || total >= limit) break;
            if (!ModelFileExtensions.IsSupported(candidate)) continue;
            try
            {
                var path = Path.GetFullPath(candidate);
                if (path.StartsWith(@"\\", StringComparison.Ordinal) || !seen.Add(path)) continue;
                if (seen.Count > MaximumModels) break;
                if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) continue;
                var before = new FileInfo(path);
                var length = before.Length;
                var modified = before.LastWriteTimeUtc;
                var desired = Math.Min(Math.Min(length, MaximumFileBytes), limit - total);
                lock (_gate)
                {
                    if (_recent.TryGetValue(path, out var warm) && warm.Length == length && warm.Modified == modified &&
                        warm.Bytes >= desired && DateTime.UtcNow - warm.Completed < TimeSpan.FromSeconds(30)) continue;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.Asynchronous);
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                long read = 0;
                try
                {
                    while (read < desired)
                    {
                        token.ThrowIfCancellationRequested();
                        if (generation != Volatile.Read(ref _generation)) return total;
                        var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferSize, desired - read)), token).ConfigureAwait(false);
                        if (count == 0) break;
                        read += count;
                        total += count;
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
                token.ThrowIfCancellationRequested();
                var after = new FileInfo(path);
                lock (_gate)
                {
                    if (generation == _generation && after.Length == length && after.LastWriteTimeUtc == modified && read == desired)
                    {
                        if (_recent.Count >= 16) _recent.Remove(_recent.MinBy(pair => pair.Value.Completed).Key);
                        _recent[path] = new(length, modified, read, DateTime.UtcNow);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        return total;
    }

    public void Clear()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            _recent.Clear();
        }
    }

    private readonly record struct WarmFile(long Length, DateTime Modified, long Bytes, DateTime Completed);
}
