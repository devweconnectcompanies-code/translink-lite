namespace TransLink.Lite.Application.RealtimeAudio;

public sealed record RealtimeSpeechVoice(
    string TargetLanguage,
    string ProviderLanguageCode,
    string VoiceId,
    string Engine,
    string AudioFormat,
    string SampleRate);

public static class RealtimeSpeechVoiceCatalog
{
    private static readonly IReadOnlyDictionary<string, RealtimeSpeechVoice> Voices =
        new Dictionary<string, RealtimeSpeechVoice>(StringComparer.OrdinalIgnoreCase)
        {
            ["es"] = new("es", "es-ES", "Lucia", "neural", "mp3", "24000"),
        };

    public static bool TryGet(string targetLanguage, out RealtimeSpeechVoice voice) =>
        Voices.TryGetValue(targetLanguage, out voice!);
}
