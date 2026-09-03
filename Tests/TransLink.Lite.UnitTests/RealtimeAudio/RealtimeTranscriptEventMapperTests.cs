using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class RealtimeTranscriptEventMapperTests
{
    [Theory]
    [InlineData(true, "transcript.partial", false)]
    [InlineData(false, "transcript.final", true)]
    public void Map_UsesProviderFinalityToCreateGenericEvent(
        bool isPartial,
        string expectedType,
        bool expectedIsFinal)
    {
        var sessionId = Guid.NewGuid();
        var result = new SpeechTranscriptionResult(
            "result-1",
            "transient user content",
            isPartial,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(250),
            "en-US");

        var mapped = RealtimeTranscriptEventMapper.Map(sessionId, 7, result);

        Assert.Equal(expectedType, mapped.Type);
        Assert.Equal(expectedIsFinal, mapped.IsFinal);
        Assert.Equal(RealtimeAudioProtocol.CurrentVersion, mapped.ProtocolVersion);
        Assert.Equal(sessionId, mapped.SessionId);
        Assert.Equal(7, mapped.EventSequence);
        Assert.Equal(10, mapped.StartTimeMilliseconds);
        Assert.Equal(250, mapped.EndTimeMilliseconds);
    }

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("EN-us", "en-US")]
    [InlineData("es-US", "es-US")]
    public void LanguageCatalog_NormalizesSupportedLanguage(
        string candidate,
        string expected)
    {
        Assert.True(RealtimeTranscriptionLanguageCatalog.TryNormalize(candidate, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("fr-FR")]
    public void LanguageCatalog_RejectsUnsupportedLanguage(string candidate)
    {
        Assert.False(RealtimeTranscriptionLanguageCatalog.IsSupported(candidate));
    }
}
