using ClassicPhotoViewer.Localization;
using System.Globalization;

namespace ClassicPhotoViewer.Tests.Core;

[TestClass]
public sealed class LocalizationTests
{
    [TestMethod]
    public void All_Supported_Cultures_Define_All_Localized_Keys()
    {
        foreach (var cultureName in LocalizedText.SupportedCultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);

            foreach (var key in LocalizedText.Keys)
            {
                var value = LocalizedText.Get(key, culture);

                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(value),
                    $"Missing localized value for '{key}' in '{cultureName}'.");
                Assert.IsTrue(
                    LocalizedText.HasDefinedValue(key, culture),
                    $"Resource file does not define '{key}' in '{cultureName}'.");
            }
        }
    }

    [TestMethod]
    public void Non_English_Culture_Uses_Culture_Specific_Resource()
    {
        var german = CultureInfo.GetCultureInfo("de");

        Assert.AreEqual("Kein Bild", LocalizedText.Get(LocalizedText.NoImage, german));
    }

    [TestMethod]
    public void Neutral_Norwegian_Culture_Uses_Bokmal_Resource()
    {
        var norwegian = CultureInfo.GetCultureInfo("nb");

        Assert.AreEqual("Ingen bilder", LocalizedText.Get(LocalizedText.NoImage, norwegian));
    }

    [TestMethod]
    public void Format_Uses_Localized_Template()
    {
        var norwegian = CultureInfo.GetCultureInfo("nb-NO");

        Assert.AreEqual(
            "Finner ikke filen: C:\\missing.jpg",
            string.Format(
                norwegian,
                LocalizedText.Get(LocalizedText.FileNotFoundFormat, norwegian),
                "C:\\missing.jpg"));
    }
}
