using Mangosteen;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void Load_Returns_Defaults_When_Settings_File_Is_Missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        var settings = AppSettings.Load(path);

        Assert.IsTrue(settings.IsDarkMode);
        Assert.IsTrue(settings.UseSmoothSampling);
        Assert.IsTrue(settings.IsPreloadEnabled);
        Assert.IsFalse(settings.IsAutoRefreshEnabled);
        Assert.AreEqual(2, settings.PreloadBudgetGigabytes);
        Assert.AreEqual(PreloadAggressiveness.Balanced, settings.PreloadAggressiveness);
    }

    [TestMethod]
    public void Save_And_Load_Round_Trips_Settings()
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
                PreloadBudgetGigabytes = 10,
                PreloadAggressiveness = PreloadAggressiveness.Aggressive
            };

            expected.Save(path);
            var actual = AppSettings.Load(path);

            Assert.IsFalse(actual.IsDarkMode);
            Assert.IsFalse(actual.UseSmoothSampling);
            Assert.IsFalse(actual.IsPreloadEnabled);
            Assert.IsTrue(actual.IsAutoRefreshEnabled);
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
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
