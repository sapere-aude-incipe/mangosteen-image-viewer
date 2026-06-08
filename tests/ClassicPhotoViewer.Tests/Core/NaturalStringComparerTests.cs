using ClassicPhotoViewer.Navigation;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class NaturalStringComparerTests
{
    [TestMethod]
    public void Sorts_Numbered_File_Names_Naturally()
    {
        string[] names = ["image10.jpg", "image2.jpg", "image1.jpg", "image02.jpg"];

        Array.Sort(names, NaturalStringComparer.Instance);

        CollectionAssert.AreEqual(new[] { "image1.jpg", "image2.jpg", "image02.jpg", "image10.jpg" }, names);
    }
}
