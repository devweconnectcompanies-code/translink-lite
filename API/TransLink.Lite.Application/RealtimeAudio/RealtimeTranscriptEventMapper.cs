namespace TransLink.Lite.Application.RealtimeAudio;

public static class RealtimeTranscriptEventMapper
{
    public static RealtimeTranscriptEvent Map(
        Guid sessionId,
        long eventSequence,
        SpeechTranscriptionResult result) =>
        new(
            result.IsPartial ? "transcript.partial" : "transcript.final",
            RealtimeAudioProtocol.CurrentVersion,
            sessionId,
            eventSequence,
            result.ResultId,
            result.Text,
            !result.IsPartial,
            ToMilliseconds(result.StartTime),
            ToMilliseconds(result.EndTime),
            result.SourceLanguage);

    private static long? ToMilliseconds(TimeSpan? value) =>
        value is null ? null : checked((long)value.Value.TotalMilliseconds);
}
