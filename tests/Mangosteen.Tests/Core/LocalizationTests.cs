using Mangosteen.Localization;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    [TestMethod]
    public void Localized_Values_Are_Not_Double_Encoded_Utf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        foreach (var cultureName in LocalizedText.SupportedCultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var key in LocalizedText.Keys)
            {
                var value = LocalizedText.Get(key, culture);
                // UTF-8 bytes misread as Windows-1252 ("Ã¼" for "ü") re-encode to valid UTF-8.
                foreach (Match run in Regex.Matches(value, "[\u0080-\u00FF\u0152-\u0192\u02C6-\u02DC\u2013-\u2122]{2,}"))
                {
                    string? repaired = null;
                    try
                    {
                        repaired = strictUtf8.GetString(windows1252.GetBytes(run.Value));
                    }
                    catch (Exception ex) when (ex is EncoderFallbackException or DecoderFallbackException or ArgumentException)
                    {
                    }

                    Assert.IsTrue(
                        repaired is null || repaired == run.Value,
                        $"Double-encoded UTF-8 in '{key}' for '{cultureName}': {value}");
                }
            }
        }
    }

    [TestMethod]
    public void Format_Model_Statistics_Uses_Grouped_Counts()
    {
        var english = CultureInfo.GetCultureInfo("en");

        Assert.AreEqual(
            "3,210,620 vertices · 6,421,238 faces",
            MainWindow.FormatModelStatistics(new(3_210_620, 6_421_238), english));
        Assert.AreEqual("12 faces", MainWindow.FormatModelStatistics(new(null, 12), english));
        Assert.IsNull(MainWindow.FormatModelStatistics(null, english));
    }

    private static bool IsSuspiciousResourceCharacter(char value)
    {
        return value is >= '\u0080' and <= '\u009F' or '\uFFFD';
    }
}
