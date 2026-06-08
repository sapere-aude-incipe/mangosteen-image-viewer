using Mangosteen.Decoding;
using System.IO;

namespace Mangosteen.Navigation;

public sealed class ImageNavigator
{
    internal const int MaxScannedFolderEntries = 20_000;
    internal const int MaxImageFolderFiles = 5_000;

    private List<string> _files = [];

    public IReadOnlyList<string> Files => _files;

    public int CurrentIndex { get; private set; } = -1;

    public string? CurrentPath => HasCurrent ? _files[CurrentIndex] : null;

    public bool CanMovePrevious => _files.Count > 1 && HasCurrent;

    public bool CanMoveNext => _files.Count > 1 && HasCurrent;

    private bool HasCurrent => CurrentIndex >= 0 && CurrentIndex < _files.Count;

    public void LoadSingle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _files = [Path.GetFullPath(path)];
        CurrentIndex = 0;
    }

    public void Clear()
    {
        _files = [];
        CurrentIndex = -1;
    }

    public void LoadFolderFor(string path, IEnumerable<string> supportedExtensions)
    {
        Apply(ScanFolderFor(path, supportedExtensions, CancellationToken.None));
    }

    public void Apply(ImageFolderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Files);

        _files = snapshot.Files.Select(NormalizeSnapshotPath).ToList();
        CurrentIndex = snapshot.CurrentIndex >= 0 && snapshot.CurrentIndex < _files.Count
            ? snapshot.CurrentIndex
            : -1;
    }

    public static ImageFolderSnapshot ScanFolderFor(
        string path,
        IEnumerable<string> supportedExtensions,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(supportedExtensions);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return new ImageFolderSnapshot([fullPath], 0);
        }

        var extensions = supportedExtensions
            .Select(ImageFileExtensions.NormalizeExtensionToken)
            .Where(static extension => !string.IsNullOrEmpty(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        try
        {
            var scannedEntries = 0;
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                scannedEntries++;
                if (scannedEntries > MaxScannedFolderEntries)
                {
                    break;
                }

                if (extensions.Contains(ImageFileExtensions.NormalizeExtension(file)))
                {
                    files.Add(Path.GetFullPath(file));
                    if (files.Count >= MaxImageFolderFiles)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new ImageFolderSnapshot([fullPath], 0);
        }

        files.Sort((left, right) => NaturalStringComparer.Instance.Compare(Path.GetFileName(left), Path.GetFileName(right)));

        if (!files.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            files.Add(fullPath);
            files.Sort((left, right) => NaturalStringComparer.Instance.Compare(Path.GetFileName(left), Path.GetFileName(right)));
        }

        var currentIndex = files.FindIndex(file => file.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        return new ImageFolderSnapshot(files, currentIndex);
    }

    private static string NormalizeSnapshotPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public string? MovePrevious()
    {
        if (_files.Count == 0 || !HasCurrent) return null;
        if (_files.Count == 1) return CurrentPath;

        CurrentIndex = CurrentIndex <= 0 ? _files.Count - 1 : CurrentIndex - 1;
        return CurrentPath;
    }

    public string? MoveNext()
    {
        if (_files.Count == 0 || !HasCurrent) return null;
        if (_files.Count == 1) return CurrentPath;

        CurrentIndex = CurrentIndex >= _files.Count - 1 ? 0 : CurrentIndex + 1;
        return CurrentPath;
    }

    public IReadOnlyList<string> GetAdjacentPaths()
    {
        if (_files.Count <= 1 || !HasCurrent) return [];

        var previousIndex = CurrentIndex <= 0 ? _files.Count - 1 : CurrentIndex - 1;
        var nextIndex = CurrentIndex >= _files.Count - 1 ? 0 : CurrentIndex + 1;

        return previousIndex == nextIndex
            ? [_files[previousIndex]]
            : [_files[previousIndex], _files[nextIndex]];
    }

    public IReadOnlyList<string> GetLookaroundPaths(int forwardCount, int backwardCount)
    {
        if (_files.Count <= 1 || !HasCurrent) return [];

        forwardCount = Math.Max(0, forwardCount);
        backwardCount = Math.Max(0, backwardCount);
        if (forwardCount == 0 && backwardCount == 0) return [];

        var paths = new List<string>(Math.Min(_files.Count - 1, forwardCount + backwardCount));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (forwardCount > 0)
        {
            AddOffset(1);
        }

        if (backwardCount > 0)
        {
            AddOffset(-1);
        }

        for (var offset = 2; offset <= forwardCount; offset++)
        {
            AddOffset(offset);
        }

        for (var offset = 2; offset <= backwardCount; offset++)
        {
            AddOffset(-offset);
        }

        return paths;

        void AddOffset(int offset)
        {
            if (offset == 0 || paths.Count >= _files.Count - 1) return;

            var index = CurrentIndex + offset;
            while (index < 0)
            {
                index += _files.Count;
            }

            index %= _files.Count;
            var path = _files[index];
            if (seen.Add(path))
            {
                paths.Add(path);
            }
        }
    }
}

public sealed record ImageFolderSnapshot(IReadOnlyList<string> Files, int CurrentIndex);
