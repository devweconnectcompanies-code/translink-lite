using System.Text.Json.Serialization;

namespace TransLink.Lite.Application.RealtimeAudio;

public sealed record RealtimeControlMessage(
    string? Type,
    int ProtocolVersion,
    RealtimeAudioFormat? Audio);

public sealed record RealtimeAudioFormat(
    string Encoding,
    int SampleRateHz,
    int ChannelCount,
    int ChunkDurationMs);

public sealed record RealtimeAudioLimits(
    int ProtocolVersion,
    int MaxBinaryFrameBytes,
    int MaxControlMessageBytes,
    int ChunkDurationMs,
    int HandshakeTimeoutSeconds,
    int IdleTimeoutSeconds,
    int MaxProtocolViolations);

public readonly record struct AudioFrameHeader(
    int ProtocolVersion,
    byte Flags,
    ulong Sequence,
    ulong ElapsedMilliseconds,
    int PayloadLength);

public readonly record struct RealtimeAudioChunk(
    AudioFrameHeader Header,
    ReadOnlyMemory<byte> Payload);

public readonly record struct RealtimeAudioSessionSummary(
    Guid SessionId,
    long ChunkCount,
    long ByteCount,
    ulong? FirstSequence,
    ulong? LastSequence,
    int ProtocolViolationCount,
    TimeSpan Duration);

public readonly record struct ControlParseResult(
    bool IsValid,
    RealtimeControlMessage? Message,
    string? ErrorCode)
{
    public static ControlParseResult Valid(RealtimeControlMessage message) =>
        new(true, message, null);

    public static ControlParseResult Invalid(string errorCode) =>
        new(false, null, errorCode);
}

public readonly record struct AudioFrameParseResult(
    bool IsValid,
    AudioFrameHeader Header,
    string? ErrorCode)
{
    public static AudioFrameParseResult Valid(AudioFrameHeader header) =>
        new(true, header, null);

    public static AudioFrameParseResult Invalid(string errorCode) =>
        new(false, default, errorCode);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RealtimeControlMessage))]
internal sealed partial class RealtimeJsonSerializerContext : JsonSerializerContext;
