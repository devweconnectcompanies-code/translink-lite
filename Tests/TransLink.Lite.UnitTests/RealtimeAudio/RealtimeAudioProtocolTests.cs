using System.Buffers.Binary;
using System.Text;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class RealtimeAudioProtocolTests
{
    [Fact]
    public void ParseControl_WithValidStart_ReturnsMessage()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"type":"session.start","protocolVersion":2,"audio":{"encoding":"pcm_s16le","sampleRateHz":48000,"channelCount":1,"chunkDurationMs":150,"sourceLanguage":"en-US"}}""");

        var result = RealtimeAudioProtocol.ParseControl(payload);

        Assert.True(result.IsValid);
        Assert.Equal("session.start", result.Message?.Type);
        Assert.Equal(48_000, result.Message?.Audio?.SampleRateHz);
    }

    [Fact]
    public void ParseControl_WithMalformedJson_ReturnsSafeError()
    {
        var result = RealtimeAudioProtocol.ParseControl("not-json"u8);

        Assert.False(result.IsValid);
        Assert.Equal("malformed-control", result.ErrorCode);
    }

    [Fact]
    public void ValidateSessionStart_WithUnsupportedVersion_ReturnsError()
    {
        var message = CreateStart(protocolVersion: 1);

        var error = RealtimeAudioProtocol.ValidateSessionStart(message, 2, 150);

        Assert.Equal("unsupported-protocol-version", error);
    }

    [Theory]
    [InlineData("float32", 48_000, 1, 150)]
    [InlineData("pcm_s16le", 8_000, 1, 150)]
    [InlineData("pcm_s16le", 48_000, 2, 150)]
    [InlineData("pcm_s16le", 48_000, 1, 200)]
    public void ValidateSessionStart_WithUnsupportedFormat_ReturnsError(
        string encoding,
        int sampleRateHz,
        int channelCount,
        int chunkDurationMs)
    {
        var message = CreateStart(
            encoding: encoding,
            sampleRateHz: sampleRateHz,
            channelCount: channelCount,
            chunkDurationMs: chunkDurationMs);

        var error = RealtimeAudioProtocol.ValidateSessionStart(message, 2, 150);

        Assert.Equal("unsupported-audio-format", error);
    }

    [Fact]
    public void ParseAudioFrame_WithValidHeader_ReturnsMetadata()
    {
        var frame = CreateFrame(sequence: 7, elapsedMilliseconds: 1_050, payloadLength: 320);

        var result = RealtimeAudioProtocol.ParseAudioFrame(frame);

        Assert.True(result.IsValid);
        Assert.Equal((ulong)7, result.Header.Sequence);
        Assert.Equal((ulong)1_050, result.Header.ElapsedMilliseconds);
        Assert.Equal(320, result.Header.PayloadLength);
    }

    [Theory]
    [InlineData(10, "frame-too-small")]
    [InlineData(25, "unaligned-pcm-payload")]
    public void ParseAudioFrame_WithInvalidLength_ReturnsError(
        int frameLength,
        string expectedError)
    {
        var frame = frameLength < RealtimeAudioProtocol.BinaryHeaderLength
            ? new byte[frameLength]
            : CreateFrame(0, 0, frameLength - RealtimeAudioProtocol.BinaryHeaderLength);

        var result = RealtimeAudioProtocol.ParseAudioFrame(frame);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public void ParseAudioFrame_WithIncorrectDeclaredPayload_ReturnsError()
    {
        var frame = CreateFrame(0, 0, 320);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(20, 4), 318);

        var result = RealtimeAudioProtocol.ParseAudioFrame(frame);

        Assert.False(result.IsValid);
        Assert.Equal("invalid-payload-length", result.ErrorCode);
    }

    [Fact]
    public void ValidateLimits_WithSupportedConfiguration_ReturnsNoError()
    {
        var limits = new RealtimeAudioLimits(2, 65_560, 4_096, 150, 10, 15, 1);

        Assert.Null(RealtimeAudioProtocol.ValidateLimits(limits));
    }

    [Theory]
    [InlineData(1, 65_560, 4_096, 150, 10, 15, 1, "protocol-version")]
    [InlineData(2, 100, 4_096, 150, 10, 15, 1, "binary-frame-limit")]
    [InlineData(2, 65_560, 100, 150, 10, 15, 1, "control-message-limit")]
    [InlineData(2, 65_560, 4_096, 250, 10, 15, 1, "chunk-duration")]
    [InlineData(2, 65_560, 4_096, 150, 0, 15, 1, "handshake-timeout")]
    [InlineData(2, 65_560, 4_096, 150, 10, 0, 1, "idle-timeout")]
    [InlineData(2, 65_560, 4_096, 150, 10, 15, 0, "protocol-violations")]
    public void ValidateLimits_WithInvalidConfiguration_ReturnsSpecificError(
        int protocolVersion,
        int maxBinaryFrameBytes,
        int maxControlMessageBytes,
        int chunkDurationMs,
        int handshakeTimeoutSeconds,
        int idleTimeoutSeconds,
        int maxProtocolViolations,
        string expectedError)
    {
        var limits = new RealtimeAudioLimits(
            protocolVersion,
            maxBinaryFrameBytes,
            maxControlMessageBytes,
            chunkDurationMs,
            handshakeTimeoutSeconds,
            idleTimeoutSeconds,
            maxProtocolViolations);

        Assert.Equal(expectedError, RealtimeAudioProtocol.ValidateLimits(limits));
    }

    private static RealtimeControlMessage CreateStart(
        int protocolVersion = 2,
        string encoding = "pcm_s16le",
        int sampleRateHz = 48_000,
        int channelCount = 1,
        int chunkDurationMs = 150) =>
        new(
            "session.start",
            protocolVersion,
            new RealtimeAudioFormat(
                encoding,
                sampleRateHz,
                channelCount,
                chunkDurationMs,
                "en-US"));

    private static byte[] CreateFrame(
        ulong sequence,
        ulong elapsedMilliseconds,
        int payloadLength)
    {
        var frame = new byte[RealtimeAudioProtocol.BinaryHeaderLength + payloadLength];
        frame[0] = RealtimeAudioProtocol.MagicFirstByte;
        frame[1] = RealtimeAudioProtocol.MagicSecondByte;
        frame[2] = RealtimeAudioProtocol.CurrentVersion;
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(4, 8), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(12, 8), elapsedMilliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(20, 4), (uint)payloadLength);
        return frame;
    }
}
