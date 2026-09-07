using Microsoft.JSInterop;
using TransLink.Lite.Web.Models;
using TransLink.Lite.Web.Services;

namespace TransLink.Lite.WebTests;

public sealed class SpeechPlaybackQueueTests
{
    [Fact]
    public async Task Queue_IsBoundedAndClearStopsPlayback()
    {
        var js = new BlockingJsRuntime();
        await using var queue = new SpeechPlaybackQueue(js);
        await queue.EnableAsync();
        Assert.True(queue.Enqueue(Segment(0)));
        await js.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var sequence = 1; sequence <= 5; sequence++)
            Assert.True(queue.Enqueue(Segment(sequence)));

        Assert.Equal(SpeechPlaybackQueue.Capacity, queue.QueueDepth);
        await queue.ClearAsync();
        Assert.Equal(0, queue.QueueDepth);
        Assert.Contains("transLinkSpeech.stop", js.Invocations);
    }

    [Fact]
    public async Task InvalidOrEmptySegment_IsRejected()
    {
        await using var queue = new SpeechPlaybackQueue(new BlockingJsRuntime());
        Assert.False(queue.Enqueue(Segment(1) with { Type = "translation.final" }));
        Assert.False(queue.Enqueue(Segment(1) with { AudioPayload = [] }));
    }

    private static RealtimeObserverEvent Segment(long sequence) => new(
        "speech.segment", 3, Guid.NewGuid(), sequence, "result", null, null, "es", null,
        sequence, "mp3", "audio/mpeg", 3, 10, [1, 2, 3]);

    private sealed class BlockingJsRuntime : IJSRuntime
    {
        public List<string> Invocations { get; } = [];
        public TaskCompletionSource PlayStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations.Add(identifier);
            if (identifier != "transLinkSpeech.play") return ValueTask.FromResult(default(TValue)!);
            PlayStarted.TrySetResult();
            return new ValueTask<TValue>(WaitForCancellation<TValue>(cancellationToken));
        }

        private static async Task<TValue> WaitForCancellation<TValue>(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return default!;
        }
    }
}
