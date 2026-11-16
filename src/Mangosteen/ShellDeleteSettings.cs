using System.Runtime.InteropServices;

namespace Mangosteen;

/// <summary>
/// Reads whether Windows itself asks before moving a file to the Recycle Bin (the Recycle
/// Bin's "Display delete confirmation dialog" setting, off by default since Windows 8).
/// </summary>
internal static class ShellDeleteSettings
{
    private const uint SsfNoConfirmRecycle = 0x00008000;
    // SHELLFLAGSTATE bit field: fShowAllObjects, fShowExtensions, fNoConfirmRecycle, ...
    private const uint NoConfirmRecycleBit = 1u << 2;

    public static bool WindowsConfirmsRecycle()
    {
        try
        {
            uint state = 0;
            SHGetSettings(ref state, SsfNoConfirmRecycle);
            return WindowsConfirmsRecycle(state);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Unknown: ask in Mangosteen, since a second prompt beats none.
            return false;
        }
    }

    internal static bool WindowsConfirmsRecycle(uint shellFlagState) => (shellFlagState & NoConfirmRecycleBit) == 0;

    [DllImport("shell32.dll")]
    private static extern void SHGetSettings(ref uint state, uint mask);
}
