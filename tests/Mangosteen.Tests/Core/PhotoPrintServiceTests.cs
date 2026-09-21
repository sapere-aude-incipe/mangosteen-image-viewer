using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mangosteen.Printing;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class PhotoPrintServiceTests
{
    [TestMethod]
    [DataRow("photo.JPG", 0, 1, true)]
    [DataRow("photo.jpeg", 4, 1, true)]
    [DataRow("photo.tiff", 0, 1, true)]
    [DataRow("photo.png", 1, 1, false)]
    [DataRow("photo.gif", 0, 3, false)]
    [DataRow("photo.webp", 0, 1, false)]
    [DataRow("photo.psd", 0, 1, false)]
    public void Native_Photos_Keep_The_Original_Resolution_And_Other_Content_Uses_A_Copy(
        string path, int turns, int frames, bool expected)
    {
        Assert.AreEqual(expected, PhotoPrintService.CanPrintOriginal(path, turns, frames));
    }

    [TestMethod]
    [DataRow(0, 2, 3)]
    [DataRow(1, 3, 2)]
    [DataRow(-1, 3, 2)]
    [DataRow(2, 2, 3)]
    public Task Print_Copy_Is_A_Rotated_Png_And_Does_Not_Change_The_Source(int turns, int width, int height) => StaTest.RunAsync(() =>
    {
        var directory = Directory.CreateTempSubdirectory("Mangosteen-print-test-");
        try
        {
            byte[] pixels = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 255, 255, 255, 255, 255, 0, 255, 255, 0, 255, 255];
            var source = BitmapSource.Create(2, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            source.Freeze();
            var path = PhotoPrintService.CreatePrintCopy(source, turns, directory.FullName);
            using var file = File.OpenRead(path);
            var decoded = new PngBitmapDecoder(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.AreEqual(width, decoded.PixelWidth);
            Assert.AreEqual(height, decoded.PixelHeight);
            var original = new byte[pixels.Length];
            source.CopyPixels(original, 8, 0);
            CollectionAssert.AreEqual(pixels, original);
            Assert.AreEqual(2, source.PixelWidth);
            Assert.AreEqual(3, source.PixelHeight);
        }
        finally { directory.Delete(recursive: true); }
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Print_Copies_Are_Unique_And_Only_Expired_Owned_Files_Are_Removed() => StaTest.RunAsync(() =>
    {
        var directory = Directory.CreateTempSubdirectory("Mangosteen-print-test-");
        try
        {
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            var oldCopy = PhotoPrintService.CreatePrintCopy(source, 0, directory.FullName);
            File.SetLastWriteTimeUtc(oldCopy, DateTime.UtcNow.AddDays(-8));
            var unrelated = Path.Combine(directory.FullName, "unrelated.png");
            File.WriteAllBytes(unrelated, [1]);
            File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-8));
            var first = PhotoPrintService.CreatePrintCopy(source, 0, directory.FullName);
            var second = PhotoPrintService.CreatePrintCopy(source, 0, directory.FullName);
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(File.Exists(first));
            Assert.IsTrue(File.Exists(second));
            Assert.IsTrue(File.Exists(unrelated));
            Assert.IsFalse(File.Exists(oldCopy));
        }
        finally { directory.Delete(recursive: true); }
        return Task.CompletedTask;
    });
}
