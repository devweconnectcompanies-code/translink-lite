using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class ValidationRealtimeAudioSessionFactory
    : IRealtimeAudioSessionFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public ValidationRealtimeAudioSessionFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IRealtimeAudioSession Create(
        Guid sessionId,
        Guid authenticatedUserId,
        RealtimeAudioFormat format) =>
        new ValidationRealtimeAudioSession(
            sessionId,
            authenticatedUserId,
            format,
            _loggerFactory.CreateLogger<ValidationRealtimeAudioSession>());

    private sealed class ValidationRealtimeAudioSession : IRealtimeAudioSession
    {
        private readonly Guid _sessionId;
        private readonly Guid _authenticatedUserId;
        private readonly RealtimeAudioFormat _format;
        private readonly ILogger _logger;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _chunkCount;
        private long _byteCount;
        private ulong? _firstSequence;
        private ulong? _lastSequence;

        public ValidationRealtimeAudioSession(
            Guid sessionId,
            Guid authenticatedUserId,
            RealtimeAudioFormat format,
            ILogger logger)
        {
            _sessionId = sessionId;
            _authenticatedUserId = authenticatedUserId;
            _format = format;
            _logger = logger;
        }

        public ValueTask ConsumeAsync(
            RealtimeAudioChunk chunk,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _firstSequence ??= chunk.Header.Sequence;
            _lastSequence = chunk.Header.Sequence;
            _chunkCount++;
            _byteCount += chunk.Payload.Length;
            return ValueTask.CompletedTask;
        }

        public RealtimeAudioSessionSummary Complete(int protocolViolationCount)
        {
            _stopwatch.Stop();
            var summary = new RealtimeAudioSessionSummary(
                _sessionId,
                _chunkCount,
                _byteCount,
                _firstSequence,
                _lastSequence,
                protocolViolationCount,
                _stopwatch.Elapsed);

            _logger.LogInformation(
                "Realtime audio session completed. SessionId: {SessionId}, UserId: {UserId}, Encoding: {Encoding}, SampleRateHz: {SampleRateHz}, ChunkCount: {ChunkCount}, ByteCount: {ByteCount}, DurationMs: {DurationMs}, ProtocolViolations: {ProtocolViolations}",
                _sessionId,
                _authenticatedUserId,
                _format.Encoding,
                _format.SampleRateHz,
                summary.ChunkCount,
                summary.ByteCount,
                summary.Duration.TotalMilliseconds,
                summary.ProtocolViolationCount);

            return summary;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
