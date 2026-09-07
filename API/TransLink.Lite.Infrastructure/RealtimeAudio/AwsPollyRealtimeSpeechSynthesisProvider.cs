using Amazon.Polly;
using Amazon.Polly.Model;
using Microsoft.Extensions.Options;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.Infrastructure.RealtimeAudio;

public sealed class AwsPollyRealtimeSpeechSynthesisProvider(
    IAmazonPolly client,
    IOptions<AwsPollyOptions> options)
    : IRealtimeSpeechSynthesisProvider
{
    public async Task<RealtimeSpeechSynthesisResult> SynthesizeAsync(
        RealtimeSpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.SynthesizeSpeechAsync(new SynthesizeSpeechRequest
            {
                Text = request.Text,
                TextType = TextType.Text,
                LanguageCode = LanguageCode.FindValue(request.LanguageCode),
                VoiceId = VoiceId.FindValue(request.VoiceId),
                Engine = Engine.FindValue(request.Engine),
                OutputFormat = OutputFormat.FindValue(request.AudioFormat),
                SampleRate = request.SampleRate,
            }, cancellationToken);
            await using var audio = response.AudioStream;
            using var buffer = new MemoryStream();
            var chunk = new byte[16_384];
            while (true)
            {
                var count = await audio.ReadAsync(chunk, cancellationToken);
                if (count == 0) break;
                if (buffer.Length + count > options.Value.MaximumAudioBytes)
                    throw new RealtimeSpeechSynthesisException("speech-audio-limit");
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
            return new RealtimeSpeechSynthesisResult(
                buffer.ToArray(), request.AudioFormat,
                response.ContentType ?? "audio/mpeg",
                int.TryParse(request.SampleRate, out var sampleRate) ? sampleRate : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (AmazonPollyException exception)
        {
            throw new RealtimeSpeechSynthesisException(
                exception.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "speech-invalid-request"
                    : "speech-unavailable",
                exception);
        }
        catch (Amazon.Runtime.AmazonClientException exception)
        {
            throw new RealtimeSpeechSynthesisException("speech-connection", exception);
        }
    }
}
