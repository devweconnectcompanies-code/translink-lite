namespace TransLink.Lite.Application.RealtimeAudio;

public sealed record RealtimeSpeechSynthesisRequest(
    string Text,
    string TargetLanguage,
    string LanguageCode,
    string VoiceId,
    string Engine,
    string AudioFormat,
    string SampleRate);

public sealed record RealtimeSpeechSynthesisResult(
    byte[] Audio,
    string AudioFormat,
    string ContentType,
    int? SampleRateHz);

public interface IRealtimeSpeechSynthesisProvider
{
    Task<RealtimeSpeechSynthesisResult> SynthesizeAsync(
        RealtimeSpeechSynthesisRequest request,
        CancellationToken cancellationToken);
}

public sealed class RealtimeSpeechSynthesisException(
    string errorCode,
    Exception? innerException = null) : Exception(errorCode, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
