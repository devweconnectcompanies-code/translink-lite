namespace TransLink.Lite.API.Configuration;

public sealed class RealtimeAudioOptions
{
    public const string SectionName = "RealtimeAudio";

    public int ProtocolVersion { get; init; } = 1;

    public int MaxBinaryFrameBytes { get; init; } = 65_560;

    public int MaxControlMessageBytes { get; init; } = 4_096;

    public int ChunkDurationMs { get; init; } = 150;

    public int HandshakeTimeoutSeconds { get; init; } = 10;

    public int IdleTimeoutSeconds { get; init; } = 15;

    public int MaxProtocolViolations { get; init; } = 1;

    public string[] AllowedOrigins { get; init; } = [];
}
