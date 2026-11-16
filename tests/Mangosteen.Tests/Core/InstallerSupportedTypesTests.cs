using Mangosteen.Decoding;
using Mangosteen.Rendering.Advanced;
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

        StringAssert.Contains(taskLine, "Register file types with {#AppDisplayName}");
        Assert.IsFalse(taskLine.Contains("unchecked", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Inno_Setup_Lets_Each_Image_Format_Be_Chosen_And_Removes_Unticked_Ones()
    {
        var text = GetInstallerScriptText();
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        var childTasks = lines
            .Select(line => ChildTaskRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["task"].Value)
            .ToArray();
        // Child tasks start checked, so a plain install still registers every format.
        Assert.IsFalse(lines.Any(line => line.StartsWith("Name: \"associatefiles\\", StringComparison.Ordinal) &&
            line.Contains("unchecked", StringComparison.OrdinalIgnoreCase)));

        foreach (var extension in ImageFileExtensions.BroadImageExtensions)
        {
            var entries = lines
                .Where(line => line.StartsWith("Root: ", StringComparison.Ordinal))
                .Where(line =>
                    line.Contains($@"\Classes\{extension}\OpenWithProgids""; ValueType: string; ValueName: ""{{#AppImageProgId}}""", StringComparison.OrdinalIgnoreCase) ||
                    (line.Contains($"ValueName: \"{extension}\";", StringComparison.OrdinalIgnoreCase) &&
                     (line.Contains(@"\Capabilities\FileAssociations""", StringComparison.Ordinal) || line.Contains(@"\SupportedTypes""", StringComparison.Ordinal))))
                .ToArray();
            // Capability and SupportedTypes for every format; the JPEG family also has OpenWithProgids.
            Assert.IsGreaterThanOrEqualTo(2, entries.Length, extension);
            var tasks = entries.Select(line => TaskOfLineRegex().Match(line).Groups["task"].Value).Distinct().ToArray();
            Assert.HasCount(1, tasks, $"{extension} entries must share one format task.");
            CollectionAssert.Contains(childTasks, tasks[0], $"{extension} uses a task that is not offered.");

            // Unticking the format on a reinstall removes the same extension.
            var removal = lines.Single(line =>
                line.Contains($"RemoveImageAssociations(RegistryRoot, '{tasks[0]}',", StringComparison.Ordinal));
            StringAssert.Contains(removal, $"'{extension}'");
        }
    }

    [TestMethod]
    public void Inno_Setup_Leaves_Shared_File_Types_Unnamed_So_Explorer_Names_Each_Format()
    {
        var text = GetInstallerScriptText();

        // A named ProgID gives every format the same Type text, so sorting by type mixes PNG and DNG.
        StringAssert.Contains(text, @"Subkey: ""Software\Classes\{#AppImageProgId}""; ValueType: none; ValueName: """"; Flags: deletevalue uninsdeletekey");
        StringAssert.Contains(text, @"Subkey: ""Software\Classes\{#AppModelProgId}""; ValueType: none; ValueName: """"; Flags: deletevalue uninsdeletekey");
        Assert.IsFalse(text.Contains(@"{#AppImageProgId}""; ValueType: string; ValueName: """"", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(@"{#AppModelProgId}""; ValueType: string; ValueName: """"", StringComparison.Ordinal));
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
    public void Inno_Setup_Registers_All_Model_Extensions_Only_With_Optional_Component()
    {
        var text = GetInstallerScriptText();
        var modelExtensions = GetInstallerModelCapabilityTypes();

        CollectionAssert.AreEquivalent(ModelFileExtensions.Supported.ToArray(), modelExtensions);
        foreach (var extension in modelExtensions)
        {
            StringAssert.Contains(text, $@"Subkey: ""Software\Classes\{extension}\OpenWithProgids""; ValueType: string; ValueName: ""{{#AppModelProgId}}""");
            StringAssert.Contains(text, $"RemoveModelAssociation(RegistryRoot, '{extension}');");
        }

        foreach (var extension in modelExtensions)
        {
            var line = text
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Single(candidate =>
                    candidate.Contains(@"\Capabilities\FileAssociations""", StringComparison.Ordinal) &&
                    candidate.Contains($"ValueName: \"{extension}\"", StringComparison.Ordinal));
            StringAssert.Contains(line, "Tasks: associatefiles");
            StringAssert.Contains(line, "Components: modelviewer");
        }
    }

    [TestMethod]
    public void Inno_Setup_Defaults_To_Core_And_Offers_Full()
    {
        var text = GetInstallerScriptText();
        var types = text[text.IndexOf("[Types]", StringComparison.Ordinal)..text.IndexOf("[Components]", StringComparison.Ordinal)]
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Name:", StringComparison.Ordinal))
            .ToArray();

        // Inno Setup selects the first type by default, so Core must stay first.
        StringAssert.StartsWith(types[0], "Name: \"core\"; Description: \"Core image viewer\"");
        CollectionAssert.AreEqual(
            new[]
            {
                "Name: \"core\"; Description: \"Core image viewer\"",
                "Name: \"full\"; Description: \"Full: core viewer with 3D model viewing\""
            },
            types);
        StringAssert.Contains(text, "Name: \"modelviewer\"; Description: \"3D model viewing (F3D 3.5.0, adds about 119 MB)\"; Types: full");
        // GPU large-image rendering is a built-in setting; upgrades remove the old component folder.
        Assert.IsFalse(text.Contains("gpulargeimages", StringComparison.Ordinal));
        StringAssert.Contains(text, "Type: filesandordirs; Name: \"{app}\\components\\gpu-large-images\"");
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

    [TestMethod]
    public void Inno_Setup_Only_Sends_Shutdown_To_Background_Capable_Installations()
    {
        var text = GetInstallerScriptText();

        StringAssert.Contains(text, "RegQueryStringValue(");
        StringAssert.Contains(text, @"Software\Microsoft\Windows\CurrentVersion\Run");
        StringAssert.Contains(text, "(Pos('--background', Lowercase(StartupCommand)) > 0)");
        StringAssert.Contains(text, "Exec(ExistingApp, '--shutdown'");
        StringAssert.Contains(text, "CloseApplications=yes");
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

    private static string[] GetInstallerModelCapabilityTypes()
    {
        var text = GetInstallerScriptText();
        return ModelCapabilityTypeRegex()
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

    [GeneratedRegex(@"Capabilities\\FileAssociations""; ValueType: string; ValueName: ""(?<extension>\.[^""]+)""; ValueData: ""\{#AppImageProgId\}""", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityTypeRegex();

    [GeneratedRegex(@"Capabilities\\FileAssociations""; ValueType: string; ValueName: ""(?<extension>\.[^""]+)""; ValueData: ""\{#AppModelProgId\}""", RegexOptions.CultureInvariant)]
    private static partial Regex ModelCapabilityTypeRegex();

    [GeneratedRegex(@"^Name: ""(?<task>associatefiles\\[a-z]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex ChildTaskRegex();

    [GeneratedRegex(@"Tasks: (?<task>[^;]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TaskOfLineRegex();
}
