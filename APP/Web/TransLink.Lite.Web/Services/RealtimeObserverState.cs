using TransLink.Lite.Web.Models;

namespace TransLink.Lite.Web.Services;

public sealed class RealtimeObserverState
{
    public const int MaximumSubtitleLines = 20;
    private readonly List<SubtitleLine> _lines = [];
    public ObserverConnectionStatus Status { get; private set; } = ObserverConnectionStatus.Disconnected;
    public IReadOnlyList<SubtitleLine> Lines => _lines;
    public string? ErrorCode { get; private set; }
    public SpeechPlaybackStatus SpeechStatus { get; private set; } = SpeechPlaybackStatus.Off;

    public void SetStatus(ObserverConnectionStatus status) => Status = status;

    public void Apply(RealtimeObserverEvent message)
    {
        switch (message.Type)
        {
            case "observer.accepted": Status = ObserverConnectionStatus.Live; break;
            case "observer.rejected": Status = ObserverConnectionStatus.Error; ErrorCode = message.Code; break;
            case "session.closed": Reset(ObserverConnectionStatus.Ended); break;
            case "speech.synthesizing": SpeechStatus = SpeechPlaybackStatus.Synthesizing; break;
            case "speech.state": SpeechStatus = message.Enabled == true ? SpeechPlaybackStatus.Ready : SpeechPlaybackStatus.Off; break;
            case "speech.error": SpeechStatus = SpeechPlaybackStatus.Unavailable; ErrorCode = message.Code; break;
            case "translation.error": ErrorCode = message.Code; break;
            case "transcript.final" when message.EventSequence is { } sequence && message.Text is { } text:
                AddOrUpdate(sequence, original: text, translation: null); break;
            case "translation.final" when message.EventSequence is { } sequence && message.Text is { } text:
                AddOrUpdate(sequence, original: null, translation: text); break;
        }
    }

    public void Reset(ObserverConnectionStatus status = ObserverConnectionStatus.Disconnected)
    {
        _lines.Clear(); ErrorCode = null; Status = status; SpeechStatus = SpeechPlaybackStatus.Off;
    }

    public void SetSpeechStatus(SpeechPlaybackStatus status) => SpeechStatus = status;

    private void AddOrUpdate(long sequence, string? original, string? translation)
    {
        var index = _lines.FindIndex(line => line.Sequence == sequence);
        if (index >= 0)
        {
            var current = _lines[index];
            _lines[index] = current with { Original = original ?? current.Original, Translation = translation ?? current.Translation };
            return;
        }
        _lines.Add(new SubtitleLine(sequence, original, translation));
        if (_lines.Count > MaximumSubtitleLines) _lines.RemoveAt(0);
    }
}
