using System.Runtime.InteropServices;

namespace Mangosteen.Rendering.Advanced;

/// <summary>Bytes read by the viewer's own code, so they can be told apart from F3D's reads.</summary>
internal sealed class ModelReadCounter
{
    private long _bytesRead;

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public void Add(long bytes) => Interlocked.Add(ref _bytesRead, bytes);
}

/// <summary>
/// Estimates F3D import progress. F3D's C API has no progress callback, but its readers
/// stream the model file while parsing, so the process's read counter advances with the
/// import (845 MB OBJ: an even ~42 MB/s for the whole parse). Reads by the viewer's own
/// statistics scan are subtracted. Formats that read everything up front reach the end
/// quickly and then show the preparing phase.
/// </summary>
internal readonly record struct ModelLoadProgress(long BytesRead, long TotalBytes)
{
    /// <summary>Below this a model loads too quickly for a progress bar to help.</summary>
    public const long MinimumFileBytes = 20L * 1024 * 1024;

    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesRead / TotalBytes, 0, 1);

    /// <summary>F3D has finished reading and is building and uploading the scene.</summary>
    public bool IsPreparing => Fraction >= 0.995;

    public static ModelLoadProgress Estimate(long processReadsAtStart, long processReadsNow, long ownReads, long fileBytes)
    {
        var importReads = processReadsNow - processReadsAtStart - ownReads;
        return new(Math.Clamp(importReads, 0, Math.Max(0, fileBytes)), fileBytes);
    }

    public static long? TryGetProcessBytesRead()
    {
        return GetProcessIoCounters(GetCurrentProcess(), out var counters) ? (long)counters.ReadTransferCount : null;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(nint process, out IoCounters counters);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
}
