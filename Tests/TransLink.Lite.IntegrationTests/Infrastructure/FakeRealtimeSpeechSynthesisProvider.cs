using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.IntegrationTests.Infrastructure;

public sealed class FakeRealtimeSpeechSynthesisProvider : IRealtimeSpeechSynthesisProvider
{
    private int _requests;
    public bool Fail { get; set; }
    public int Requests => Volatile.Read(ref _requests);

    public Task<RealtimeSpeechSynthesisResult> SynthesizeAsync(
        RealtimeSpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        if (Fail) throw new RealtimeSpeechSynthesisException("speech-unavailable");
        return Task.FromResult(new RealtimeSpeechSynthesisResult(
            [0x49, 0x44, 0x33, 0x04], "mp3", "audio/mpeg", 24_000));
    }

    public void Reset() { Fail = false; Volatile.Write(ref _requests, 0); }
}
