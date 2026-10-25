using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace Mangosteen;

internal static class StartupRegistration
{
    internal const string ValueName = "Mangosteen Image Viewer";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool TrySetEnabled(bool enabled, out string? error, bool replaceExisting = true)
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

            var commandLine = BuildCommandLine(GetExecutablePath());
            var currentValue = runKey.GetValue(
                ValueName, defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (!MayReplaceEntry(currentValue, commandLine, replaceExisting, File.Exists)) return true;

            if (enabled)
            {
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

    public static bool IsEnabledForCurrentExecutable()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var currentValue = runKey?.GetValue(
                ValueName, defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return string.Equals(
                currentValue,
                BuildCommandLine(GetExecutablePath()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Failed to read Mangosteen startup registration: {ex}");
            return false;
        }
    }

    // Another installation's entry is left alone unless the user asked for this one,
    // but an entry whose executable is gone (e.g. a portable copy moved to a new
    // folder) is stale and would otherwise leave keep-ready silently broken.
    internal static bool MayReplaceEntry(
        string? currentValue,
        string commandLine,
        bool explicitlyRequested,
        Func<string, bool> fileExists) =>
        explicitlyRequested || string.IsNullOrWhiteSpace(currentValue) ||
        string.Equals(currentValue, commandLine, StringComparison.OrdinalIgnoreCase) ||
        GetRegisteredExecutablePath(currentValue) is { } registered && !fileExists(registered);

    internal static string? GetRegisteredExecutablePath(string commandLine)
    {
        var value = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        string path;
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end < 0) return null;
            path = value[1..end];
        }
        else
        {
            var exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exe < 0 ? value.Split(' ', 2)[0] : value[..(exe + 4)];
        }

        path = path.Trim();
        return path.Length == 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(path)
            ? null
            : path;
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("The Mangosteen executable path is unavailable.");
    }
}
