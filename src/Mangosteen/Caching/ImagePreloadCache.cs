using Mangosteen.Decoding;

namespace Mangosteen.Caching;

public sealed class ImagePreloadCache : IDisposable
{
    private readonly Dictionary<string, CacheEntry> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private long _budgetBytes = 2L * ImageMemoryEstimator.Gigabyte;
    private long _usedBytes;
    private long _sequence;
    private bool _disposed;

    public long BudgetBytes
    {
        get
        {
            lock (_gate)
            {
                return _budgetBytes;
            }
        }
        set
        {
            List<KeyValuePair<string, CacheEntry>> evicted;
            lock (_gate)
            {
                if (_disposed) return;

                _budgetBytes = Math.Max(0, value);
                evicted = TrimToBudget();
            }

            DisposeEntries(evicted);
        }
    }

    public long UsedBytes
    {
        get
        {
            lock (_gate)
            {
                return _usedBytes;
            }
        }
    }

    public bool Contains(string path)
    {
        ValidatePath(path);

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            return _images.ContainsKey(path);
        }
    }

    public bool ContainsFullResolution(string path)
    {
        ValidatePath(path);

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            return _images.TryGetValue(path, out var entry) && entry.Image.IsFullResolution;
        }
    }

    public bool CanFit(long bytes)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            return FitsWithinBudget(_usedBytes, Math.Max(0, bytes), _budgetBytes);
        }
    }

    public bool CanStore(long bytes)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            return Math.Max(0, bytes) <= _budgetBytes;
        }
    }

    public bool CanStore(string path, long bytes, long evictionPriority)
    {
        ValidatePath(path);

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var incomingBytes = Math.Max(0, bytes);
            if (incomingBytes > _budgetBytes)
            {
                return false;
            }

            var usedBytes = _usedBytes;
            if (_images.TryGetValue(path, out var current))
            {
                usedBytes = Math.Max(0, usedBytes - current.Bytes);
            }

            if (FitsWithinBudget(usedBytes, incomingBytes, _budgetBytes))
            {
                return true;
            }

            foreach (var entry in GetEvictionCandidates(path, evictionPriority))
            {
                usedBytes = Math.Max(0, usedBytes - entry.Bytes);
                if (FitsWithinBudget(usedBytes, incomingBytes, _budgetBytes))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool CanFit(string path, long bytes)
    {
        ValidatePath(path);

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var currentBytes = _images.TryGetValue(path, out var entry) ? entry.Bytes : 0;
            var usedWithoutCurrent = Math.Max(0, _usedBytes - currentBytes);
            return FitsWithinBudget(usedWithoutCurrent, Math.Max(0, bytes), _budgetBytes);
        }
    }

    public bool TryTake(string path, out DecodedImage? image)
    {
        ValidatePath(path);

        lock (_gate)
        {
            if (_disposed)
            {
                image = null;
                return false;
            }

            if (!_images.Remove(path, out var entry))
            {
                image = null;
                return false;
            }

            _usedBytes -= entry.Bytes;
            image = entry.Image;
            return true;
        }
    }

    public bool Store(string path, DecodedImage image, long evictionPriority = 0)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(image);

        var bytes = Math.Max(0, image.EstimatedBytes);
        var disposeIncoming = false;
        CacheEntry? oldToDispose = null;
        List<KeyValuePair<string, CacheEntry>> evictedToDispose = [];
        var stored = false;

        lock (_gate)
        {
            if (_disposed)
            {
                disposeIncoming = true;
            }
            else if (_images.TryGetValue(path, out var existing) &&
                existing.Image.IsFullResolution &&
                !image.IsFullResolution)
            {
                disposeIncoming = true;
            }
            else
            {
                if (bytes > _budgetBytes)
                {
                    disposeIncoming = true;
                }
                else
                {
                    CacheEntry? old = null;
                    if (_images.Remove(path, out var removed) && removed is not null)
                    {
                        old = removed;
                        _usedBytes -= removed.Bytes;
                    }

                    var evicted = TrimToBudget(bytes, evictionPriority);

                    if (!FitsWithinBudget(_usedBytes, bytes, _budgetBytes))
                    {
                        RestoreEntry(path, old);
                        RestoreEntries(evicted);
                        disposeIncoming = true;
                    }
                    else
                    {
                        oldToDispose = old;
                        evictedToDispose = evicted;

                        _images[path] = new CacheEntry(image, bytes, evictionPriority, ++_sequence);
                        _usedBytes += bytes;
                        stored = true;
                    }
                }
            }
        }

        if (disposeIncoming)
        {
            image.Dispose();
        }

        oldToDispose?.Image.Dispose();
        DisposeEntries(evictedToDispose);
        return stored;
    }

    public void Clear()
    {
        List<CacheEntry> entries;
        lock (_gate)
        {
            entries = _images.Values.ToList();
            _images.Clear();
            _usedBytes = 0;
        }

        DisposeEntries(entries);
    }

    public void Dispose()
    {
        List<CacheEntry> entries;
        lock (_gate)
        {
            if (_disposed) return;

            entries = _images.Values.ToList();
            _images.Clear();
            _usedBytes = 0;
            _disposed = true;
        }

        DisposeEntries(entries);
    }

    private List<KeyValuePair<string, CacheEntry>> TrimToBudget(long incomingBytes = 0, long? incomingPriority = null)
    {
        var evicted = new List<KeyValuePair<string, CacheEntry>>();
        while (_images.Count > 0 && !FitsWithinBudget(_usedBytes, Math.Max(0, incomingBytes), _budgetBytes))
        {
            if (!TryFindEvictionCandidate(incomingPriority, out var key, out var entry))
            {
                return evicted;
            }

            _images.Remove(key);
            _usedBytes -= entry.Bytes;
            evicted.Add(new KeyValuePair<string, CacheEntry>(key, entry));
        }

        return evicted;
    }

    private bool TryFindEvictionCandidate(long? incomingPriority, out string key, out CacheEntry entry)
    {
        key = string.Empty;
        entry = null!;
        var found = false;

        foreach (var pair in _images)
        {
            var candidate = pair.Value;
            if (incomingPriority is long priority && candidate.EvictionPriority < priority)
            {
                continue;
            }

            if (!found ||
                candidate.EvictionPriority > entry.EvictionPriority ||
                candidate.EvictionPriority == entry.EvictionPriority && candidate.LastAccess < entry.LastAccess)
            {
                key = pair.Key;
                entry = candidate;
                found = true;
            }
        }

        return found;
    }

    private IEnumerable<CacheEntry> GetEvictionCandidates(string incomingPath, long incomingPriority)
    {
        return _images
            .Where(pair => !pair.Key.Equals(incomingPath, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Value)
            .Where(entry => entry.EvictionPriority >= incomingPriority)
            .OrderByDescending(static entry => entry.EvictionPriority)
            .ThenBy(static entry => entry.LastAccess);
    }

    private void RestoreEntry(string path, CacheEntry? entry)
    {
        if (entry is null) return;

        _images[path] = entry;
        _usedBytes += entry.Bytes;
    }

    private void RestoreEntries(IEnumerable<KeyValuePair<string, CacheEntry>> entries)
    {
        foreach (var pair in entries)
        {
            _images[pair.Key] = pair.Value;
            _usedBytes += pair.Value.Bytes;
        }
    }

    private static void DisposeEntries(IEnumerable<KeyValuePair<string, CacheEntry>> entries)
    {
        foreach (var pair in entries)
        {
            pair.Value.Image.Dispose();
        }
    }

    private static void DisposeEntries(IEnumerable<CacheEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Image.Dispose();
        }
    }

    private static bool FitsWithinBudget(long usedBytes, long incomingBytes, long budgetBytes)
    {
        if (usedBytes < 0 || incomingBytes < 0 || budgetBytes < 0)
        {
            return false;
        }

        return usedBytes <= budgetBytes && incomingBytes <= budgetBytes - usedBytes;
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
    }

    private sealed record CacheEntry(DecodedImage Image, long Bytes, long EvictionPriority, long LastAccess);
}
