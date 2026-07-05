using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Mangosteen;

internal static class StartupDiagnostics
{
    private const string EnvironmentVariableName = "MANGOSTEEN_STARTUP_LOG";
    private const string DiagnosticsDirectoryName = "Diagnostics";
    private static readonly object Gate = new();
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private static readonly string? LogPath = ResolveLogPath(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    public static bool IsEnabled => !string.IsNullOrWhiteSpace(LogPath);

    internal static string? ResolveLogPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        try
        {
            if (RequestsDefaultLog(trimmed))
            {
                return Path.Combine(GetDiagnosticsDirectory(), $"startup-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");
            }

            if (Path.IsPathFullyQualified(trimmed))
            {
                return null;
            }

            var fileName = SanitizeFileName(Path.GetFileName(trimmed));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (!fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".log";
            }

            return Path.Combine(GetDiagnosticsDirectory(), fileName);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            Trace.TraceWarning($"Ignoring invalid {EnvironmentVariableName} value: {ex.Message}");
            return null;
        }
    }

    public static void Mark(string name, string? detail = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(LogPath!);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{Stopwatch.Elapsed.TotalMilliseconds:0.0}\t{name}\t{detail ?? string.Empty}{Environment.NewLine}");

            lock (Gate)
            {
                File.AppendAllText(LogPath!, line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning($"Failed to write startup diagnostics: {ex}");
        }
    }

    private static bool RequestsDefaultLog(string value)
    {
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("default", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDiagnosticsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
        return Path.Combine(root, "Mangosteen Image Viewer", DiagnosticsDirectoryName);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(characters).Trim();
    }
}
