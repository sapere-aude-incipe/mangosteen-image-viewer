using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mangosteen.Rendering.Advanced;

internal readonly record struct GraphicsAdapter(string Name, uint VendorId, ulong DedicatedVideoMemory, bool IsSoftware);

/// <summary>
/// Lists the machine's graphics adapters through DXGI to decide whether GPU large-image
/// rendering should be on by default. Integrated GPUs report little or no dedicated video
/// memory (typically 128 MB or less); a dedicated card reports its own VRAM.
/// </summary>
internal static unsafe partial class GraphicsAdapters
{
    public const ulong DedicatedGpuMinimumVideoMemory = 512UL * 1024 * 1024;

    private const uint DxgiAdapterFlagSoftware = 2;
    private const uint MicrosoftVendorId = 0x1414;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private static readonly Guid DxgiFactory1Id = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Lazy<bool> HasDedicatedGpuValue = new(DetectDedicatedGpu);

    /// <summary>Cached for the process; adapters rarely change while the viewer runs.</summary>
    public static bool HasDedicatedGpu => HasDedicatedGpuValue.Value;

    internal static bool IsDedicated(GraphicsAdapter adapter)
    {
        // Microsoft's vendor id covers the Basic Render Driver and remote-display adapters.
        return !adapter.IsSoftware &&
            adapter.VendorId != MicrosoftVendorId &&
            adapter.DedicatedVideoMemory >= DedicatedGpuMinimumVideoMemory;
    }

    internal static IReadOnlyList<GraphicsAdapter> Enumerate()
    {
        var adapters = new List<GraphicsAdapter>();
        var factoryId = DxgiFactory1Id;
        if (CreateDXGIFactory1(&factoryId, out var factory) < 0 || factory == nint.Zero)
        {
            return adapters;
        }

        try
        {
            // IDXGIFactory1 vtable: IUnknown (0-2), IDXGIObject (3-6), IDXGIFactory (7-11), EnumAdapters1 (12).
            var enumAdapters1 = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)factory)[12];
            for (uint index = 0; ; index++)
            {
                nint adapter;
                var result = enumAdapters1(factory, index, &adapter);
                if (result == DxgiErrorNotFound || result < 0 || adapter == nint.Zero) break;
                try
                {
                    // IDXGIAdapter1 vtable: IUnknown (0-2), IDXGIObject (3-6), IDXGIAdapter (7-9), GetDesc1 (10).
                    var getDesc1 = (delegate* unmanaged<nint, AdapterDescription1*, int>)(*(nint**)adapter)[10];
                    AdapterDescription1 description;
                    if (getDesc1(adapter, &description) >= 0)
                    {
                        adapters.Add(new GraphicsAdapter(
                            new string(description.Description).TrimEnd('\0'),
                            description.VendorId,
                            (ulong)description.DedicatedVideoMemory,
                            (description.Flags & DxgiAdapterFlagSoftware) != 0));
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        return adapters;
    }

    private static bool DetectDedicatedGpu()
    {
        try
        {
            return Enumerate().Any(IsDedicated);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            Trace.WriteLine($"Graphics adapter detection failed: {ex}");
            return false;
        }
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* riid, out nint factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDescription1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }
}
