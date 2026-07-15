namespace Mangosteen;

internal sealed record StartupLaunchOptions(
    bool IsBackgroundLaunch,
    bool RequestShutdown,
    string? FilePath)
{
    public bool ShouldActivate => !RequestShutdown && (!IsBackgroundLaunch || FilePath is not null);

    public AppActivationRequest ToActivationRequest()
    {
        return new AppActivationRequest(FilePath, ShouldActivate, RequestShutdown);
    }

    public static StartupLaunchOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var isBackgroundLaunch = false;
        var requestShutdown = false;
        string? filePath = null;

        foreach (var argument in arguments)
        {
            if (argument.Equals("--background", StringComparison.OrdinalIgnoreCase))
            {
                isBackgroundLaunch = true;
            }
            else if (argument.Equals("--shutdown", StringComparison.OrdinalIgnoreCase))
            {
                requestShutdown = true;
            }
            else if (!argument.StartsWith("--", StringComparison.Ordinal) && filePath is null)
            {
                filePath = argument;
            }
        }

        return new StartupLaunchOptions(isBackgroundLaunch, requestShutdown, filePath);
    }
}

internal sealed record AppActivationRequest(
    string? FilePath,
    bool Activate,
    bool RequestShutdown);
