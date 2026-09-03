using System.Buffers.Binary;
using System.Text.Json;

namespace TransLink.Lite.Application.RealtimeAudio;

public static class RealtimeAudioProtocol
{
    public const int CurrentVersion = 1;
    public const int BinaryHeaderLength = 24;
    public const byte MagicFirstByte = (byte)'T';
    public const byte MagicSecondByte = (byte)'L';
    public const string PcmSigned16LittleEndian = "pcm_s16le";
    public const string WebSocketSubprotocol = "translink.realtime.v1";
    public const string BearerSubprotocolPrefix = "translink.bearer.";

    public static ControlParseResult ParseControl(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<RealtimeControlMessage>(utf8Json,
                RealtimeJsonSerializerContext.Default.RealtimeControlMessage);

            if (message is null || string.IsNullOrWhiteSpace(message.Type))
            {
                return ControlParseResult.Invalid("invalid-control");
            }

            return ControlParseResult.Valid(message);
        }
        catch (JsonException)
        {
            return ControlParseResult.Invalid("malformed-control");
        }
    }

    public static AudioFrameParseResult ParseAudioFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < BinaryHeaderLength)
        {
            return AudioFrameParseResult.Invalid("frame-too-small");
        }

        if (frame[0] != MagicFirstByte || frame[1] != MagicSecondByte)
        {
            return AudioFrameParseResult.Invalid("invalid-frame-magic");
        }

        var protocolVersion = frame[2];
        var flags = frame[3];
        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(frame[4..12]);
        var elapsedMilliseconds = BinaryPrimitives.ReadUInt64LittleEndian(frame[12..20]);
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[20..24]);

        if (payloadLength != frame.Length - BinaryHeaderLength || payloadLength == 0)
        {
            return AudioFrameParseResult.Invalid("invalid-payload-length");
        }

        if ((payloadLength & 1) != 0)
        {
            return AudioFrameParseResult.Invalid("unaligned-pcm-payload");
        }

        return AudioFrameParseResult.Valid(new AudioFrameHeader(
            protocolVersion,
            flags,
            sequence,
            elapsedMilliseconds,
            checked((int)payloadLength)));
    }

    public static string? ValidateSessionStart(
        RealtimeControlMessage message,
        int expectedProtocolVersion,
        int expectedChunkDurationMs)
    {
        if (message.Type != "session.start") return "start-required";
        if (message.ProtocolVersion != expectedProtocolVersion ||
            message.ProtocolVersion != CurrentVersion)
            return "unsupported-protocol-version";

        var format = message.Audio;
        if (format is null ||
            format.Encoding != PcmSigned16LittleEndian ||
            format.ChannelCount != 1 ||
            format.SampleRateHz is < 16_000 or > 48_000 ||
            format.ChunkDurationMs != expectedChunkDurationMs)
            return "unsupported-audio-format";

        return null;
    }

    public static string? ValidateLimits(RealtimeAudioLimits limits)
    {
        if (limits.ProtocolVersion != CurrentVersion) return "protocol-version";
        if (limits.MaxBinaryFrameBytes is < 1_024 or > 1_048_576)
            return "binary-frame-limit";
        if (limits.MaxControlMessageBytes is < 256 or > 65_536)
            return "control-message-limit";
        if (limits.ChunkDurationMs is < 100 or > 200) return "chunk-duration";
        if (limits.HandshakeTimeoutSeconds is < 1 or > 60) return "handshake-timeout";
        if (limits.IdleTimeoutSeconds is < 1 or > 300) return "idle-timeout";
        if (limits.MaxProtocolViolations is < 1 or > 10) return "protocol-violations";
        return null;
    }
}
