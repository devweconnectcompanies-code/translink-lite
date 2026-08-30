namespace TransLink.Lite.Application.Common.Languages;

public static class SupportedLanguageCatalog
{
    private static readonly HashSet<string> SupportedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "en",
        "es",
    };

    public static string Normalize(string languageCode) => languageCode.Trim().ToLowerInvariant();

    public static bool IsSupported(string languageCode) =>
        SupportedCodes.Contains(Normalize(languageCode));
}
