using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class RealtimeTranslationLanguageCatalogTests
{
    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("EN-us", "en")]
    [InlineData("es-US", "es")]
    [InlineData("es-ES", "es")]
    [InlineData("fr-FR", "fr")]
    public void TryMapSource_WithSupportedLocale_ReturnsProviderCode(
        string value,
        string expected)
    {
        Assert.True(RealtimeTranslationLanguageCatalog.TryMapSource(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData(" ES ", "es")]
    [InlineData("fr", "fr")]
    public void TryNormalizeTarget_WithSupportedCode_ReturnsCanonicalCode(
        string value,
        string expected)
    {
        Assert.True(RealtimeTranslationLanguageCatalog.TryNormalizeTarget(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de-DE")]
    [InlineData("auto")]
    public void UnsupportedValues_AreRejected(string? value)
    {
        Assert.False(RealtimeTranslationLanguageCatalog.TryMapSource(value, out _));
        Assert.False(RealtimeTranslationLanguageCatalog.TryNormalizeTarget(value, out _));
    }
}
