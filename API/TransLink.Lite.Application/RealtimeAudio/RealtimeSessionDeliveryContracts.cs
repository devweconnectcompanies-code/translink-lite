using System.Threading.Channels;
using System.Text.Json.Serialization;

namespace TransLink.Lite.Application.RealtimeAudio;

public sealed record ActiveRealtimeSession(
    Guid SessionId,
    string SourceLanguage,
    string TargetLanguage,
    DateTimeOffset StartedAt);

public sealed record RealtimeSessionEvent(
    string Type,
    int ProtocolVersion,
    Guid SessionId,
    long? EventSequence = null,
    string? SourceResultId = null,
    string? Text = null,
    string? SourceLanguage = null,
    string? TargetLanguage = null,
    string? Code = null,
    long? SpeechSequence = null,
    string? AudioFormat = null,
    string? ContentType = null,
    int? AudioByteLength = null,
    long? SynthesisDurationMilliseconds = null,
    [property: JsonIgnore] byte[]? AudioPayload = null);

public interface IRealtimeSessionSubscription : IAsyncDisposable
{
    ChannelReader<RealtimeSessionEvent> Events { get; }
}

public interface IRealtimeSessionRegistry
{
    bool TryRegisterProducer(Guid sessionId, Guid ownerId, string sourceLanguage, string targetLanguage);
    IReadOnlyList<ActiveRealtimeSession> GetActiveSessions(Guid ownerId);
    IRealtimeSessionSubscription? Subscribe(Guid sessionId, Guid ownerId);
    void Publish(Guid sessionId, RealtimeSessionEvent message);
    void Complete(Guid sessionId);
}
