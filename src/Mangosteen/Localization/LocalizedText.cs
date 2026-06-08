using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Mangosteen.Localization;

internal static class LocalizedText
{
    public const string AllFiles = nameof(AllFiles);
    public const string Aggressive = nameof(Aggressive);
    public const string AppTitle = nameof(AppTitle);
    public const string Balanced = nameof(Balanced);
    public const string Conservative = nameof(Conservative);
    public const string DeleteImage = nameof(DeleteImage);
    public const string DeleteImageConfirmationFormat = nameof(DeleteImageConfirmationFormat);
    public const string Exit = nameof(Exit);
    public const string FileNotFoundFormat = nameof(FileNotFoundFormat);
    public const string FileMenu = nameof(FileMenu);
    public const string FullResolutionDecodeFailed = nameof(FullResolutionDecodeFailed);
    public const string FullResolutionDecodeFailedFormat = nameof(FullResolutionDecodeFailedFormat);
    public const string ImageFiles = nameof(ImageFiles);
    public const string Loading = nameof(Loading);
    public const string Manual = nameof(Manual);
    public const string Nearest = nameof(Nearest);
    public const string NextImage = nameof(NextImage);
    public const string NoImage = nameof(NoImage);
    public const string OpenCommand = nameof(OpenCommand);
    public const string OpenImage = nameof(OpenImage);
    public const string OptionsMenu = nameof(OptionsMenu);
    public const string Preload = nameof(Preload);
    public const string PreloadAggressiveness = nameof(PreloadAggressiveness);
    public const string PreloadMemoryBudget = nameof(PreloadMemoryBudget);
    public const string PreloadMemoryBudgetTooltip = nameof(PreloadMemoryBudgetTooltip);
    public const string PreloadNearbyImages = nameof(PreloadNearbyImages);
    public const string PreviewOnly = nameof(PreviewOnly);
    public const string PreviousImage = nameof(PreviousImage);
    public const string Smooth = nameof(Smooth);
    public const string ToggleActualPixels = nameof(ToggleActualPixels);
    public const string ToggleDarkMode = nameof(ToggleDarkMode);
    public const string ToggleSmoothNearestUpscaling = nameof(ToggleSmoothNearestUpscaling);
    public const string UnexpectedError = nameof(UnexpectedError);
    public const string Upscaling = nameof(Upscaling);
    public const string UseDarkMode = nameof(UseDarkMode);
    public const string UseLightMode = nameof(UseLightMode);
    public const string Zoom = nameof(Zoom);

    private static readonly ResourceManager Resources = new(
        "Mangosteen.Localization.Strings",
        Assembly.GetExecutingAssembly());

    public static readonly string[] SupportedCultureNames =
    [
        "en",
        "nb-NO",
        "de",
        "fr",
        "es",
        "pt-BR",
        "pl",
        "tr",
        "ja",
        "ko",
        "zh-Hans",
        "zh-Hant",
        "ru"
    ];

    public static readonly string[] Keys =
    [
        AllFiles,
        Aggressive,
        AppTitle,
        Balanced,
        Conservative,
        DeleteImage,
        DeleteImageConfirmationFormat,
        Exit,
        FileNotFoundFormat,
        FileMenu,
        FullResolutionDecodeFailed,
        FullResolutionDecodeFailedFormat,
        ImageFiles,
        Loading,
        Manual,
        Nearest,
        NextImage,
        NoImage,
        OpenCommand,
        OpenImage,
        OptionsMenu,
        Preload,
        PreloadAggressiveness,
        PreloadMemoryBudget,
        PreloadMemoryBudgetTooltip,
        PreloadNearbyImages,
        PreviewOnly,
        PreviousImage,
        Smooth,
        ToggleActualPixels,
        ToggleDarkMode,
        ToggleSmoothNearestUpscaling,
        UnexpectedError,
        Upscaling,
        UseDarkMode,
        UseLightMode,
        Zoom
    ];

    public static string Get(string key)
    {
        return Get(key, CultureInfo.CurrentUICulture);
    }

    public static string Get(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);

        return Resources.GetString(key, ResolveCulture(culture))
            ?? Resources.GetString(key, CultureInfo.InvariantCulture)
            ?? key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentUICulture, Get(key), args);
    }

    internal static bool HasDefinedValue(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);

        culture = ResolveCulture(culture);
        var resourceCulture = culture.Name.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.InvariantCulture
            : culture;
        return Resources.GetResourceSet(resourceCulture, createIfNotExists: true, tryParents: false)
            ?.GetString(key) is { Length: > 0 };
    }

    private static CultureInfo ResolveCulture(CultureInfo culture)
    {
        if (culture.Name.Equals("nb", StringComparison.OrdinalIgnoreCase) ||
            culture.Name.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("nb-NO");
        }

        return culture;
    }
}
