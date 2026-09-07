using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace TransLink.Lite.Application.RealtimeAudio;

public sealed record RealtimeSpeechSynthesisLimits(
    int WorkQueueCapacity,
    int ProviderTimeoutSeconds,
    int MaximumTextCharacters,
    int MaximumAudioBytes);

public interface IRealtimeSpeechConsumerLease : IAsyncDisposable;

public interface IRealtimeSpeechSynthesisCoordinator
{
    bool RegisterSession(Guid sessionId, string targetLanguage);
    IRealtimeSpeechConsumerLease? AcquireConsumer(Guid sessionId);
    bool EnqueueTranslation(Guid sessionId, RealtimeSessionEvent translation);
    Task CompleteAsync(Guid sessionId);
}

public sealed class RealtimeSpeechSynthesisCoordinator(
    IRealtimeSpeechSynthesisProvider provider,
    IRealtimeSessionRegistry registry,
    RealtimeSpeechSynthesisLimits limits,
    RealtimeSpeechSynthesisMetrics metrics) : IRealtimeSpeechSynthesisCoordinator
{
    private readonly ConcurrentDictionary<Guid, SpeechSession> _sessions = new();

    public bool RegisterSession(Guid sessionId, string targetLanguage)
    {
        if (!RealtimeSpeechVoiceCatalog.TryGet(targetLanguage, out var voice)) return false;
        var session = new SpeechSession(sessionId, voice, provider, registry, limits, metrics);
        if (!_sessions.TryAdd(sessionId, session)) return false;
        session.Start();
        return true;
    }

    public IRealtimeSpeechConsumerLease? AcquireConsumer(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session.AcquireConsumer() : null;

    public bool EnqueueTranslation(Guid sessionId, RealtimeSessionEvent translation) =>
        _sessions.TryGetValue(sessionId, out var session) && session.Enqueue(translation);

    public async Task CompleteAsync(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session)) await session.DisposeAsync();
    }

    private sealed class SpeechSession(
        Guid sessionId,
        RealtimeSpeechVoice voice,
        IRealtimeSpeechSynthesisProvider provider,
        IRealtimeSessionRegistry registry,
        RealtimeSpeechSynthesisLimits limits,
        RealtimeSpeechSynthesisMetrics metrics) : IAsyncDisposable
    {
        private readonly Channel<RealtimeSessionEvent> _work = Channel.CreateBounded<RealtimeSessionEvent>(
            new BoundedChannelOptions(limits.WorkQueueCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        private readonly CancellationTokenSource _sessionLifetime = new();
        private readonly object _gate = new();
        private readonly HashSet<string> _queuedResults = new(StringComparer.Ordinal);
        private readonly Queue<string> _recentResults = new();
        private CancellationTokenSource? _demandLifetime;
        private Task? _worker;
        private int _consumers;
        private long _speechSequence;

        public void Start() => _worker = ProcessAsync();

        public IRealtimeSpeechConsumerLease AcquireConsumer()
        {
            lock (_gate)
            {
                _consumers++;
                metrics.ConsumerAdded();
                if (_consumers == 1)
                    _demandLifetime = CancellationTokenSource.CreateLinkedTokenSource(_sessionLifetime.Token);
            }
            return new ConsumerLease(this);
        }

        public bool Enqueue(RealtimeSessionEvent translation)
        {
            if (translation.Type != "translation.final" ||
                string.IsNullOrWhiteSpace(translation.Text) ||
                string.IsNullOrWhiteSpace(translation.SourceResultId) ||
                translation.Text.Length > limits.MaximumTextCharacters)
                return false;
            lock (_gate)
            {
                if (_consumers == 0 || _queuedResults.Contains(translation.SourceResultId)) return false;
                _queuedResults.Add(translation.SourceResultId);
                _recentResults.Enqueue(translation.SourceResultId);
                if (_recentResults.Count > 256)
                    _queuedResults.Remove(_recentResults.Dequeue());
                if (_work.Writer.TryWrite(translation)) return true;
                if (_work.Reader.TryRead(out _))
                {
                    metrics.Dropped();
                }
                if (_work.Writer.TryWrite(translation)) return true;
                return false;
            }
        }

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var translation in _work.Reader.ReadAllAsync(_sessionLifetime.Token))
                {
                    CancellationToken demandToken;
                    lock (_gate)
                    {
                        if (_consumers == 0 || _demandLifetime is null)
                        {
                            continue;
                        }
                        demandToken = _demandLifetime.Token;
                    }
                    var sequence = Interlocked.Increment(ref _speechSequence) - 1;
                    registry.Publish(sessionId, new RealtimeSessionEvent(
                        "speech.synthesizing", RealtimeAudioProtocol.CurrentVersion, sessionId,
                        translation.EventSequence, translation.SourceResultId,
                        TargetLanguage: voice.TargetLanguage, SpeechSequence: sequence));
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        metrics.RequestStarted(translation.Text!.Length);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                            _sessionLifetime.Token, demandToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(limits.ProviderTimeoutSeconds));
                        var result = await provider.SynthesizeAsync(new RealtimeSpeechSynthesisRequest(
                            translation.Text!, voice.TargetLanguage, voice.ProviderLanguageCode,
                            voice.VoiceId, voice.Engine, voice.AudioFormat, voice.SampleRate), timeout.Token);
                        stopwatch.Stop();
                        if (result.Audio.Length == 0 || result.Audio.Length > limits.MaximumAudioBytes)
                            throw new RealtimeSpeechSynthesisException("speech-audio-limit");
                        metrics.SegmentCompleted(result.Audio.Length, (long)stopwatch.Elapsed.TotalMilliseconds);
                        registry.Publish(sessionId, new RealtimeSessionEvent(
                            "speech.segment", RealtimeAudioProtocol.CurrentVersion, sessionId,
                            translation.EventSequence, translation.SourceResultId,
                            TargetLanguage: voice.TargetLanguage, SpeechSequence: sequence,
                            AudioFormat: result.AudioFormat, ContentType: result.ContentType,
                            AudioByteLength: result.Audio.Length,
                            SynthesisDurationMilliseconds: (long)stopwatch.Elapsed.TotalMilliseconds,
                            AudioPayload: result.Audio));
                    }
                    catch (OperationCanceledException) when (!_sessionLifetime.IsCancellationRequested)
                    {
                        if (!demandToken.IsCancellationRequested)
                        {
                            metrics.Failed();
                            PublishError(translation, sequence, "speech-timeout");
                        }
                    }
                    catch (RealtimeSpeechSynthesisException exception)
                    {
                        metrics.Failed();
                        PublishError(translation, sequence, exception.ErrorCode);
                    }
                    catch (Exception) when (!_sessionLifetime.IsCancellationRequested)
                    {
                        metrics.Failed();
                        PublishError(translation, sequence, "speech-failed");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void PublishError(RealtimeSessionEvent translation, long sequence, string code) =>
            registry.Publish(sessionId, new RealtimeSessionEvent(
                "speech.error", RealtimeAudioProtocol.CurrentVersion, sessionId,
                translation.EventSequence, translation.SourceResultId,
                TargetLanguage: voice.TargetLanguage, Code: code, SpeechSequence: sequence));

        private void ReleaseConsumer()
        {
            lock (_gate)
            {
                if (_consumers == 0) return;
                _consumers--;
                metrics.ConsumerRemoved();
                if (_consumers != 0) return;
                _demandLifetime?.Cancel();
                _demandLifetime?.Dispose();
                _demandLifetime = null;
                while (_work.Reader.TryRead(out _)) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _work.Writer.TryComplete();
            await _sessionLifetime.CancelAsync();
            if (_worker is not null) await _worker;
            lock (_gate)
            {
                _demandLifetime?.Dispose();
                _demandLifetime = null;
            }
            _sessionLifetime.Dispose();
        }

        private sealed class ConsumerLease(SpeechSession owner) : IRealtimeSpeechConsumerLease
        {
            private int _disposed;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.ReleaseConsumer();
                return ValueTask.CompletedTask;
            }
        }
    }
}
