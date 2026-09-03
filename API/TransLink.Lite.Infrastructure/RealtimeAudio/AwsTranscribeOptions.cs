namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class AwsTranscribeOptions
{
    public const string SectionName = "AwsTranscribe";

    public string Region { get; init; } = string.Empty;

    public bool EnablePartialResultsStabilization { get; init; } = true;

    public string PartialResultsStability { get; init; } = "medium";

    public int AudioBufferCapacity { get; init; } = 8;

    public int TranscriptBufferCapacity { get; init; } = 32;

    public int AudioWriteTimeoutMilliseconds { get; init; } = 2_000;

    public int ProviderStartTimeoutSeconds { get; init; } = 10;

    public int CompletionTimeoutSeconds { get; init; } = 10;

    public int MaximumSessionMinutes { get; init; } = 60;

    public static bool IsValid(AwsTranscribeOptions options) =>
        !string.IsNullOrWhiteSpace(options.Region) &&
        options.PartialResultsStability is "low" or "medium" or "high" &&
        options.AudioBufferCapacity is >= 1 and <= 64 &&
        options.TranscriptBufferCapacity is >= 1 and <= 256 &&
        options.AudioWriteTimeoutMilliseconds is >= 100 and <= 10_000 &&
        options.ProviderStartTimeoutSeconds is >= 1 and <= 60 &&
        options.CompletionTimeoutSeconds is >= 1 and <= 60 &&
        options.MaximumSessionMinutes is >= 1 and <= 240;
}
