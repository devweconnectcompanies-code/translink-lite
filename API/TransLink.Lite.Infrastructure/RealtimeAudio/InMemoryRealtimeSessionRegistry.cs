using System.Collections.Concurrent;
using System.Threading.Channels;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class InMemoryRealtimeSessionRegistry : IRealtimeSessionRegistry
{
    public const int ObserverBufferCapacity = 32;
    private readonly ConcurrentDictionary<Guid, ProducerSession> _sessions = new();

    public bool TryRegisterProducer(
        Guid sessionId,
        Guid ownerId,
        string sourceLanguage,
        string targetLanguage) =>
        _sessions.TryAdd(sessionId, new ProducerSession(
            ownerId, sourceLanguage, targetLanguage, DateTimeOffset.UtcNow));

    public IReadOnlyList<ActiveRealtimeSession> GetActiveSessions(Guid ownerId) =>
        _sessions
            .Where(pair => pair.Value.OwnerId == ownerId)
            .Select(pair => new ActiveRealtimeSession(
                pair.Key,
                pair.Value.SourceLanguage,
                pair.Value.TargetLanguage,
                pair.Value.StartedAt))
            .OrderByDescending(session => session.StartedAt)
            .ToArray();

    public IRealtimeSessionSubscription? Subscribe(Guid sessionId, Guid ownerId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.OwnerId != ownerId)
            return null;

        var observerId = Guid.NewGuid();
        var channel = Channel.CreateBounded<RealtimeSessionEvent>(new BoundedChannelOptions(
            ObserverBufferCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        if (!session.Observers.TryAdd(observerId, channel)) return null;
        return new Subscription(channel.Reader, () =>
        {
            if (session.Observers.TryRemove(observerId, out var removed))
                removed.Writer.TryComplete();
        });
    }

    public void Publish(Guid sessionId, RealtimeSessionEvent message)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        foreach (var observer in session.Observers)
        {
            if (observer.Value.Writer.TryWrite(message)) continue;
            if (session.Observers.TryRemove(observer.Key, out var slowObserver))
                slowObserver.Writer.TryComplete(
                    new RealtimeObserverOverflowException());
        }
    }

    public void Complete(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session)) return;
        var ended = new RealtimeSessionEvent(
            "session.closed", RealtimeAudioProtocol.CurrentVersion, sessionId);
        foreach (var observer in session.Observers.Values)
        {
            observer.Writer.TryWrite(ended);
            observer.Writer.TryComplete();
        }
        session.Observers.Clear();
    }

    private sealed class ProducerSession(
        Guid ownerId,
        string sourceLanguage,
        string targetLanguage,
        DateTimeOffset startedAt)
    {
        public Guid OwnerId { get; } = ownerId;
        public string SourceLanguage { get; } = sourceLanguage;
        public string TargetLanguage { get; } = targetLanguage;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public ConcurrentDictionary<Guid, Channel<RealtimeSessionEvent>> Observers { get; } = new();
    }

    private sealed class Subscription(
        ChannelReader<RealtimeSessionEvent> events,
        Action dispose) : IRealtimeSessionSubscription
    {
        private int _disposed;
        public ChannelReader<RealtimeSessionEvent> Events { get; } = events;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class RealtimeObserverOverflowException : Exception;
