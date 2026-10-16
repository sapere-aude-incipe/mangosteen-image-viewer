using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("true")]
    [DataRow("42")]
    [DataRow("\"settings\"")]
    [DataRow("{\"IsDarkMode\":\"yes\"}")]
    public void Invalid_Settings_Shape_Falls_Back_To_Defaults(string json)
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(path, json);
            Assert.IsTrue(AppSettings.Load(path).IsDarkMode);
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void Failed_Replacement_Preserves_Settings_And_Removes_Temporary_File()
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            new AppSettings { IsDarkMode = false }.Save(path);
            var original = File.ReadAllBytes(path);
            using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                new AppSettings { IsDarkMode = true }.Save(path);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                Assert.HasCount(1, Directory.GetFiles(directory.FullName));
            }
            new AppSettings { IsDarkMode = true }.Save(path);
            Assert.IsTrue(AppSettings.Load(path).IsDarkMode);
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void Load_Returns_Defaults_When_Settings_File_Is_Missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        var settings = AppSettings.Load(path);

        Assert.IsTrue(settings.IsDarkMode);
        Assert.IsTrue(settings.UseSmoothSampling);
        Assert.IsTrue(settings.IsPreloadEnabled);
        Assert.IsFalse(settings.IsAutoRefreshEnabled);
        Assert.IsTrue(settings.KeepReadyInBackground);
        Assert.IsFalse(settings.IsModelGridAtWorldOrigin);
        Assert.AreEqual(2, settings.PreloadBudgetGigabytes);
        Assert.AreEqual(PreloadAggressiveness.Balanced, settings.PreloadAggressiveness);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Save_And_Load_Round_Trips_Settings(bool gridAtWorldOrigin)
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            var expected = new AppSettings
            {
                IsDarkMode = false,
                UseSmoothSampling = false,
                IsPreloadEnabled = false,
                IsAutoRefreshEnabled = true,
                KeepReadyInBackground = false,
                IsModelGridAtWorldOrigin = gridAtWorldOrigin,
                PreloadBudgetGigabytes = 10,
                PreloadAggressiveness = PreloadAggressiveness.Aggressive
            };

            expected.Save(path);
            var actual = AppSettings.Load(path);

            Assert.IsFalse(actual.IsDarkMode);
            Assert.IsFalse(actual.UseSmoothSampling);
            Assert.IsFalse(actual.IsPreloadEnabled);
            Assert.IsTrue(actual.IsAutoRefreshEnabled);
            Assert.IsFalse(actual.KeepReadyInBackground);
            Assert.AreEqual(gridAtWorldOrigin, actual.IsModelGridAtWorldOrigin);
            Assert.AreEqual(10, actual.PreloadBudgetGigabytes);
            Assert.AreEqual(PreloadAggressiveness.Aggressive, actual.PreloadAggressiveness);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Load_Returns_Defaults_For_Corrupt_Settings_File()
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(path, "{not valid json");

            var settings = AppSettings.Load(path);

            Assert.IsTrue(settings.IsDarkMode);
            Assert.IsTrue(settings.UseSmoothSampling);
            Assert.IsTrue(settings.IsPreloadEnabled);
            Assert.IsFalse(settings.IsAutoRefreshEnabled);
            Assert.IsTrue(settings.KeepReadyInBackground);
            Assert.AreEqual(2, settings.PreloadBudgetGigabytes);
            Assert.AreEqual(PreloadAggressiveness.Balanced, settings.PreloadAggressiveness);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Load_Normalizes_Out_Of_Range_Values()
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(
                path,
                """
                {
                  "PreloadBudgetGigabytes": 99,
                  "PreloadAggressiveness": 999
                }
                """);

            var settings = AppSettings.Load(path);

            Assert.AreEqual(15, settings.PreloadBudgetGigabytes);
            Assert.AreEqual(PreloadAggressiveness.Balanced, settings.PreloadAggressiveness);
            Assert.IsTrue(settings.KeepReadyInBackground);
            Assert.IsFalse(settings.IsModelGridAtWorldOrigin, "Older settings must default to the beneath-model grid.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
