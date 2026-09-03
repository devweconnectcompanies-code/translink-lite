namespace TransLink.Lite.Application.RealtimeAudio;

public static class RealtimeTranscriptionLanguageCatalog
{
    private static readonly Dictionary<string, string> SupportedCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = "en-US",
            ["es-US"] = "es-US",
        };

    public static bool TryNormalize(string? languageCode, out string normalized)
    {
        if (languageCode is not null &&
            SupportedCodes.TryGetValue(languageCode.Trim(), out var canonical))
        {
            normalized = canonical;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static bool IsSupported(string? languageCode) =>
        TryNormalize(languageCode, out _);
}
