using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mangosteen.Updates;

internal sealed class GitHubUpdateService : IDisposable
{
    private const int DownloadBufferSize = 128 * 1024;
    private const long DownloadProgressIntervalBytes = 1024 * 1024;

    public const string ReleasesPageUrl = "https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases";

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/sapere-aude-incipe/mangosteen-image-viewer/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public GitHubUpdateService()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    internal GitHubUpdateService(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(
        ReleaseVersion currentVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mangosteen-Image-Viewer", currentVersion.ToString()));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseLatestRelease(currentVersion, json);
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        string downloadDirectory,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (update.InstallerAsset is null)
        {
            throw new InvalidOperationException("The latest release does not include a Windows installer.");
        }

        Directory.CreateDirectory(downloadDirectory);

        var expectedSha256 = update.InstallerAsset.Sha256;
        if (string.IsNullOrWhiteSpace(expectedSha256) && update.ChecksumAsset is not null)
        {
            var checksums = await _httpClient.GetStringAsync(
                update.ChecksumAsset.DownloadUrl,
                cancellationToken).ConfigureAwait(false);
            expectedSha256 = TryFindSha256ForAsset(checksums, update.InstallerAsset.Name);
        }

        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidOperationException("The release does not include a SHA256 checksum for the installer.");
        }

        var installerPath = Path.Combine(downloadDirectory, update.InstallerAsset.Name);
        var temporaryPath = installerPath + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        var completed = false;
        try
        {
            using (var response = await _httpClient.GetAsync(
                update.InstallerAsset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var destination = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    DownloadBufferSize,
                    useAsync: true);
                await CopyToAsync(
                    source,
                    destination,
                    response.Content.Headers.ContentLength,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            var actualSha256 = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The downloaded installer did not match the published SHA256 checksum.");
            }

            File.Move(temporaryPath, installerPath, overwrite: true);
            completed = true;
            return installerPath;
        }
        finally
        {
            if (!completed)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    internal static UpdateCheckResult ParseLatestRelease(ReleaseVersion currentVersion, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString();
        if (!ReleaseVersion.TryParse(tagName, out var latestVersion))
        {
            throw new InvalidOperationException($"GitHub returned an unsupported release tag: {tagName}");
        }

        var releaseName = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var releasePageUrl = root.TryGetProperty("html_url", out var htmlUrlElement)
            ? htmlUrlElement.GetString()
            : null;

        UpdateAsset? installerAsset = null;
        UpdateAsset? checksumAsset = null;
        var expectedInstallerName = $"Mangosteen-Setup-{latestVersion}-x64.exe";

        if (root.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                var asset = ParseAsset(assetElement);
                if (asset is null)
                {
                    continue;
                }

                if (string.Equals(asset.Name, expectedInstallerName, StringComparison.OrdinalIgnoreCase))
                {
                    installerAsset = asset;
                }
                else if (string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumAsset = asset;
                }
            }
        }

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            string.IsNullOrWhiteSpace(releaseName) ? $"Mangosteen {latestVersion}" : releaseName!,
            string.IsNullOrWhiteSpace(releasePageUrl) ? ReleasesPageUrl : releasePageUrl!,
            installerAsset,
            checksumAsset);
    }

    internal static string? TryFindSha256ForAsset(string checksums, string assetName)
    {
        foreach (var rawLine in checksums.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            if (IsSha256(parts[0]) &&
                string.Equals(parts[^1], assetName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }

    private static UpdateAsset? ParseAsset(JsonElement assetElement)
    {
        var name = assetElement.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var downloadUrl = assetElement.TryGetProperty("browser_download_url", out var urlElement)
            ? urlElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var sha256 = assetElement.TryGetProperty("digest", out var digestElement)
            ? NormalizeSha256Digest(digestElement.GetString())
            : null;

        return new UpdateAsset(name!, downloadUrl!, sha256);
    }

    private static string? NormalizeSha256Digest(string? digest)
    {
        const string prefix = "sha256:";

        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var value = digest.Trim();
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..];
        }

        return IsSha256(value) ? value.ToLowerInvariant() : null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DownloadBufferSize);
        var downloadedBytes = 0L;
        var nextProgressReport = DownloadProgressIntervalBytes;
        progress?.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes));

        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(
                    buffer.AsMemory(0, DownloadBufferSize),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken).ConfigureAwait(false);
                downloadedBytes += bytesRead;

                if (downloadedBytes >= nextProgressReport)
                {
                    progress?.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes));
                    nextProgressReport = downloadedBytes + DownloadProgressIntervalBytes;
                }
            }

            progress?.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsSha256(string value)
    {
        return value.Length == 64 && value.All(Uri.IsHexDigit);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to remove incomplete update download '{path}': {ex}");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

internal sealed record UpdateCheckResult(
    ReleaseVersion CurrentVersion,
    ReleaseVersion LatestVersion,
    string ReleaseName,
    string ReleasePageUrl,
    UpdateAsset? InstallerAsset,
    UpdateAsset? ChecksumAsset)
{
    public bool IsUpdateAvailable => LatestVersion.CompareTo(CurrentVersion) > 0;
}

internal sealed record UpdateAsset(
    string Name,
    string DownloadUrl,
    string? Sha256);

internal readonly record struct UpdateDownloadProgress(
    long BytesDownloaded,
    long? TotalBytes);

internal readonly record struct ReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        normalized = normalized.Split('+', 2)[0];
        var versionAndPrerelease = normalized.Split('-', 2);
        var coreParts = versionAndPrerelease[0].Split('.');
        if (coreParts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(coreParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(coreParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(coreParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        var prerelease = versionAndPrerelease.Length == 2 ? versionAndPrerelease[1] : null;
        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0) return patch;

        if (string.IsNullOrWhiteSpace(Prerelease))
        {
            return string.IsNullOrWhiteSpace(other.Prerelease) ? 0 : 1;
        }

        if (string.IsNullOrWhiteSpace(other.Prerelease))
        {
            return -1;
        }

        return ComparePrereleaseIdentifiers(Prerelease, other.Prerelease);
    }

    private static int ComparePrereleaseIdentifiers(string left, string right)
    {
        var leftIdentifiers = left.Split('.');
        var rightIdentifiers = right.Split('.');
        var sharedLength = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);

        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = ComparePrereleaseIdentifier(leftIdentifiers[index], rightIdentifiers[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftIsNumeric = left.Length > 0 && left.All(char.IsAsciiDigit);
        var rightIsNumeric = right.Length > 0 && right.All(char.IsAsciiDigit);
        if (leftIsNumeric && rightIsNumeric)
        {
            var normalizedLeft = left.TrimStart('0');
            var normalizedRight = right.TrimStart('0');
            normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
            normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;

            var lengthComparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
            return lengthComparison != 0
                ? lengthComparison
                : string.Compare(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }

        if (leftIsNumeric != rightIsNumeric)
        {
            return leftIsNumeric ? -1 : 1;
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Prerelease)
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{Prerelease}";
    }
}
