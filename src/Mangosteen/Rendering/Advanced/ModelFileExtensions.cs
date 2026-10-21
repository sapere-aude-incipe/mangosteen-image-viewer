using System.IO;

namespace Mangosteen.Rendering.Advanced;

internal static class ModelFileExtensions
{
    // F3D 3.5.0 readers (see `f3d --list-readers`) minus formats owned by the image
    // pipeline (.tif, .png, .exr, .ptx Pentax RAW, ...) and names too generic to claim
    // (.e, .g, .bin, .nc G-code, .cdf, .gml, .210).
    private static readonly IReadOnlyCollection<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Meshes and scenes
            ".stl", ".ply", ".obj", ".gltf", ".glb", ".3ds", ".wrl", ".vrml", ".mdl",
            ".fbx", ".dae", ".dxf", ".off", ".x", ".3mf", ".drc", ".abc",
            ".usd", ".usda", ".usdc", ".usdz",
            // CAD and BIM
            ".step", ".stp", ".stpnc", ".p21", ".iges", ".igs", ".brep", ".xbf", ".ifc",
            // VTK and simulation data
            ".vtk", ".vtp", ".vtu", ".vtr", ".vts", ".vtm", ".vtkhdf", ".exo", ".ex2", ".ncdf",
            // Volumes
            ".vti", ".mha", ".mhd", ".nrrd", ".nhdr", ".dcm", ".vdb",
            // Point clouds and Gaussian splats
            ".pts", ".las", ".laz", ".pcd", ".bpf", ".slpk", ".splat", ".spz"
        };

    /// <summary>
    /// Options the F3D application applies per format from its config.d files; libf3d does
    /// not, so the viewer applies them itself. Up-axis and camera overrides are left out to
    /// keep the viewer's Y-up turntable orbit.
    /// </summary>
    internal static readonly IReadOnlyList<string> FormatOptionNames =
    [
        "model.scivis.enable", "model.scivis.array_name", "model.scivis.component", "model.scivis.cells",
        "model.volume.enable", "model.volume.inverse",
        "model.point_sprites.enable", "model.point_sprites.type", "model.point_sprites.size",
        "model.point_sprites.absolute_size",
        "render.point_size", "render.effect.blending.enable", "render.effect.blending.mode", "ui.scalar_bar"
    ];

    private static readonly (string Name, string Value)[] ScalarColoring = [("model.scivis.enable", "true")];
    private static readonly (string Name, string Value)[] ScientificData =
        [("model.scivis.enable", "true"), ("ui.scalar_bar", "true")];
    private static readonly (string Name, string Value)[] Volume =
        [("model.scivis.enable", "true"), ("ui.scalar_bar", "true"), ("model.volume.enable", "true")];
    private static readonly (string Name, string Value)[] DirectColors =
        [("model.scivis.enable", "true"), ("model.scivis.component", "-2")];
    private static readonly (string Name, string Value)[] CadColors =
        [("model.scivis.enable", "true"), ("model.scivis.component", "-2"), ("model.scivis.cells", "true")];
    private static readonly (string Name, string Value)[] LidarPoints =
    [
        ("model.scivis.enable", "true"), ("model.scivis.array_name", "NormalizedColor"),
        ("model.scivis.component", "-2"), ("render.point_size", "5")
    ];
    private static readonly (string Name, string Value)[] GaussianSplats =
    [
        ("model.point_sprites.enable", "true"), ("model.point_sprites.type", "gaussian"),
        ("model.point_sprites.absolute_size", "true"), ("model.point_sprites.size", "1"),
        ("model.scivis.enable", "true"), ("model.scivis.array_name", "color"), ("model.scivis.component", "-2"),
        ("render.effect.blending.enable", "true"), ("render.effect.blending.mode", "sort")
    ];

    private static readonly IReadOnlyDictionary<string, (string Name, string Value)[]> FormatOptions =
        new Dictionary<string, (string Name, string Value)[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".stl"] = ScalarColoring,
            [".pts"] = ScalarColoring,
            [".drc"] = ScalarColoring,
            [".ply"] = DirectColors,
            [".vtp"] = ScientificData,
            [".vtu"] = ScientificData,
            [".vtr"] = ScientificData,
            [".vts"] = ScientificData,
            [".vtm"] = ScientificData,
            [".vtkhdf"] = ScientificData,
            [".exo"] = ScientificData,
            [".ex2"] = ScientificData,
            [".ncdf"] = ScientificData,
            [".vti"] = Volume,
            [".mha"] = Volume,
            [".mhd"] = Volume,
            [".nrrd"] = Volume,
            [".nhdr"] = Volume,
            [".dcm"] = Volume,
            [".vdb"] = [("model.volume.enable", "true"), ("model.volume.inverse", "true")],
            [".step"] = CadColors,
            [".stp"] = CadColors,
            [".stpnc"] = CadColors,
            [".p21"] = CadColors,
            [".iges"] = CadColors,
            [".igs"] = CadColors,
            [".brep"] = CadColors,
            [".xbf"] = CadColors,
            [".ifc"] = [.. CadColors, ("model.scivis.array_name", "Colors")],
            [".las"] = LidarPoints,
            [".laz"] = LidarPoints,
            [".pcd"] = LidarPoints,
            [".bpf"] = LidarPoints,
            [".slpk"] = [("render.point_size", "5")],
            [".splat"] = GaussianSplats,
            [".spz"] = GaussianSplats
        };

    public static IReadOnlyCollection<string> Supported => Extensions;

    public static bool IsSupported(string path)
    {
        return Extensions.Contains(Path.GetExtension(path));
    }

    public static IReadOnlyList<(string Name, string Value)> GetFormatOptions(string path)
    {
        return FormatOptions.TryGetValue(Path.GetExtension(path), out var options) ? options : [];
    }
}
