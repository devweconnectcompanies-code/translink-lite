using Amazon;
using Amazon.TranscribeStreaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public static class RealtimeTranscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddAwsRealtimeTranscription(
        this IServiceCollection services,
        AwsTranscribeOptions settings)
    {
        if (!AwsTranscribeOptions.IsValid(settings))
        {
            throw new InvalidOperationException(
                "AwsTranscribe configuration is missing or invalid.");
        }

        var region = RegionEndpoint.EnumerableAllRegions.FirstOrDefault(candidate =>
            string.Equals(candidate.SystemName, settings.Region, StringComparison.Ordinal));
        if (region is null)
        {
            throw new InvalidOperationException(
                "AwsTranscribe configuration is missing or invalid.");
        }

        services.AddSingleton(Options.Create(settings));
        services.AddSingleton<IAmazonTranscribeStreaming>(_ =>
            new AmazonTranscribeStreamingClient(
                new AmazonTranscribeStreamingConfig
                {
                    RegionEndpoint = region,
                }));
        services.AddSingleton<IRealtimeSpeechTranscriptionSessionFactory,
            AwsRealtimeSpeechTranscriptionSessionFactory>();

        return services;
    }
}
