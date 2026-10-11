using System.IO;
using System.Runtime.InteropServices;

namespace Mangosteen.Shell;

internal static class OpenWithService
{
    internal const uint ExecuteFile = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string File;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Class;
        public uint Flags;
    }

    internal delegate int ShowDialog(IntPtr owner, ref OpenAsInfo info);

    internal static void Show(string path, IntPtr owner, ShowDialog? showDialog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new OpenAsInfo
        {
            File = Path.GetFullPath(path),
            Class = null,
            // Without OAIF_EXEC, Windows 10+ shows a Settings notice instead of the app chooser.
            Flags = ExecuteFile
        };
        var result = (showDialog ?? SHOpenWithDialog)(owner, ref info);
        if (result is unchecked((int)0x800704C7) or unchecked((int)0x80004004)) return;
        Marshal.ThrowExceptionForHR(result);
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);
}
