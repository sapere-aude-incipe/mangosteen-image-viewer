using Mangosteen.Decoding;
using System.Text.RegularExpressions;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed partial class InstallerSupportedTypesTests
{
    [TestMethod]
    public void Inno_Setup_Uses_Full_Product_Name_For_Uninstall_Display()
    {
        var text = GetInstallerScriptText();

        StringAssert.Contains(text, "#define AppDisplayName \"Mangosteen Image Viewer\"");
        StringAssert.Contains(text, "UninstallDisplayName={#AppDisplayName}");
    }

    [TestMethod]
    public void Inno_Setup_Offers_Checked_File_Association_Task()
    {
        var text = GetInstallerScriptText();
        var taskLine = text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.Contains("Name: \"associatefiles\"", StringComparison.Ordinal));

        StringAssert.Contains(taskLine, "Register supported image file types");
        Assert.IsFalse(taskLine.Contains("unchecked", StringComparison.OrdinalIgnoreCase));
    }

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
    public void Inno_Setup_Registers_All_Broad_Image_Extensions_As_Capabilities()
    {
        var capabilityTypes = GetInstallerCapabilityTypes();
        var missing = ImageFileExtensions.BroadImageExtensions
            .Except(capabilityTypes, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpected = capabilityTypes
            .Except(ImageFileExtensions.BroadImageExtensions, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsEmpty(missing, "Installer is missing capability entries: " + string.Join(", ", missing));
        Assert.IsEmpty(unexpected, "Installer has unexpected capability entries: " + string.Join(", ", unexpected));
    }

    [TestMethod]
    public void Inno_Setup_Registers_Jpeg_Family_For_Windows_File_Associations()
    {
        var text = GetInstallerScriptText();

        StringAssert.Contains(text, "#define AppImageProgId \"Mangosteen.Image\"");
        StringAssert.Contains(text, @"Subkey: ""Software\RegisteredApplications""");
        StringAssert.Contains(text, @"Subkey: ""Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations""; ValueType: string; ValueName: "".jpg""; ValueData: ""{#AppImageProgId}""; Flags: uninsdeletekey; Tasks: associatefiles");
        StringAssert.Contains(text, @"Subkey: ""Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations""; ValueType: string; ValueName: "".jpe""; ValueData: ""{#AppImageProgId}""; Flags: uninsdeletekey; Tasks: associatefiles");
        StringAssert.Contains(text, @"Subkey: ""Software\Classes\Applications\{#AppExeName}\Capabilities\FileAssociations""; ValueType: string; ValueName: "".jpeg""; ValueData: ""{#AppImageProgId}""; Flags: uninsdeletekey; Tasks: associatefiles");
        StringAssert.Contains(text, @"Subkey: ""Software\Classes\.jpeg\OpenWithProgids""; ValueType: string; ValueName: ""{#AppImageProgId}""; ValueData: """"; Flags: uninsdeletevalue; Tasks: associatefiles");
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

    [TestMethod]
    public void Inno_Setup_Does_Not_Register_Duplicate_Capability_Types()
    {
        var duplicates = GetInstallerCapabilityTypes()
            .GroupBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.IsEmpty(duplicates, "Installer has duplicate capability entries: " + string.Join(", ", duplicates));
    }

    [TestMethod]
    public void Inno_Setup_Gates_SupportedTypes_Behind_File_Association_Task()
    {
        var unsupportedLines = GetInstallerScriptText()
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.Contains(@"\SupportedTypes""", StringComparison.Ordinal))
            .Where(line => line.Contains("ValueName:", StringComparison.Ordinal))
            .Where(line => !line.Contains("Tasks: associatefiles", StringComparison.Ordinal))
            .ToArray();

        Assert.IsEmpty(unsupportedLines, "SupportedTypes entries must be gated by the associatefiles task.");
    }

    [TestMethod]
    public void Inno_Setup_Gates_Capability_Types_Behind_File_Association_Task()
    {
        var unsupportedLines = GetInstallerScriptText()
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.Contains(@"\Capabilities\FileAssociations""", StringComparison.Ordinal))
            .Where(line => line.Contains("ValueName:", StringComparison.Ordinal))
            .Where(line => !line.Contains("Tasks: associatefiles", StringComparison.Ordinal))
            .ToArray();

        Assert.IsEmpty(unsupportedLines, "Capability entries must be gated by the associatefiles task.");
    }

    private static string[] GetInstallerSupportedTypes()
    {
        var text = GetInstallerScriptText();
        return SupportedTypeRegex()
            .Matches(text)
            .Select(match => match.Groups["extension"].Value)
            .ToArray();
    }

    private static string[] GetInstallerCapabilityTypes()
    {
        var text = GetInstallerScriptText();
        return CapabilityTypeRegex()
            .Matches(text)
            .Select(match => match.Groups["extension"].Value)
            .ToArray();
    }

    private static string GetInstallerScriptText()
    {
        var installerScript = Path.Combine(GetRepositoryRoot(), "packaging", "inno", "Mangosteen.iss");
        return File.ReadAllText(installerScript);
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

    [GeneratedRegex(@"SupportedTypes""; ValueType: string; ValueName: ""(?<extension>\.[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex SupportedTypeRegex();

    [GeneratedRegex(@"Capabilities\\FileAssociations""; ValueType: string; ValueName: ""(?<extension>\.[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityTypeRegex();
}
