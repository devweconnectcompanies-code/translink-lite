using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class RealtimeTranslationOrchestratorTests
{
    [Fact]
    public async Task FinalTranscript_IsMappedAndTranslated()
    {
        var provider = new RecordingProvider();
        var orchestrator = new RealtimeTranslationOrchestrator(provider);

        var outcome = await orchestrator.TranslateFinalAsync(
            Transcript("source text"), "es", default);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("translated text", outcome.Result?.Text);
        Assert.Equal("en", provider.LastRequest?.SourceLanguage);
        Assert.Equal("es", provider.LastRequest?.TargetLanguage);
        Assert.Equal(1, provider.Requests);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("partial", false)]
    public async Task IneligibleTranscript_IsSkipped(string text, bool isFinal)
    {
        var provider = new RecordingProvider();
        var outcome = await new RealtimeTranslationOrchestrator(provider)
            .TranslateFinalAsync(Transcript(text, isFinal), "es", default);

        Assert.True(outcome.Skipped);
        Assert.Equal(0, provider.Requests);
    }

    [Fact]
    public async Task SameLanguage_PassesThroughWithoutProviderCall()
    {
        var provider = new RecordingProvider();
        var outcome = await new RealtimeTranslationOrchestrator(provider)
            .TranslateFinalAsync(Transcript("source text"), "en", default);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("source text", outcome.Result?.Text);
        Assert.Equal(0, provider.Requests);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsSafeCategory()
    {
        var provider = new RecordingProvider { ErrorCode = "translation-connection" };
        var outcome = await new RealtimeTranslationOrchestrator(provider)
            .TranslateFinalAsync(Transcript("source text"), "es", default);

        Assert.False(outcome.IsSuccess);
        Assert.Equal("translation-connection", outcome.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_PropagatesToProvider()
    {
        var provider = new RecordingProvider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new RealtimeTranslationOrchestrator(provider).TranslateFinalAsync(
                Transcript("source text"), "es", cancellation.Token));
    }

    private static RealtimeTranscriptEvent Transcript(
        string text,
        bool isFinal = true) => new(
            isFinal ? "transcript.final" : "transcript.partial",
            3,
            Guid.NewGuid(),
            1,
            "result-1",
            text,
            isFinal,
            0,
            100,
            "en-US");

    private sealed class RecordingProvider : IRealtimeTranslationProvider
    {
        public int Requests { get; private set; }
        public string? ErrorCode { get; init; }
        public RealtimeTranslationRequest? LastRequest { get; private set; }

        public Task<RealtimeTranslationResult> TranslateAsync(
            RealtimeTranslationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            LastRequest = request;
            if (ErrorCode is not null)
                throw new RealtimeTranslationException(ErrorCode);
            return Task.FromResult(new RealtimeTranslationResult(
                "translated text", request.SourceLanguage, request.TargetLanguage));
        }
    }
}
