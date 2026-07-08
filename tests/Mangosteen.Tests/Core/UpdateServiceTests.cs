using Mangosteen.Updates;

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
}
