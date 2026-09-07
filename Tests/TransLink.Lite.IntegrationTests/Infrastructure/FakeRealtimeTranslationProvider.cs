using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.IntegrationTests.Infrastructure;

public sealed class FakeRealtimeTranslationProvider : IRealtimeTranslationProvider
{
    private int _requests;

    public bool Fail { get; set; }
    public int Requests => Volatile.Read(ref _requests);

    public Task<RealtimeTranslationResult> TranslateAsync(
        RealtimeTranslationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        if (Fail)
            throw new RealtimeTranslationException("translation-connection");
        return Task.FromResult(new RealtimeTranslationResult(
            "translated test text", request.SourceLanguage, request.TargetLanguage));
    }

    public void Reset()
    {
        Fail = false;
        Volatile.Write(ref _requests, 0);
    }
}
