namespace TransLink.Lite.Application.RealtimeAudio;

public sealed class RealtimeSpeechSynthesisMetrics
{
    private long _requests;
    private long _characters;
    private long _segments;
    private long _failures;
    private long _audioBytes;
    private long _droppedSegments;
    private long _totalSynthesisMilliseconds;
    private long _activeConsumers;

    public void ConsumerAdded() => Interlocked.Increment(ref _activeConsumers);
    public void ConsumerRemoved() => Interlocked.Decrement(ref _activeConsumers);
    public void RequestStarted(int characters) { Interlocked.Increment(ref _requests); Interlocked.Add(ref _characters, characters); }
    public void SegmentCompleted(int bytes, long milliseconds) { Interlocked.Increment(ref _segments); Interlocked.Add(ref _audioBytes, bytes); Interlocked.Add(ref _totalSynthesisMilliseconds, milliseconds); }
    public void Failed() => Interlocked.Increment(ref _failures);
    public void Dropped() => Interlocked.Increment(ref _droppedSegments);

    public RealtimeSpeechSynthesisMetricsSnapshot Snapshot() => new(
        Volatile.Read(ref _requests), Volatile.Read(ref _characters),
        Volatile.Read(ref _segments), Volatile.Read(ref _failures),
        Volatile.Read(ref _audioBytes), Volatile.Read(ref _droppedSegments),
        Volatile.Read(ref _totalSynthesisMilliseconds), Volatile.Read(ref _activeConsumers));
}

public sealed record RealtimeSpeechSynthesisMetricsSnapshot(
    long Requests, long Characters, long Segments, long Failures,
    long AudioBytes, long DroppedSegments, long TotalSynthesisMilliseconds,
    long ActiveConsumers);
