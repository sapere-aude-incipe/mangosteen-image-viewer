using System.IO;
using System.Runtime.InteropServices;

namespace Mangosteen.Core;

internal static class WindowsShell
{
    public static bool TryOpenFolderAndSelectItem(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var result = SHParseDisplayName(
            Path.GetFullPath(path),
            bindContext: 0,
            out var itemIdList,
            attributesToQuery: 0,
            out _);
        if (result < 0 || itemIdList == 0)
        {
            return false;
        }

        try
        {
            // With no child array, the PIDL identifies the item whose parent
            // Explorer should open and whose row it should select.
            return SHOpenFolderAndSelectItems(
                itemIdList,
                itemCount: 0,
                childItemIdLists: 0,
                flags: 0) >= 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(itemIdList);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(
        string displayName,
        nint bindContext,
        out nint itemIdList,
        uint attributesToQuery,
        out uint attributes);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(
        nint folderItemIdList,
        uint itemCount,
        nint childItemIdLists,
        uint flags);
}
