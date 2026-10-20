namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ReleaseWorkflowTests
{
    [TestMethod]
    public void Release_Workflow_Skips_Superseded_Main_Commits()
    {
        var text = GetReleaseWorkflowText();

        StringAssert.Contains(text, "- name: Confirm release commit is current");
        StringAssert.Contains(text, "latest_sha=\"$(git ls-remote");
        StringAssert.Contains(text, "if [[ \"$latest_sha\" == \"$GITHUB_SHA\" ]]");
        Assert.AreEqual(
            2,
            text.Split("if: steps.current.outputs.publish == 'true'", StringSplitOptions.None).Length - 1,
            "Both artifact download and release creation must be gated.");
    }

    [TestMethod]
    public void Release_Workflow_Does_Not_Gate_Tag_Or_Manual_Releases()
    {
        var text = GetReleaseWorkflowText();

        StringAssert.Contains(text, "if [[ \"$EVENT_NAME\" != \"push\" || \"$REF_TYPE\" != \"branch\" ]]");
        StringAssert.Contains(text, "echo \"publish=true\" >> \"$GITHUB_OUTPUT\"");
    }

    [TestMethod]
    public void Release_Workflow_Manual_Dispatch_Requires_A_New_Version()
    {
        var text = GetReleaseWorkflowText();
        var dispatch = text[text.IndexOf("workflow_dispatch:", StringComparison.Ordinal)..text.IndexOf("permissions:", StringComparison.Ordinal)];

        StringAssert.Contains(dispatch, "required: true");
        Assert.IsFalse(dispatch.Contains("default:", StringComparison.Ordinal), "Manual releases must not default to an old version.");
        StringAssert.Contains(text, "if ($env:EVENT_NAME -eq \"workflow_dispatch\")");
        StringAssert.Contains(text, "git fetch --quiet --force --tags origin");
        StringAssert.Contains(text, "git rev-parse -q --verify \"refs/tags/v$version\"");
        StringAssert.Contains(text, "Tag 'v$version' already exists.");
    }

    [TestMethod]
    public void Release_Workflow_Passes_Untrusted_Inputs_Through_Environment()
    {
        var text = GetReleaseWorkflowText();
        var runBlocks = text.ReplaceLineEndings("\n")
            .Split("run: ", StringSplitOptions.None)
            .Skip(1)
            .Select(block => block.Split("\n      - ", StringSplitOptions.None)[0]);

        StringAssert.Contains(text, "INPUT_VERSION: ${{ github.event.inputs.version }}");
        StringAssert.Contains(text, "REF_NAME: ${{ github.ref_name }}");
        StringAssert.Contains(text, "$version = $env:INPUT_VERSION");
        foreach (var block in runBlocks)
        {
            Assert.IsFalse(block.Contains("${{ github.event.inputs.", StringComparison.Ordinal), "Workflow inputs must not be interpolated into scripts.");
            Assert.IsFalse(block.Contains("${{ github.ref_name }}", StringComparison.Ordinal), "Ref names must not be interpolated into scripts.");
        }
    }

    [TestMethod]
    public void Release_Workflow_Publishes_Three_App_Packages()
    {
        var text = GetReleaseWorkflowText();

        StringAssert.Contains(text, "dist/Mangosteen-Setup-${{ needs.package.outputs.version }}-x64.exe");
        StringAssert.Contains(text, "dist/Mangosteen-Portable-${{ needs.package.outputs.version }}-x64.zip");
        StringAssert.Contains(text, "dist/Mangosteen-Complete-Portable-${{ needs.package.outputs.version }}-x64.zip");
        Assert.IsFalse(text.Contains("Mangosteen-GPU-Large-Images-", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Mangosteen-3D-Viewer-", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Release_Build_Offers_Optional_Components_In_The_Single_Installer()
    {
        var script = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "scripts", "build-installer.ps1"));

        StringAssert.Contains(script, "Mangosteen-Complete-Portable-$Version-x64.zip");
        StringAssert.Contains(script, "\"/DIncludeOptionalComponents=1\"");
        StringAssert.Contains(script, "\"/DOptionalComponentsDir=$optionalInputDir\"");
        Assert.IsFalse(script.Contains("Mangosteen-GPU-Large-Images-$Version-x64.zip", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("Mangosteen-3D-Viewer-$Version-x64.zip", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Release_And_Pr_Builds_Smoke_Test_Core_And_Full_Installs()
    {
        foreach (var workflow in new[] { "release.yml", "pr-artifacts.yml" })
        {
            var text = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", workflow));

            StringAssert.Contains(text, "- name: Smoke-test installer", workflow);
            StringAssert.Contains(text, "- name: Smoke-test full installer", workflow);
            StringAssert.Contains(text, "-SetupType full", workflow);
        }
    }

    private static string GetReleaseWorkflowText()
    {
        return File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "release.yml"));
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
}
