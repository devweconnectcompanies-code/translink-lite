namespace TransLink.Lite.Application.RealtimeAudio;

public static class RealtimeTranslationLanguageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> SourceMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = "en",
            ["es-US"] = "es",
            ["es-ES"] = "es",
            ["fr-FR"] = "fr",
        };

    private static readonly HashSet<string> Targets =
        new(["en", "es", "fr"], StringComparer.OrdinalIgnoreCase);

    public static bool TryMapSource(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return SourceMappings.TryGetValue(value.Trim(), out normalized!);
    }

    public static bool TryNormalizeTarget(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim().ToLowerInvariant();
        if (!Targets.Contains(candidate)) return false;
        normalized = candidate;
        return true;
    }
}
