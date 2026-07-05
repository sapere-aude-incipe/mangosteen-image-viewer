using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Mangosteen;

internal static class StartupDiagnostics
{
    private static readonly object Gate = new();
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("MANGOSTEEN_STARTUP_LOG");

    public static bool IsEnabled => !string.IsNullOrWhiteSpace(LogPath);

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
}
