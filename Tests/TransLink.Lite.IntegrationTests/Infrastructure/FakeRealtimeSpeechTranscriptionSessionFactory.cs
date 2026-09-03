using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.IntegrationTests.Infrastructure;

public sealed class FakeRealtimeSpeechTranscriptionSessionFactory
    : IRealtimeSpeechTranscriptionSessionFactory
{
    private int _created;
    private int _completed;
    private int _disposed;

    public bool FailOnAudio { get; set; }
    public int Created => Volatile.Read(ref _created);
    public int Completed => Volatile.Read(ref _completed);
    public int Disposed => Volatile.Read(ref _disposed);

    public IRealtimeSpeechTranscriptionSession Create(
        Guid sessionId,
        Guid authenticatedUserId,
        RealtimeAudioFormat format)
    {
        Interlocked.Increment(ref _created);
        return new Session(this, sessionId, format);
    }

    public void Reset()
    {
        FailOnAudio = false;
        Volatile.Write(ref _created, 0);
        Volatile.Write(ref _completed, 0);
        Volatile.Write(ref _disposed, 0);
    }

    private sealed class Session : IRealtimeSpeechTranscriptionSession
    {
        private readonly FakeRealtimeSpeechTranscriptionSessionFactory _owner;
        private readonly Guid _sessionId;
        private readonly RealtimeAudioFormat _format;
        private Func<RealtimeTranscriptEvent, CancellationToken, ValueTask>? _onTranscript;
        private long _chunks;
        private long _bytes;
        private int _emitted;

        public Session(
            FakeRealtimeSpeechTranscriptionSessionFactory owner,
            Guid sessionId,
            RealtimeAudioFormat format)
        {
            _owner = owner;
            _sessionId = sessionId;
            _format = format;
        }

        public Task StartAsync(
            Func<RealtimeTranscriptEvent, CancellationToken, ValueTask> onTranscript,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onTranscript = onTranscript;
            return Task.CompletedTask;
        }

        public async ValueTask SendAudioAsync(
            RealtimeAudioChunk chunk,
            CancellationToken cancellationToken)
        {
            if (_owner.FailOnAudio)
            {
                throw new RealtimeTranscriptionException("transcription-provider-unavailable");
            }

            Interlocked.Increment(ref _chunks);
            Interlocked.Add(ref _bytes, chunk.Payload.Length);
            if (Interlocked.Exchange(ref _emitted, 1) == 0 && _onTranscript is not null)
            {
                await _onTranscript(
                    RealtimeTranscriptEventMapper.Map(
                        _sessionId,
                        0,
                        new SpeechTranscriptionResult(
                            "test-partial",
                            "partial test transcript",
                            true,
                            TimeSpan.Zero,
                            TimeSpan.FromMilliseconds(100),
                            _format.SourceLanguage)),
                    cancellationToken);
                await _onTranscript(
                    RealtimeTranscriptEventMapper.Map(
                        _sessionId,
                        1,
                        new SpeechTranscriptionResult(
                            "test-final",
                            "final test transcript",
                            false,
                            TimeSpan.Zero,
                            TimeSpan.FromMilliseconds(150),
                            _format.SourceLanguage)),
                    cancellationToken);
            }
        }

        public Task<RealtimeAudioSessionSummary> CompleteAsync(
            int protocolViolationCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _owner._completed);
            return Task.FromResult(new RealtimeAudioSessionSummary(
                _sessionId,
                Volatile.Read(ref _chunks),
                Volatile.Read(ref _bytes),
                null,
                null,
                protocolViolationCount,
                TimeSpan.Zero));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _owner._disposed);
            return ValueTask.CompletedTask;
        }
    }
}
