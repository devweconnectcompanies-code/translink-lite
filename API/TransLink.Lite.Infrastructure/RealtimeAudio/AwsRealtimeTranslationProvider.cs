using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.Translate;
using Amazon.Translate.Model;
using Microsoft.Extensions.Logging;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class AwsRealtimeTranslationProvider : IRealtimeTranslationProvider
{
    private const int MaximumTextBytes = 10_000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly IAmazonTranslate _client;
    private readonly ILogger<AwsRealtimeTranslationProvider> _logger;

    public AwsRealtimeTranslationProvider(
        IAmazonTranslate client,
        ILogger<AwsRealtimeTranslationProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<RealtimeTranslationResult> TranslateAsync(
        RealtimeTranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text) ||
            Encoding.UTF8.GetByteCount(request.Text) > MaximumTextBytes)
            throw new RealtimeTranslationException("translation-rejected");

        if (string.Equals(
                request.SourceLanguage, request.TargetLanguage,
                StringComparison.OrdinalIgnoreCase))
            return new(request.Text, request.SourceLanguage, request.TargetLanguage);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var response = await _client.TranslateTextAsync(new TranslateTextRequest
            {
                Text = request.Text,
                SourceLanguageCode = request.SourceLanguage,
                TargetLanguageCode = request.TargetLanguage,
            }, timeout.Token);
            return new(
                response.TranslatedText,
                response.SourceLanguageCode ?? request.SourceLanguage,
                response.TargetLanguageCode ?? request.TargetLanguage);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RealtimeTranslationException("translation-timeout", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var mapped = MapFailure(exception);
            _logger.LogWarning(
                "Realtime translation request failed. Category: {Category}",
                mapped.ErrorCode);
            throw mapped;
        }
    }

    private static RealtimeTranslationException MapFailure(Exception exception) => exception switch
    {
        UnsupportedLanguagePairException => new("translation-unsupported-language", exception),
        InvalidRequestException or TextSizeLimitExceededException =>
            new("translation-rejected", exception),
        TooManyRequestsException or ServiceUnavailableException or
            InternalServerException => new("translation-connection", exception),
        AmazonServiceException serviceException when
            serviceException.StatusCode == HttpStatusCode.Forbidden =>
            new("translation-rejected", exception),
        AmazonClientException => new("translation-connection", exception),
        RealtimeTranslationException mapped => mapped,
        _ => new("translation-failed", exception),
    };
}
