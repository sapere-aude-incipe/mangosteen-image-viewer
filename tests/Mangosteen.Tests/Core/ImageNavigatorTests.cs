using Mangosteen.Navigation;
using Mangosteen.Decoding;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ImageNavigatorTests
{
    [TestMethod]
    public void LoadSingle_Sets_Current_File_Without_Enabling_Folder_Navigation()
    {
        var path = Path.Combine(Path.GetTempPath(), "photo1.jpg");
        var nav = new ImageNavigator();

        nav.LoadSingle(path);

        Assert.AreEqual(Path.GetFullPath(path), nav.CurrentPath);
        Assert.AreEqual(0, nav.CurrentIndex);
        Assert.IsFalse(nav.CanMovePrevious);
        Assert.IsFalse(nav.CanMoveNext);
    }

    [TestMethod]
    public void Navigation_Wraps_At_Folder_Edges()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-nav-");
        try
        {
            var first = Path.Combine(dir.FullName, "photo1.jpg");
            var second = Path.Combine(dir.FullName, "photo2.jpg");
            File.WriteAllText(first, "");
            File.WriteAllText(second, "");

            var nav = new ImageNavigator();
            nav.LoadFolderFor(first, [".jpg"]);

            Assert.IsTrue(nav.CanMovePrevious);
            Assert.IsTrue(nav.CanMoveNext);

            nav.MovePrevious();
            Assert.AreEqual(second, nav.CurrentPath);

            nav.MoveNext();
            Assert.AreEqual(first, nav.CurrentPath);

            nav.MoveNext();
            Assert.AreEqual(second, nav.CurrentPath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Apply_Uses_Background_Scan_Snapshot_For_Folder_Navigation()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-snapshot-");
        try
        {
            var first = Path.Combine(dir.FullName, "photo1.jpg");
            var second = Path.Combine(dir.FullName, "photo2.jpg");
            File.WriteAllText(first, "");
            File.WriteAllText(second, "");

            var nav = new ImageNavigator();
            nav.LoadSingle(first);

            var snapshot = ImageNavigator.ScanFolderFor(first, [".jpg"]);
            nav.Apply(snapshot);

            Assert.AreEqual(first, nav.CurrentPath);
            Assert.IsTrue(nav.CanMovePrevious);
            Assert.IsTrue(nav.CanMoveNext);
            nav.MoveNext();
            Assert.AreEqual(second, nav.CurrentPath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Apply_Invalid_Current_Index_Disables_Navigation()
    {
        var first = Path.Combine(Path.GetTempPath(), "classic-viewer-invalid-1.jpg");
        var second = Path.Combine(Path.GetTempPath(), "classic-viewer-invalid-2.jpg");
        var nav = new ImageNavigator();

        nav.Apply(new ImageFolderSnapshot([first, second], -1));

        Assert.AreEqual(-1, nav.CurrentIndex);
        Assert.IsNull(nav.CurrentPath);
        Assert.IsFalse(nav.CanMovePrevious);
        Assert.IsFalse(nav.CanMoveNext);
        Assert.IsNull(nav.MovePrevious());
        Assert.IsNull(nav.MoveNext());
        Assert.AreEqual(-1, nav.CurrentIndex);
        Assert.IsEmpty(nav.GetAdjacentPaths());
        Assert.IsEmpty(nav.GetLookaroundPaths(forwardCount: 2, backwardCount: 2));
    }

    [TestMethod]
    public void Clear_Removes_Current_File_And_Disables_Navigation()
    {
        var path = Path.Combine(Path.GetTempPath(), "classic-viewer-clear.jpg");
        var nav = new ImageNavigator();
        nav.LoadSingle(path);

        nav.Clear();

        Assert.IsEmpty(nav.Files);
        Assert.AreEqual(-1, nav.CurrentIndex);
        Assert.IsNull(nav.CurrentPath);
        Assert.IsFalse(nav.CanMovePrevious);
        Assert.IsFalse(nav.CanMoveNext);
        Assert.IsNull(nav.MovePrevious());
        Assert.IsNull(nav.MoveNext());
    }

    [TestMethod]
    public void RemoveCurrent_Selects_Next_Image()
    {
        var first = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "classic-viewer-remove-1.jpg"));
        var second = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "classic-viewer-remove-2.jpg"));
        var third = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "classic-viewer-remove-3.jpg"));
        var nav = new ImageNavigator();
        nav.Apply(new ImageFolderSnapshot([first, second, third], 1));

        var selected = nav.RemoveCurrent();

        Assert.AreEqual(third, selected);
        Assert.AreEqual(third, nav.CurrentPath);
        CollectionAssert.AreEqual(new[] { first, third }, nav.Files.ToArray());
    }

    [TestMethod]
    public void RemoveCurrent_Wraps_To_First_When_Removing_Last_Image()
    {
        var first = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "classic-viewer-remove-last-1.jpg"));
        var second = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "classic-viewer-remove-last-2.jpg"));
        var nav = new ImageNavigator();
        nav.Apply(new ImageFolderSnapshot([first, second], 1));

        var selected = nav.RemoveCurrent();

        Assert.AreEqual(first, selected);
        Assert.AreEqual(first, nav.CurrentPath);
        Assert.AreEqual(0, nav.CurrentIndex);
        CollectionAssert.AreEqual(new[] { first }, nav.Files.ToArray());
    }

    [TestMethod]
    public void RemoveCurrent_Clears_When_Removing_Only_Image()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "classic-viewer-remove-only.jpg"));
        var nav = new ImageNavigator();
        nav.LoadSingle(path);

        var selected = nav.RemoveCurrent();

        Assert.IsNull(selected);
        Assert.IsNull(nav.CurrentPath);
        Assert.AreEqual(-1, nav.CurrentIndex);
        Assert.IsEmpty(nav.Files);
        Assert.IsFalse(nav.CanMovePrevious);
        Assert.IsFalse(nav.CanMoveNext);
    }

    [TestMethod]
    public void LoadFolderFor_Normalizes_Dotless_Supported_Extensions()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-dotless-ext-");
        try
        {
            var first = Path.Combine(dir.FullName, "photo1.jpg");
            var second = Path.Combine(dir.FullName, "photo2.jpg");
            File.WriteAllText(first, "");
            File.WriteAllText(second, "");

            var nav = new ImageNavigator();
            nav.LoadFolderFor(first, ["jpg"]);

            Assert.HasCount(2, nav.Files);
            nav.MoveNext();
            Assert.AreEqual(second, nav.CurrentPath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Public_Methods_Reject_Invalid_Inputs()
    {
        var nav = new ImageNavigator();

        Assert.ThrowsExactly<ArgumentException>(() => nav.LoadSingle(""));
        Assert.ThrowsExactly<ArgumentNullException>(() => nav.LoadFolderFor("photo.jpg", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => nav.Apply(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => nav.Apply(new ImageFolderSnapshot(null!, 0)));
        Assert.ThrowsExactly<ArgumentException>(() => nav.Apply(new ImageFolderSnapshot([" "], 0)));
        Assert.ThrowsExactly<ArgumentException>(() => ImageNavigator.ScanFolderFor(" ", [".jpg"]));
        Assert.ThrowsExactly<ArgumentNullException>(() => ImageNavigator.ScanFolderFor("photo.jpg", null!));
    }

    [TestMethod]
    public void Lookaround_Prioritizes_Forward_Then_Backward_Without_Duplicates()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-lookaround-");
        try
        {
            for (var i = 1; i <= 6; i++)
            {
                File.WriteAllText(Path.Combine(dir.FullName, $"photo{i}.jpg"), "");
            }

            var current = Path.Combine(dir.FullName, "photo3.jpg");
            var nav = new ImageNavigator();
            nav.LoadFolderFor(current, [".jpg"]);

            var paths = nav.GetLookaroundPaths(forwardCount: 4, backwardCount: 2)
                .Select(Path.GetFileName)
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "photo4.jpg", "photo2.jpg", "photo5.jpg", "photo6.jpg", "photo1.jpg" },
                paths);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Lookaround_Treats_Negative_Counts_As_Zero()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-lookaround-negative-");
        try
        {
            for (var i = 1; i <= 3; i++)
            {
                File.WriteAllText(Path.Combine(dir.FullName, $"photo{i}.jpg"), "");
            }

            var current = Path.Combine(dir.FullName, "photo2.jpg");
            var nav = new ImageNavigator();
            nav.LoadFolderFor(current, [".jpg"]);

            var paths = nav.GetLookaroundPaths(forwardCount: -1, backwardCount: 1)
                .Select(Path.GetFileName)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "photo1.jpg" }, paths);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Lookaround_Returns_Empty_When_Counts_Are_Zero()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-lookaround-zero-");
        try
        {
            for (var i = 1; i <= 3; i++)
            {
                File.WriteAllText(Path.Combine(dir.FullName, $"photo{i}.jpg"), "");
            }

            var nav = new ImageNavigator();
            nav.LoadFolderFor(Path.Combine(dir.FullName, "photo2.jpg"), [".jpg"]);

            var paths = nav.GetLookaroundPaths(forwardCount: 0, backwardCount: 0);

            Assert.IsEmpty(paths);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ScanFolderFor_Does_Not_Include_Extensionless_Siblings_For_Extensionless_Explicit_Open()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-extensionless-");
        try
        {
            var current = Path.Combine(dir.FullName, "current");
            var sibling = Path.Combine(dir.FullName, "sibling");
            var known = Path.Combine(dir.FullName, "photo.jpg");
            File.WriteAllText(current, "");
            File.WriteAllText(sibling, "");
            File.WriteAllText(known, "");

            var extensions = ImageFileExtensions.BuildFolderScanExtensions([".jpg"], current);
            var snapshot = ImageNavigator.ScanFolderFor(current, extensions);
            var names = snapshot.Files.Select(Path.GetFileName).ToArray();

            CollectionAssert.Contains(names, "current");
            CollectionAssert.Contains(names, "photo.jpg");
            CollectionAssert.DoesNotContain(names, "sibling");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ScanFolderFor_Caps_Folder_Image_Count()
    {
        var dir = Directory.CreateTempSubdirectory("classic-viewer-scan-cap-");
        try
        {
            var current = Path.Combine(dir.FullName, "photo00000.jpg");
            for (var i = 0; i < ImageNavigator.MaxImageFolderFiles + 5; i++)
            {
                File.WriteAllText(Path.Combine(dir.FullName, $"photo{i:00000}.jpg"), "");
            }

            var snapshot = ImageNavigator.ScanFolderFor(current, [".jpg"]);

            Assert.IsLessThanOrEqualTo(snapshot.Files.Count, ImageNavigator.MaxImageFolderFiles);
            CollectionAssert.Contains(snapshot.Files.ToList(), current);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
