using System.Diagnostics;

namespace TransLink.Lite.Application.RealtimeAudio;

public sealed record RealtimeTranslationOutcome(
    RealtimeTranslationResult? Result,
    string? ErrorCode,
    bool Skipped,
    long DurationMilliseconds)
{
    public bool IsSuccess => Result is not null;
}

public sealed class RealtimeTranslationOrchestrator
{
    private readonly IRealtimeTranslationProvider _provider;

    public RealtimeTranslationOrchestrator(IRealtimeTranslationProvider provider)
    {
        _provider = provider;
    }

    public async Task<RealtimeTranslationOutcome> TranslateFinalAsync(
        RealtimeTranscriptEvent transcript,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (!transcript.IsFinal || string.IsNullOrWhiteSpace(transcript.Text))
            return new(null, null, true, 0);
        if (!RealtimeTranslationLanguageCatalog.TryMapSource(
                transcript.SourceLanguage, out var sourceLanguage) ||
            !RealtimeTranslationLanguageCatalog.TryNormalizeTarget(
                targetLanguage, out var normalizedTargetLanguage))
            return new(null, "translation-unsupported-language", false, 0);
        if (string.Equals(
                sourceLanguage, normalizedTargetLanguage,
                StringComparison.Ordinal))
            return new(
                new RealtimeTranslationResult(
                    transcript.Text, sourceLanguage, normalizedTargetLanguage),
                null,
                false,
                0);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _provider.TranslateAsync(
                new RealtimeTranslationRequest(
                    transcript.Text, sourceLanguage, normalizedTargetLanguage),
                cancellationToken);
            stopwatch.Stop();
            return new(result, null, false, (long)stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (RealtimeTranslationException exception)
        {
            stopwatch.Stop();
            return new(null, exception.ErrorCode, false,
                (long)stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
