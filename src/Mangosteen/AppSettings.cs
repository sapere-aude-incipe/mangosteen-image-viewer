using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Mangosteen;

internal sealed class AppSettings
{
    private const int DefaultPreloadBudgetGigabytes = 2;
    private const int MinimumPreloadBudgetGigabytes = 1;
    private const int MaximumPreloadBudgetGigabytes = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly AppSettingsJsonContext JsonContext = new(JsonOptions);

    public bool IsDarkMode { get; init; } = true;
    public bool UseSmoothSampling { get; init; } = true;
    public bool IsPreloadEnabled { get; init; } = true;
    public bool IsAutoRefreshEnabled { get; init; }
    public bool KeepReadyInBackground { get; init; } = true;
    public bool IsModelGridAtWorldOrigin { get; init; }
    public int PreloadBudgetGigabytes { get; init; } = DefaultPreloadBudgetGigabytes;
    public PreloadAggressiveness PreloadAggressiveness { get; init; } = PreloadAggressiveness.Balanced;

    public static AppSettings Load()
    {
        return Load(GetDefaultPath());
    }

    internal static AppSettings Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AppSettings();
            }
            var settings = JsonSerializer.Deserialize(document.RootElement, JsonContext.AppSettings);
            var keepReadyInBackground = document.RootElement.TryGetProperty(
                nameof(KeepReadyInBackground),
                out _)
                ? settings?.KeepReadyInBackground
                : true;
            return Normalize(settings, keepReadyInBackground);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Trace.TraceWarning($"Failed to load Mangosteen settings: {ex}");
            return new AppSettings();
        }
    }

    public void Save()
    {
        Save(GetDefaultPath());
    }

    internal void Save(string path)
    {
        string? temporaryPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Normalize(this), JsonContext.AppSettings);
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning($"Failed to save Mangosteen settings: {ex}");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Failed to remove temporary settings file: {ex}");
                }
            }
        }
    }

    private static string GetDefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Mangosteen Image Viewer", "settings.json");
    }

    private static AppSettings Normalize(
        AppSettings? settings,
        bool? keepReadyInBackground = null)
    {
        if (settings is null)
        {
            return new AppSettings();
        }

        var aggressiveness = Enum.IsDefined(settings.PreloadAggressiveness)
            ? settings.PreloadAggressiveness
            : PreloadAggressiveness.Balanced;

        return new AppSettings
        {
            IsDarkMode = settings.IsDarkMode,
            UseSmoothSampling = settings.UseSmoothSampling,
            IsPreloadEnabled = settings.IsPreloadEnabled,
            IsAutoRefreshEnabled = settings.IsAutoRefreshEnabled,
            KeepReadyInBackground = keepReadyInBackground ?? settings.KeepReadyInBackground,
            IsModelGridAtWorldOrigin = settings.IsModelGridAtWorldOrigin,
            PreloadBudgetGigabytes = Math.Clamp(
                settings.PreloadBudgetGigabytes,
                MinimumPreloadBudgetGigabytes,
                MaximumPreloadBudgetGigabytes),
            PreloadAggressiveness = aggressiveness
        };
    }
}
