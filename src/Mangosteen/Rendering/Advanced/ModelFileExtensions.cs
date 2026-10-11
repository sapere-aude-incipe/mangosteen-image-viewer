using System.IO;

namespace Mangosteen.Rendering.Advanced;

internal static class ModelFileExtensions
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".stl", ".ply", ".obj", ".gltf", ".glb"
        };

    public static IReadOnlyCollection<string> Supported => Extensions;

    public static bool IsSupported(string path)
    {
        return Extensions.Contains(Path.GetExtension(path));
    }
}
