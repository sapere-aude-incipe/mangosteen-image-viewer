using Mangosteen.Localization;
using System.Globalization;

namespace Mangosteen.Tests.Core;

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

        Assert.AreEqual(
            "Bild hier ablegen oder klicken zum Öffnen",
            LocalizedText.Get(LocalizedText.NoImage, german));
    }

    [TestMethod]
    public void Localized_Values_Do_Not_Contain_Mojibake_Control_Characters()
    {
        foreach (var cultureName in LocalizedText.SupportedCultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var key in LocalizedText.Keys)
            {
                var value = LocalizedText.Get(key, culture);

                Assert.IsFalse(
                    value.Any(IsSuspiciousResourceCharacter),
                    $"Suspicious mojibake/control character in '{key}' for '{cultureName}': {value}");
            }
        }
    }

    [TestMethod]
    public void Neutral_Norwegian_Culture_Uses_Bokmal_Resource()
    {
        var norwegian = CultureInfo.GetCultureInfo("nb");

        Assert.AreEqual(
            "Dra og slipp eller klikk for å åpne",
            LocalizedText.Get(LocalizedText.NoImage, norwegian));
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

    private static bool IsSuspiciousResourceCharacter(char value)
    {
        return value is >= '\u0080' and <= '\u009F' or '\uFFFD';
    }
}
