using System.Diagnostics;
using System.Threading.Channels;
using Amazon.Runtime;
using Amazon.TranscribeStreaming;
using Amazon.TranscribeStreaming.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class AwsRealtimeSpeechTranscriptionSessionFactory
    : IRealtimeSpeechTranscriptionSessionFactory
{
    private readonly IAmazonTranscribeStreaming _client;
    private readonly AwsTranscribeOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public AwsRealtimeSpeechTranscriptionSessionFactory(
        IAmazonTranscribeStreaming client,
        IOptions<AwsTranscribeOptions> options,
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _options = options.Value;
        _loggerFactory = loggerFactory;
    }

    public IRealtimeSpeechTranscriptionSession Create(
        Guid sessionId,
        Guid authenticatedUserId,
        RealtimeAudioFormat format) =>
        new AwsRealtimeSpeechTranscriptionSession(
            _client,
            _options,
            sessionId,
            authenticatedUserId,
            format,
            _loggerFactory.CreateLogger<AwsRealtimeSpeechTranscriptionSession>());

    private sealed class AwsRealtimeSpeechTranscriptionSession
        : IRealtimeSpeechTranscriptionSession
    {
        private readonly IAmazonTranscribeStreaming _client;
        private readonly AwsTranscribeOptions _options;
        private readonly Guid _sessionId;
        private readonly Guid _authenticatedUserId;
        private readonly RealtimeAudioFormat _format;
        private readonly ILogger _logger;
        private readonly Channel<byte[]> _audio;
        private readonly Channel<SpeechTranscriptionResult> _transcripts;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private StartStreamTranscriptionResponse? _response;
        private Func<RealtimeTranscriptEvent, CancellationToken, ValueTask>? _onTranscript;
        private Task? _resultProcessingTask;
        private Task? _dispatchTask;
        private Exception? _failure;
        private long _eventSequence;
        private long _chunkCount;
        private long _byteCount;
        private long _partialCount;
        private long _finalCount;
        private ulong? _firstSequence;
        private ulong? _lastSequence;
        private int _started;
        private int _completed;

        public AwsRealtimeSpeechTranscriptionSession(
            IAmazonTranscribeStreaming client,
            AwsTranscribeOptions options,
            Guid sessionId,
            Guid authenticatedUserId,
            RealtimeAudioFormat format,
            ILogger logger)
        {
            _client = client;
            _options = options;
            _sessionId = sessionId;
            _authenticatedUserId = authenticatedUserId;
            _format = format;
            _logger = logger;
            _audio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(
                options.AudioBufferCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
            _transcripts = Channel.CreateBounded<SpeechTranscriptionResult>(
                new BoundedChannelOptions(options.TranscriptBufferCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                });
        }

        public async Task StartAsync(
            Func<RealtimeTranscriptEvent, CancellationToken, ValueTask> onTranscript,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                throw new InvalidOperationException("Transcription session already started.");

            _onTranscript = onTranscript;
            _lifetime.CancelAfter(TimeSpan.FromMinutes(_options.MaximumSessionMinutes));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(_options.ProviderStartTimeoutSeconds));

            try
            {
                var request = new StartStreamTranscriptionRequest
                {
                    AudioStreamPublisher = NextAudioEventAsync,
                    LanguageCode = LanguageCode.FindValue(_format.SourceLanguage),
                    MediaEncoding = MediaEncoding.Pcm,
                    MediaSampleRateHertz = _format.SampleRateHz,
                    EnablePartialResultsStabilization =
                        _options.EnablePartialResultsStabilization,
                };
                if (_options.EnablePartialResultsStabilization)
                {
                    request.PartialResultsStability = PartialResultsStability.FindValue(
                        _options.PartialResultsStability);
                }

                _response = await _client.StartStreamTranscriptionAsync(
                    request,
                    linked.Token);
                _response.TranscriptResultStream.TranscriptEventReceived +=
                    HandleTranscriptEvent;
                _response.TranscriptResultStream.ExceptionReceived +=
                    HandleProviderException;
                _dispatchTask = DispatchTranscriptsAsync(_lifetime.Token);
                _resultProcessingTask = ObserveResultsAsync(
                    _response.TranscriptResultStream.StartProcessingAsync());

                _logger.LogInformation(
                    "Realtime transcription provider started. SessionId: {SessionId}, UserId: {UserId}, Provider: AWS, Region: {Region}, Language: {Language}, SampleRateHz: {SampleRateHz}",
                    _sessionId,
                    _authenticatedUserId,
                    _options.Region,
                    _format.SourceLanguage,
                    _format.SampleRateHz);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var mapped = MapProviderFailure(exception);
                _logger.LogWarning(
                    "Realtime transcription provider failed to start. SessionId: {SessionId}, Category: {Category}",
                    _sessionId,
                    mapped.ErrorCode);
                throw mapped;
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested)
            {
                throw new RealtimeTranscriptionException(
                    "transcription-start-timeout",
                    exception);
            }
        }

        public async ValueTask SendAudioAsync(
            RealtimeAudioChunk chunk,
            CancellationToken cancellationToken)
        {
            ThrowIfFailed();
            if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _completed) != 0)
                throw new InvalidOperationException("Transcription session is not active.");

            var ownedPayload = chunk.Payload.ToArray();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            timeout.CancelAfter(_options.AudioWriteTimeoutMilliseconds);
            try
            {
                await _audio.Writer.WriteAsync(ownedPayload, timeout.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                !_lifetime.IsCancellationRequested)
            {
                throw new RealtimeTranscriptionException("transcription-backpressure");
            }

            _firstSequence ??= chunk.Header.Sequence;
            _lastSequence = chunk.Header.Sequence;
            Interlocked.Increment(ref _chunkCount);
            Interlocked.Add(ref _byteCount, ownedPayload.Length);
            ThrowIfFailed();
        }

        public async Task<RealtimeAudioSessionSummary> CompleteAsync(
            int protocolViolationCount,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _audio.Writer.TryComplete();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.CompletionTimeoutSeconds));
                try
                {
                    if (_resultProcessingTask is not null)
                        await _resultProcessingTask.WaitAsync(timeout.Token);
                    _transcripts.Writer.TryComplete();
                    if (_dispatchTask is not null)
                        await _dispatchTask.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    _lifetime.Cancel();
                }
                catch (Exception exception)
                {
                    SetFailure(MapProviderFailure(exception));
                }
            }

            _stopwatch.Stop();
            var summary = new RealtimeAudioSessionSummary(
                _sessionId,
                Interlocked.Read(ref _chunkCount),
                Interlocked.Read(ref _byteCount),
                _firstSequence,
                _lastSequence,
                protocolViolationCount,
                _stopwatch.Elapsed);
            _logger.LogInformation(
                "Realtime transcription session completed. SessionId: {SessionId}, UserId: {UserId}, ChunkCount: {ChunkCount}, ByteCount: {ByteCount}, PartialCount: {PartialCount}, FinalCount: {FinalCount}, DurationMs: {DurationMs}, ProtocolViolations: {ProtocolViolations}",
                _sessionId,
                _authenticatedUserId,
                summary.ChunkCount,
                summary.ByteCount,
                Interlocked.Read(ref _partialCount),
                Interlocked.Read(ref _finalCount),
                summary.Duration.TotalMilliseconds,
                summary.ProtocolViolationCount);
            return summary;
        }

        public async ValueTask DisposeAsync()
        {
            _audio.Writer.TryComplete();
            _transcripts.Writer.TryComplete();
            await _lifetime.CancelAsync();
            _response?.Dispose();
            var backgroundTasks = new[] { _resultProcessingTask, _dispatchTask }
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
            if (backgroundTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(backgroundTasks).WaitAsync(
                        TimeSpan.FromSeconds(_options.CompletionTimeoutSeconds));
                }
                catch (Exception)
                {
                    // Provider/result failures were already mapped and the lifetime is cancelled.
                }
            }
            _lifetime.Dispose();
        }

        private async Task<IAudioStreamEvent> NextAudioEventAsync()
        {
            try
            {
                if (await _audio.Reader.WaitToReadAsync(_lifetime.Token) &&
                    _audio.Reader.TryRead(out var payload))
                {
                    return new AudioEvent
                    {
                        AudioChunk = new MemoryStream(payload, writable: false),
                    };
                }
            }
            catch (OperationCanceledException)
            {
                // Returning null terminates the SDK publisher after cancellation.
            }

            return null!;
        }

        private void HandleTranscriptEvent(
            object? sender,
            Amazon.Runtime.EventStreams.EventStreamEventReceivedArgs<TranscriptEvent> args)
        {
            foreach (var result in args.EventStreamEvent.Transcript?.Results ?? [])
            {
                var text = result.Alternatives?.FirstOrDefault()?.Transcript;
                if (string.IsNullOrWhiteSpace(text)) continue;
                var mapped = new SpeechTranscriptionResult(
                    result.ResultId ?? string.Empty,
                    text,
                    result.IsPartial == true,
                    result.StartTime is null ? null : TimeSpan.FromSeconds(result.StartTime.Value),
                    result.EndTime is null ? null : TimeSpan.FromSeconds(result.EndTime.Value),
                    _format.SourceLanguage);
                if (!_transcripts.Writer.TryWrite(mapped))
                {
                    SetFailure(new RealtimeTranscriptionException(
                        "transcription-output-overload"));
                    return;
                }
            }
        }

        private void HandleProviderException(
            object? sender,
            Amazon.Runtime.EventStreams.EventStreamExceptionReceivedArgs<
                TranscribeStreamingEventStreamException> args) =>
            SetFailure(MapProviderFailure(args.EventStreamException));

        private async Task ObserveResultsAsync(Task processingTask)
        {
            try
            {
                await processingTask;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetFailure(MapProviderFailure(exception));
            }
            finally
            {
                _transcripts.Writer.TryComplete();
            }
        }

        private async Task DispatchTranscriptsAsync(CancellationToken cancellationToken)
        {
            await foreach (var result in _transcripts.Reader.ReadAllAsync(cancellationToken))
            {
                var sequence = Interlocked.Increment(ref _eventSequence) - 1;
                var mapped = RealtimeTranscriptEventMapper.Map(
                    _sessionId,
                    sequence,
                    result);
                if (result.IsPartial)
                    Interlocked.Increment(ref _partialCount);
                else
                    Interlocked.Increment(ref _finalCount);
                await _onTranscript!(mapped, cancellationToken);
            }
        }

        private void SetFailure(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _failure, exception, null) is null)
            {
                _audio.Writer.TryComplete(exception);
                _transcripts.Writer.TryComplete(exception);
                _lifetime.Cancel();
            }
        }

        private void ThrowIfFailed()
        {
            if (Volatile.Read(ref _failure) is { } failure)
                throw failure is RealtimeTranscriptionException mapped
                    ? mapped
                    : MapProviderFailure(failure);
            _lifetime.Token.ThrowIfCancellationRequested();
        }

        private static RealtimeTranscriptionException MapProviderFailure(Exception exception) =>
            exception switch
            {
                BadRequestException => new("transcription-invalid-request", exception),
                LimitExceededException => new("transcription-capacity", exception),
                ServiceUnavailableException or InternalFailureException =>
                    new("transcription-unavailable", exception),
                AmazonServiceException => new("transcription-rejected", exception),
                AmazonClientException => new("transcription-connection", exception),
                RealtimeTranscriptionException mapped => mapped,
                _ => new("transcription-failed", exception),
            };
    }
}
