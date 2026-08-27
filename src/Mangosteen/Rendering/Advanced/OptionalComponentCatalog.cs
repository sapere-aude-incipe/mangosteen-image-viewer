using System.IO;
using System.Text.Json;

namespace Mangosteen.Rendering.Advanced;

internal enum OptionalComponentKind
{
    GpuLargeImages,
    ModelViewer
}

internal sealed class OptionalComponentCatalog
{
    private const string GpuEnvironmentVariable = "MANGOSTEEN_ENABLE_GPU_LARGE_IMAGES";
    private const string ModelEnvironmentVariable = "MANGOSTEEN_ENABLE_3D_VIEWER";
    private readonly string _applicationDirectory;

    public OptionalComponentCatalog(string? applicationDirectory = null)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
    }

    public bool IsInstalled(OptionalComponentKind component)
    {
        var environmentVariable = component switch
        {
            OptionalComponentKind.GpuLargeImages => GpuEnvironmentVariable,
            OptionalComponentKind.ModelViewer => ModelEnvironmentVariable,
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.Equals(environmentValue, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentValue, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasValidManifest(component);
    }

    public string GetComponentDirectory(OptionalComponentKind component)
    {
        var directoryName = component switch
        {
            OptionalComponentKind.GpuLargeImages => "gpu-large-images",
            OptionalComponentKind.ModelViewer => "model-viewer",
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

        return Path.Combine(_applicationDirectory, "components", directoryName);
    }

    public string GetManifestPath(OptionalComponentKind component)
    {
        return Path.Combine(GetComponentDirectory(component), "component.json");
    }

    private bool HasValidManifest(OptionalComponentKind component)
    {
        var manifestPath = GetManifestPath(component);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = manifest.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return component switch
            {
                OptionalComponentKind.GpuLargeImages =>
                    id.GetString() == "gpu-large-images" &&
                    root.TryGetProperty("version", out var version) &&
                    version.TryGetInt32(out var versionNumber) &&
                    versionNumber == 1,
                OptionalComponentKind.ModelViewer =>
                    id.GetString() == "model-viewer" &&
                    root.TryGetProperty("engine", out var engine) &&
                    engine.GetString() == "F3D" &&
                    root.TryGetProperty("engineVersion", out var engineVersion) &&
                    engineVersion.GetString() == "3.5.0" &&
                    File.Exists(Path.Combine(GetComponentDirectory(component), "bin", "f3d_c_api.dll")),
                _ => false
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
