using TransLink.Lite.Web.Models;
using TransLink.Lite.Web.Services;

namespace TransLink.Lite.WebTests;

public sealed class RealtimeObserverStateTests
{
    [Fact]
    public void AcceptedAndFinalEvents_UpdateCorrelatedState()
    {
        var state = new RealtimeObserverState();
        state.Apply(Event("observer.accepted"));
        state.Apply(Event("transcript.final", 4, "original"));
        state.Apply(Event("translation.final", 4, "translated"));

        Assert.Equal(ObserverConnectionStatus.Live, state.Status);
        var line = Assert.Single(state.Lines);
        Assert.Equal("original", line.Original);
        Assert.Equal("translated", line.Translation);
    }

    [Fact]
    public void Lines_AreBoundedToTwenty()
    {
        var state = new RealtimeObserverState();
        for (var index = 0; index < 25; index++)
            state.Apply(Event("translation.final", index, $"translation-{index}"));

        Assert.Equal(20, state.Lines.Count);
        Assert.Equal(5, state.Lines[0].Sequence);
        Assert.Equal(24, state.Lines[^1].Sequence);
    }

    [Fact]
    public void SessionEndAndReset_ClearEphemeralContent()
    {
        var state = new RealtimeObserverState();
        state.Apply(Event("translation.final", 1, "translation"));
        state.Apply(Event("session.closed"));

        Assert.Equal(ObserverConnectionStatus.Ended, state.Status);
        Assert.Empty(state.Lines);
        Assert.Null(state.ErrorCode);
        state.Reset();
        Assert.Equal(ObserverConnectionStatus.Disconnected, state.Status);
    }

    [Fact]
    public void RejectionAndTranslationFailure_UseSanitizedState()
    {
        var state = new RealtimeObserverState();
        state.Apply(Event("observer.rejected", code: "session-not-found"));
        Assert.Equal(ObserverConnectionStatus.Error, state.Status);
        Assert.Equal("session-not-found", state.ErrorCode);
        state.Apply(Event("translation.error", code: "translation-connection"));
        Assert.Equal("translation-connection", state.ErrorCode);
    }

    [Fact]
    public void SpeechLifecycle_UsesSanitizedBoundedClientState()
    {
        var state = new RealtimeObserverState();
        state.Apply(Event("speech.synthesizing"));
        Assert.Equal(SpeechPlaybackStatus.Synthesizing, state.SpeechStatus);
        state.Apply(Event("speech.error", code: "speech-unavailable"));
        Assert.Equal(SpeechPlaybackStatus.Unavailable, state.SpeechStatus);
        state.Reset();
        Assert.Equal(SpeechPlaybackStatus.Off, state.SpeechStatus);
    }

    private static RealtimeObserverEvent Event(string type, long? sequence = null, string? text = null, string? code = null) =>
        new(type, 3, Guid.NewGuid(), sequence, "result", text, "en", "es", code);
}
