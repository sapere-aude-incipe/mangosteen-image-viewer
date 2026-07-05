using Mangosteen.Decoding;
using System.Text.RegularExpressions;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed partial class InstallerSupportedTypesTests
{
    [TestMethod]
    public void Inno_Setup_Registers_All_Broad_Image_Extensions()
    {
        var supportedTypes = GetInstallerSupportedTypes();
        var missing = ImageFileExtensions.BroadImageExtensions
            .Except(supportedTypes, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsEmpty(missing, "Installer is missing SupportedTypes entries: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void Inno_Setup_Does_Not_Register_Duplicate_SupportedTypes()
    {
        var supportedTypes = GetInstallerSupportedTypes();
        var duplicates = supportedTypes
            .GroupBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsEmpty(duplicates, "Installer has duplicate SupportedTypes entries: " + string.Join(", ", duplicates));
    }

    private static string[] GetInstallerSupportedTypes()
    {
        var installerScript = Path.Combine(GetRepositoryRoot(), "packaging", "inno", "Mangosteen.iss");
        var text = File.ReadAllText(installerScript);
        return SupportedTypeRegex()
            .Matches(text)
            .Select(match => match.Groups["extension"].Value)
            .ToArray();
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Mangosteen.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Mangosteen repository root.");
    }

    [GeneratedRegex("ValueName: \"(?<extension>\\.[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex SupportedTypeRegex();
}
