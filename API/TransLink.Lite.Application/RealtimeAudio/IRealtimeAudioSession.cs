namespace TransLink.Lite.Application.RealtimeAudio;

public interface IRealtimeSpeechTranscriptionSession : IAsyncDisposable
{
    Task StartAsync(
        Func<RealtimeTranscriptEvent, CancellationToken, ValueTask> onTranscript,
        CancellationToken cancellationToken);

    ValueTask SendAudioAsync(
        RealtimeAudioChunk chunk,
        CancellationToken cancellationToken);

    Task<RealtimeAudioSessionSummary> CompleteAsync(
        int protocolViolationCount,
        CancellationToken cancellationToken);
}

public interface IRealtimeSpeechTranscriptionSessionFactory
{
    IRealtimeSpeechTranscriptionSession Create(
        Guid sessionId,
        Guid authenticatedUserId,
        RealtimeAudioFormat format);
}

public sealed class RealtimeTranscriptionException : Exception
{
    public RealtimeTranscriptionException(string errorCode, Exception? innerException = null)
        : base("Realtime transcription failed.", innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
