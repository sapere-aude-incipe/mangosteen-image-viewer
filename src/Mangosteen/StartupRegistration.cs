using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace Mangosteen;

internal static class StartupRegistration
{
    internal const string ValueName = "Mangosteen Image Viewer";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;

        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                error = "The Windows startup registry key could not be opened.";
                return false;
            }

            if (enabled)
            {
                var commandLine = BuildCommandLine(GetExecutablePath());
                var currentValue = runKey.GetValue(
                    ValueName,
                    defaultValue: null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (!string.Equals(currentValue, commandLine, StringComparison.Ordinal))
                {
                    runKey.SetValue(ValueName, commandLine, RegistryValueKind.String);
                }
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Failed to update Mangosteen startup registration: {ex}");
            error = ex.Message;
            return false;
        }
    }

    internal static string BuildCommandLine(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\" --background";
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("The Mangosteen executable path is unavailable.");
    }
}
