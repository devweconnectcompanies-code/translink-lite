using System.Threading.Channels;
using Microsoft.JSInterop;
using TransLink.Lite.Web.Models;

namespace TransLink.Lite.Web.Services;

public sealed class SpeechPlaybackQueue(IJSRuntime jsRuntime) : IAsyncDisposable
{
    public const int Capacity = 3;
    private readonly Channel<RealtimeObserverEvent> _queue = Channel.CreateBounded<RealtimeObserverEvent>(
        new BoundedChannelOptions(Capacity) { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _playback = new();
    private Task? _worker;
    private int _disposed;
    public int QueueDepth { get; private set; }
    public Func<SpeechPlaybackStatus, Task>? OnStatusChanged { get; set; }

    public async Task EnableAsync()
    {
        _worker ??= ProcessAsync();
        await jsRuntime.InvokeVoidAsync("transLinkSpeech.unlock");
        await NotifyAsync(SpeechPlaybackStatus.Ready);
    }

    public bool Enqueue(RealtimeObserverEvent segment)
    {
        if (segment.Type != "speech.segment" || segment.AudioPayload is not { Length: > 0 }) return false;
        if (!_queue.Writer.TryWrite(segment))
        {
            if (_queue.Reader.TryRead(out _)) QueueDepth--;
            if (!_queue.Writer.TryWrite(segment)) return false;
        }
        QueueDepth++;
        return true;
    }

    public async Task ClearAsync()
    {
        await _playback.CancelAsync();
        _playback.Dispose();
        _playback = new CancellationTokenSource();
        while (_queue.Reader.TryRead(out _)) { }
        QueueDepth = 0;
        await jsRuntime.InvokeVoidAsync("transLinkSpeech.stop");
        await NotifyAsync(SpeechPlaybackStatus.Off);
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var segment in _queue.Reader.ReadAllAsync(_lifetime.Token))
            {
                QueueDepth = Math.Max(0, QueueDepth - 1);
                await NotifyAsync(SpeechPlaybackStatus.Playing);
                try
                {
                    using var stream = new MemoryStream(segment.AudioPayload!, writable: false);
                    using var reference = new DotNetStreamReference(stream);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _playback.Token);
                    await jsRuntime.InvokeVoidAsync(
                        "transLinkSpeech.play", linked.Token, reference, segment.ContentType ?? "audio/mpeg");
                    await NotifyAsync(SpeechPlaybackStatus.Ready);
                }
                catch (OperationCanceledException) { }
                catch (JSException) { await NotifyAsync(SpeechPlaybackStatus.Unavailable); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        await _lifetime.CancelAsync();
        await ClearAsync();
        if (_worker is not null) await _worker;
        _playback.Dispose();
        _lifetime.Dispose();
    }

    private Task NotifyAsync(SpeechPlaybackStatus status) =>
        OnStatusChanged?.Invoke(status) ?? Task.CompletedTask;
}
