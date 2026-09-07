namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class AwsPollyOptions
{
    public const string SectionName = "AwsPolly";
    public int WorkQueueCapacity { get; init; } = 4;
    public int ProviderTimeoutSeconds { get; init; } = 10;
    public int MaximumTextCharacters { get; init; } = 3_000;
    public int MaximumAudioBytes { get; init; } = 1_048_576;

    public static bool IsValid(AwsPollyOptions options) =>
        options.WorkQueueCapacity is >= 1 and <= 16 &&
        options.ProviderTimeoutSeconds is >= 1 and <= 60 &&
        options.MaximumTextCharacters is >= 100 and <= 3_000 &&
        options.MaximumAudioBytes is >= 65_536 and <= 4_194_304;
}
