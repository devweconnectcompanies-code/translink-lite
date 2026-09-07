namespace TransLink.Lite.Application.RealtimeAudio;

public interface IRealtimeTranslationProvider
{
    Task<RealtimeTranslationResult> TranslateAsync(
        RealtimeTranslationRequest request,
        CancellationToken cancellationToken);
}

public sealed class RealtimeTranslationException : Exception
{
    public RealtimeTranslationException(string errorCode, Exception? innerException = null)
        : base("Realtime translation failed.", innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
