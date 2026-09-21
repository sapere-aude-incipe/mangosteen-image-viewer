using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mangosteen.Printing;

internal static class PhotoPrintService
{
    internal static bool CanPrintOriginal(string path, int quarterTurns, int frameCount) =>
        quarterTurns % 4 == 0 && frameCount == 1 &&
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".bmp" or ".png" or ".tif" or ".tiff" or ".gif";

    internal static string CreatePrintCopy(BitmapSource source, int quarterTurns, string? directory = null)
    {
        directory ??= Path.Combine(Path.GetTempPath(), "Mangosteen", "Printing");
        Directory.CreateDirectory(directory);
        RemoveExpiredCopies(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
        var turns = ((quarterTurns % 4) + 4) % 4;
        var rotated = turns == 0 ? source : new TransformedBitmap(source, new RotateTransform(turns * 90));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rotated));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
        return path;
    }

    internal static void Show(string path)
    {
        var type = Type.GetTypeFromProgID("WIA.CommonDialog", throwOnError: true)!;
        var dialog = Activator.CreateInstance(type)!;
        try
        {
            // Explicitly invoke the Windows photo wizard, independent of the file's default app.
            // https://learn.microsoft.com/previous-versions/windows/desktop/wiaaut/-wiaaut-icommondialog-showphotoprintingwizard
            ((dynamic)dialog).ShowPhotoPrintingWizard(Path.GetFullPath(path));
        }
        catch (COMException ex) when (ex.HResult is unchecked((int)0x800704C7) or unchecked((int)0x80004004))
        {
            // Cancelling the wizard is not a printing failure.
        }
        finally
        {
            Marshal.FinalReleaseComObject(dialog);
        }
    }

    private static void RemoveExpiredCopies(string directory)
    {
        // The Windows wizard can outlive this call. Keep its input available and expire only old owned copies.
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) ||
                    File.GetLastWriteTimeUtc(path) >= DateTime.UtcNow.AddDays(-7)) continue;
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
