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

            var settings = JsonSerializer.Deserialize(File.ReadAllText(path), JsonContext.AppSettings);
            return Normalize(settings);
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
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Normalize(this), JsonContext.AppSettings);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning($"Failed to save Mangosteen settings: {ex}");
        }
    }

    private static string GetDefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Mangosteen Image Viewer", "settings.json");
    }

    private static AppSettings Normalize(AppSettings? settings)
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
            PreloadBudgetGigabytes = Math.Clamp(
                settings.PreloadBudgetGigabytes,
                MinimumPreloadBudgetGigabytes,
                MaximumPreloadBudgetGigabytes),
            PreloadAggressiveness = aggressiveness
        };
    }
}
