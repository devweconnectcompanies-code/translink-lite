namespace TransLink.Lite.Web.Models;

public sealed record ActiveRealtimeSession(Guid SessionId, string SourceLanguage, string TargetLanguage, DateTimeOffset StartedAt);
public sealed record RealtimeObserverEvent(string? Type, int ProtocolVersion, Guid? SessionId, long? EventSequence, string? SourceResultId, string? Text, string? SourceLanguage, string? TargetLanguage, string? Code, long? SpeechSequence = null, string? AudioFormat = null, string? ContentType = null, int? AudioByteLength = null, long? SynthesisDurationMilliseconds = null, byte[]? AudioPayload = null, bool? Enabled = null);
public sealed record SubtitleLine(long Sequence, string? Original, string? Translation);

public enum ObserverConnectionStatus { Disconnected, Discovering, Connecting, Live, Ended, Error }
public enum SpeechPlaybackStatus { Off, Ready, Synthesizing, Playing, Delayed, Unavailable }
