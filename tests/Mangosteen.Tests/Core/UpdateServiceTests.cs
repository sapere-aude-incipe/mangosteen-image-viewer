using Mangosteen.Updates;

using System.Net;
using System.Security.Cryptography;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class UpdateServiceTests
{
    [TestMethod]
    public void ReleaseVersion_Parses_Tag_With_V_Prefix()
    {
        Assert.IsTrue(ReleaseVersion.TryParse("v0.2.4", out var version));

        Assert.AreEqual("0.2.4", version.ToString());
    }

    [TestMethod]
    public void ReleaseVersion_Treats_Stable_As_Newer_Than_Same_Prerelease()
    {
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4-preview.1", out var preview));
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4", out var stable));

        Assert.IsGreaterThan(0, stable.CompareTo(preview));
    }

    [TestMethod]
    public void ReleaseVersion_Treats_Equal_Stable_Versions_As_Equal()
    {
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4", out var current));
        Assert.IsTrue(ReleaseVersion.TryParse("v0.2.4", out var latest));

        Assert.AreEqual(0, latest.CompareTo(current));
        Assert.IsFalse(new UpdateCheckResult(
            current,
            latest,
            "Mangosteen 0.2.4",
            GitHubUpdateService.ReleasesPageUrl,
            InstallerAsset: null,
            ChecksumAsset: null).IsUpdateAvailable);
    }

    [TestMethod]
    public void ReleaseVersion_Orders_Numeric_Prerelease_Identifiers_Numerically()
    {
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4-preview.2", out var preview2));
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4-preview.10", out var preview10));

        Assert.IsGreaterThan(0, preview10.CompareTo(preview2));
    }

    [TestMethod]
    public void ReleaseVersion_Orders_Numeric_Prerelease_Identifiers_Before_Text()
    {
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4-1", out var numeric));
        Assert.IsTrue(ReleaseVersion.TryParse("0.2.4-preview", out var text));

        Assert.IsLessThan(0, numeric.CompareTo(text));
    }

    [TestMethod]
    public void ParseLatestRelease_Finds_Installer_And_Checksum_Assets()
    {
        var json = """
        {
          "tag_name": "v0.2.4",
          "name": "Mangosteen 0.2.4",
          "html_url": "https://github.com/sapere-aude-incipe/mangosteen-image-viewer/releases/tag/v0.2.4",
          "assets": [
            {
              "name": "Mangosteen-Portable-0.2.4-x64.zip",
              "browser_download_url": "https://example.test/portable.zip"
            },
            {
              "name": "Mangosteen-Setup-0.2.4-x64.exe",
              "browser_download_url": "https://example.test/setup.exe",
              "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            },
            {
              "name": "SHA256SUMS.txt",
              "browser_download_url": "https://example.test/SHA256SUMS.txt"
            }
          ]
        }
        """;

        Assert.IsTrue(ReleaseVersion.TryParse("0.2.3", out var current));

        var result = GitHubUpdateService.ParseLatestRelease(current, json);

        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("0.2.4", result.LatestVersion.ToString());
        Assert.AreEqual("Mangosteen-Setup-0.2.4-x64.exe", result.InstallerAsset?.Name);
        Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.InstallerAsset?.Sha256);
        Assert.AreEqual("SHA256SUMS.txt", result.ChecksumAsset?.Name);
    }

    [TestMethod]
    public void TryFindSha256ForAsset_Returns_Matching_Checksum()
    {
        var checksums = """
        bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  Mangosteen-Portable-0.2.4-x64.zip
        cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  Mangosteen-Setup-0.2.4-x64.exe
        """;

        var checksum = GitHubUpdateService.TryFindSha256ForAsset(
            checksums,
            "Mangosteen-Setup-0.2.4-x64.exe");

        Assert.AreEqual("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", checksum);
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_Reports_Byte_Progress_And_Writes_File()
    {
        var payload = CreatePayload(2_500_000);

        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new GitHubUpdateService(client);
        var update = new UpdateCheckResult(
            new ReleaseVersion(0, 2, 3, null),
            new ReleaseVersion(0, 2, 4, null),
            "Mangosteen 0.2.4",
            GitHubUpdateService.ReleasesPageUrl,
            new UpdateAsset(
                "Mangosteen-Setup-0.2.4-x64.exe",
                "https://example.test/setup.exe",
                sha256),
            ChecksumAsset: null);
        var progress = new RecordingProgress();
        var directory = Path.Combine(Path.GetTempPath(), $"mangosteen-update-{Guid.NewGuid():N}");

        try
        {
            var path = await service.DownloadInstallerAsync(
                update,
                directory,
                progress,
                CancellationToken.None);

            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(path));
            Assert.IsGreaterThan(1, progress.Values.Count);
            Assert.AreEqual(0, progress.Values[0].BytesDownloaded);
            Assert.AreEqual(payload.LongLength, progress.Values[^1].BytesDownloaded);
            Assert.AreEqual(payload.LongLength, progress.Values[^1].TotalBytes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_Rejects_Checksum_Mismatch_And_Removes_Temporary_File()
    {
        var payload = CreatePayload(4096);
        using var client = new HttpClient(new StaticResponseHandler(payload));
        using var service = new GitHubUpdateService(client);
        var update = CreateUpdate(new string('0', 64));
        var directory = CreateDownloadDirectory();

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.DownloadInstallerAsync(update, directory, progress: null, CancellationToken.None));

            StringAssert.Contains(ex.Message, "did not match");
            Assert.IsFalse(File.Exists(Path.Combine(directory, update.InstallerAsset!.Name)));
            Assert.IsFalse(File.Exists(Path.Combine(directory, update.InstallerAsset.Name + ".tmp")));
        }
        finally
        {
            DeleteDownloadDirectory(directory);
        }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_Uses_Published_Checksum_File_When_Asset_Digest_Is_Missing()
    {
        var payload = CreatePayload(8192);
        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        const string checksumUrl = "https://example.test/SHA256SUMS.txt";
        const string installerUrl = "https://example.test/setup.exe";
        using var client = new HttpClient(new StaticResponseHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == checksumUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{sha256}  Mangosteen-Setup-0.2.4-x64.exe")
                };
            }

            Assert.AreEqual(installerUrl, request.RequestUri?.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        }));
        using var service = new GitHubUpdateService(client);
        var update = CreateUpdate(
            sha256: null,
            new UpdateAsset("SHA256SUMS.txt", checksumUrl, Sha256: null));
        var directory = CreateDownloadDirectory();

        try
        {
            var path = await service.DownloadInstallerAsync(
                update,
                directory,
                progress: null,
                CancellationToken.None);

            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteDownloadDirectory(directory);
        }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_Removes_Partial_File_When_Stream_Fails()
    {
        var payload = CreatePayload(256 * 1024);
        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        using var client = new HttpClient(new StaticResponseHandler(_ =>
        {
            var content = new StreamContent(new FailingReadStream(payload, bytesBeforeFailure: 64 * 1024));
            content.Headers.ContentLength = payload.LongLength;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        using var service = new GitHubUpdateService(client);
        var update = CreateUpdate(sha256);
        var directory = CreateDownloadDirectory();

        try
        {
            await Assert.ThrowsExactlyAsync<IOException>(
                () => service.DownloadInstallerAsync(update, directory, progress: null, CancellationToken.None));

            Assert.IsFalse(File.Exists(Path.Combine(directory, update.InstallerAsset!.Name)));
            Assert.IsFalse(File.Exists(Path.Combine(directory, update.InstallerAsset.Name + ".tmp")));
        }
        finally
        {
            DeleteDownloadDirectory(directory);
        }
    }

    [TestMethod]
    public async Task DownloadInstallerAsync_Removes_Partial_File_When_Cancelled()
    {
        var payload = CreatePayload(256 * 1024);
        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var stream = new BlockingReadStream(payload, bytesBeforeBlock: 64 * 1024);
        using var client = new HttpClient(new StaticResponseHandler(_ =>
        {
            var content = new StreamContent(stream);
            content.Headers.ContentLength = payload.LongLength;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        using var service = new GitHubUpdateService(client);
        using var cts = new CancellationTokenSource();
        var update = CreateUpdate(sha256);
        var directory = CreateDownloadDirectory();

        try
        {
            var downloadTask = service.DownloadInstallerAsync(update, directory, progress: null, cts.Token);
            await stream.Blocked.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();

            try
            {
                await downloadTask;
                Assert.Fail("The download should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.IsFalse(File.Exists(Path.Combine(directory, update.InstallerAsset!.Name)));
            Assert.IsFalse(File.Exists(Path.Combine(directory, update.InstallerAsset.Name + ".tmp")));
        }
        finally
        {
            DeleteDownloadDirectory(directory);
            stream.Dispose();
        }
    }

    private static UpdateCheckResult CreateUpdate(string? sha256, UpdateAsset? checksumAsset = null)
    {
        return new UpdateCheckResult(
            new ReleaseVersion(0, 2, 3, null),
            new ReleaseVersion(0, 2, 4, null),
            "Mangosteen 0.2.4",
            GitHubUpdateService.ReleasesPageUrl,
            new UpdateAsset(
                "Mangosteen-Setup-0.2.4-x64.exe",
                "https://example.test/setup.exe",
                sha256),
            checksumAsset);
    }

    private static string CreateDownloadDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"mangosteen-update-{Guid.NewGuid():N}");
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)((index * 31 + 7) % 251);
        }

        return payload;
    }

    private static void DeleteDownloadDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingProgress : IProgress<UpdateDownloadProgress>
    {
        public List<UpdateDownloadProgress> Values { get; } = [];

        public void Report(UpdateDownloadProgress value)
        {
            Values.Add(value);
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StaticResponseHandler(byte[] payload)
            : this(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            })
        {
        }

        public StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class FailingReadStream(byte[] bytes, int bytesBeforeFailure) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Position >= bytesBeforeFailure)
            {
                return ValueTask.FromException<int>(new IOException("Simulated interrupted download."));
            }

            var remainingBeforeFailure = bytesBeforeFailure - (int)Position;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, remainingBeforeFailure)], cancellationToken);
        }
    }

    private sealed class BlockingReadStream(byte[] bytes, int bytesBeforeBlock) : MemoryStream(bytes, writable: false)
    {
        private readonly TaskCompletionSource<bool> _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Blocked => _blocked.Task;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Position >= bytesBeforeBlock)
            {
                _blocked.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }

            var remainingBeforeBlock = bytesBeforeBlock - (int)Position;
            return await base.ReadAsync(
                buffer[..Math.Min(buffer.Length, remainingBeforeBlock)],
                cancellationToken);
        }
    }
}
