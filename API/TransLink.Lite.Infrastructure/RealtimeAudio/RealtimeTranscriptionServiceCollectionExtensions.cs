using Amazon;
using Amazon.TranscribeStreaming;
using Amazon.Translate;
using Amazon.Polly;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public static class RealtimeTranscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddAwsRealtimeTranscription(
        this IServiceCollection services,
        AwsTranscribeOptions settings,
        AwsPollyOptions pollySettings)
    {
        if (!AwsTranscribeOptions.IsValid(settings))
        {
            throw new InvalidOperationException(
                "AwsTranscribe configuration is missing or invalid.");
        }
        if (!AwsPollyOptions.IsValid(pollySettings))
            throw new InvalidOperationException("AwsPolly configuration is missing or invalid.");

        var region = RegionEndpoint.EnumerableAllRegions.FirstOrDefault(candidate =>
            string.Equals(candidate.SystemName, settings.Region, StringComparison.Ordinal));
        if (region is null)
        {
            throw new InvalidOperationException(
                "AwsTranscribe configuration is missing or invalid.");
        }

        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(Options.Create(pollySettings));
        services.AddSingleton<IAmazonTranscribeStreaming>(_ =>
            new AmazonTranscribeStreamingClient(
                new AmazonTranscribeStreamingConfig
                {
                    RegionEndpoint = region,
                }));
        services.AddSingleton<IAmazonTranslate>(_ =>
            new AmazonTranslateClient(new AmazonTranslateConfig
            {
                RegionEndpoint = region,
            }));
        services.AddSingleton<IAmazonPolly>(_ => new AmazonPollyClient(new AmazonPollyConfig
        {
            RegionEndpoint = region,
        }));
        services.AddSingleton<IRealtimeTranslationProvider, AwsRealtimeTranslationProvider>();
        services.AddSingleton<IRealtimeSpeechSynthesisProvider, AwsPollyRealtimeSpeechSynthesisProvider>();
        services.AddSingleton<IRealtimeSessionRegistry, InMemoryRealtimeSessionRegistry>();
        services.AddSingleton(new RealtimeSpeechSynthesisLimits(
            pollySettings.WorkQueueCapacity,
            pollySettings.ProviderTimeoutSeconds,
            pollySettings.MaximumTextCharacters,
            pollySettings.MaximumAudioBytes));
        services.AddSingleton<RealtimeSpeechSynthesisMetrics>();
        services.AddSingleton<IRealtimeSpeechSynthesisCoordinator, RealtimeSpeechSynthesisCoordinator>();
        services.AddSingleton<IRealtimeSpeechTranscriptionSessionFactory,
            AwsRealtimeSpeechTranscriptionSessionFactory>();

        return services;
    }
}
