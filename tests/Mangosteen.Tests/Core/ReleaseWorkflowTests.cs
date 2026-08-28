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

    private static string GetReleaseWorkflowText()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var workflow = Path.Combine(current.FullName, ".github", "workflows", "release.yml");
            if (File.Exists(workflow))
            {
                return File.ReadAllText(workflow);
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Mangosteen release workflow.");
    }
}

