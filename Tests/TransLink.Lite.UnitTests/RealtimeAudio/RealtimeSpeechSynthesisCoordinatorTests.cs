using System.Collections.Concurrent;
using System.Threading.Channels;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class RealtimeSpeechSynthesisCoordinatorTests
{
    [Fact]
    public void VoiceCatalog_MapsSpanishToExplicitNeuralMp3Profile()
    {
        Assert.True(RealtimeSpeechVoiceCatalog.TryGet("es", out var voice));
        Assert.Equal("es-ES", voice.ProviderLanguageCode);
        Assert.Equal("Lucia", voice.VoiceId);
        Assert.Equal("neural", voice.Engine);
        Assert.Equal("mp3", voice.AudioFormat);
    }

    [Fact]
    public async Task NoConsumer_DoesNotSynthesize()
    {
        var fixture = new CoordinatorFixture();
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");

        Assert.False(fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("one")));
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
        Assert.Equal(0, fixture.Provider.Requests);
    }

    [Fact]
    public async Task TranslationFinal_WithMultipleConsumers_SynthesizesOnceAndPublishesAudio()
    {
        var fixture = new CoordinatorFixture();
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        await using var first = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        await using var second = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;

        Assert.True(fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("one")));
        Assert.False(fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("one")));
        await fixture.Registry.WaitForAsync("speech.segment");

        Assert.Equal(1, fixture.Provider.Requests);
        var speech = fixture.Registry.Events.Single(item => item.Type == "speech.segment");
        Assert.Equal("result-one", speech.SourceResultId);
        Assert.NotEmpty(speech.AudioPayload!);
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
    }

    [Theory]
    [InlineData("transcript.partial", "text")]
    [InlineData("transcript.final", "text")]
    [InlineData("translation.final", "")]
    [InlineData("translation.final", "   ")]
    public async Task IneligibleEvents_DoNotSynthesize(string type, string text)
    {
        var fixture = new CoordinatorFixture();
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        await using var consumer = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        var message = Translation("ineligible") with { Type = type, Text = text };

        Assert.False(fixture.Coordinator.EnqueueTranslation(fixture.SessionId, message));
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
        Assert.Equal(0, fixture.Provider.Requests);
    }

    [Fact]
    public void UnsupportedLanguage_DoesNotCreateSpeechSession()
    {
        var fixture = new CoordinatorFixture();
        Assert.False(fixture.Coordinator.RegisterSession(fixture.SessionId, "zz"));
        Assert.Null(fixture.Coordinator.AcquireConsumer(fixture.SessionId));
    }

    [Fact]
    public async Task ProviderFailure_IsPublishedWithoutEndingSession()
    {
        var fixture = new CoordinatorFixture { Provider = { Fail = true } };
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        await using var consumer = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("failure"));

        await fixture.Registry.WaitForAsync("speech.error");
        Assert.Equal("speech-unavailable", fixture.Registry.Events.Last().Code);
        Assert.NotNull(fixture.Coordinator.AcquireConsumer(fixture.SessionId));
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
    }

    [Fact]
    public async Task LastConsumerDisconnect_CancelsActiveSynthesis()
    {
        var fixture = new CoordinatorFixture();
        fixture.Provider.Block = true;
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        var consumer = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("cancel"));
        await fixture.Provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await consumer.DisposeAsync();
        await fixture.Provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
    }

    [Fact]
    public async Task SpeechSegments_AreSynthesizedSequentially()
    {
        var fixture = new CoordinatorFixture();
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        await using var consumer = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("first") with { EventSequence = 1 });
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("second") with { EventSequence = 2 });

        await fixture.Registry.WaitForCountAsync("speech.segment", 2);
        var sequences = fixture.Registry.Events
            .Where(item => item.Type == "speech.segment")
            .Select(item => item.EventSequence).ToArray();
        Assert.Equal([1L, 2L], sequences);
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
    }

    [Fact]
    public async Task ProviderTimeout_IsSanitizedAndSessionRemainsAvailable()
    {
        var fixture = new CoordinatorFixture(providerTimeoutSeconds: 1);
        fixture.Provider.Block = true;
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        await using var consumer = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("timeout"));

        await fixture.Registry.WaitForAsync("speech.error", attempts: 200);
        Assert.Equal("speech-timeout", fixture.Registry.Events.Last().Code);
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
    }

    [Fact]
    public async Task WorkQueue_DropsOldestWholePendingSegmentWhenFull()
    {
        var fixture = new CoordinatorFixture();
        fixture.Provider.Block = true;
        fixture.Coordinator.RegisterSession(fixture.SessionId, "es");
        var consumer = fixture.Coordinator.AcquireConsumer(fixture.SessionId)!;
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("active"));
        await fixture.Provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("one"));
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("two"));
        fixture.Coordinator.EnqueueTranslation(fixture.SessionId, Translation("three"));

        Assert.Equal(1, fixture.Metrics.Snapshot().DroppedSegments);
        await consumer.DisposeAsync();
        await fixture.Coordinator.CompleteAsync(fixture.SessionId);
    }

    private static RealtimeSessionEvent Translation(string suffix) => new(
        "translation.final", 3, Guid.Empty, 1, $"result-{suffix}", "translated text", "en", "es");

    private sealed class CoordinatorFixture
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public RecordingProvider Provider { get; } = new();
        public RecordingRegistry Registry { get; } = new();
        public RealtimeSpeechSynthesisMetrics Metrics { get; } = new();
        public RealtimeSpeechSynthesisCoordinator Coordinator { get; }
        public CoordinatorFixture(int providerTimeoutSeconds = 2) =>
            Coordinator = new(Provider, Registry, new(2, providerTimeoutSeconds, 3_000, 1_048_576), Metrics);
    }

    private sealed class RecordingProvider : IRealtimeSpeechSynthesisProvider
    {
        public int Requests { get; private set; }
        public bool Fail { get; set; }
        public bool Block { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RealtimeSpeechSynthesisResult> SynthesizeAsync(RealtimeSpeechSynthesisRequest request, CancellationToken cancellationToken)
        {
            Requests++; Started.TrySetResult();
            if (Fail) throw new RealtimeSpeechSynthesisException("speech-unavailable");
            if (Block)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            }
            return new([1, 2, 3], "mp3", "audio/mpeg", 24_000);
        }
    }

    private sealed class RecordingRegistry : IRealtimeSessionRegistry
    {
        public ConcurrentQueue<RealtimeSessionEvent> Events { get; } = new();
        public void Publish(Guid sessionId, RealtimeSessionEvent message) => Events.Enqueue(message);
        public Task WaitForAsync(string type, int attempts = 100) => WaitForCountAsync(type, 1, attempts);
        public async Task WaitForCountAsync(string type, int count, int attempts = 100)
        {
            for (var attempt = 0; attempt < attempts && Events.Count(item => item.Type == type) < count; attempt++)
                await Task.Delay(10);
            Assert.True(Events.Count(item => item.Type == type) >= count);
        }
        public bool TryRegisterProducer(Guid sessionId, Guid ownerId, string sourceLanguage, string targetLanguage) => true;
        public IReadOnlyList<ActiveRealtimeSession> GetActiveSessions(Guid ownerId) => [];
        public IRealtimeSessionSubscription? Subscribe(Guid sessionId, Guid ownerId) => null;
        public void Complete(Guid sessionId) { }
    }
}
