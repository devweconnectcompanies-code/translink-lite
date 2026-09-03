namespace TransLink.Lite.Application.RealtimeAudio;

public interface IRealtimeAudioSession : IAsyncDisposable
{
    ValueTask ConsumeAsync(RealtimeAudioChunk chunk, CancellationToken cancellationToken);

    RealtimeAudioSessionSummary Complete(int protocolViolationCount);
}

public interface IRealtimeAudioSessionFactory
{
    IRealtimeAudioSession Create(
        Guid sessionId,
        Guid authenticatedUserId,
        RealtimeAudioFormat format);
}
