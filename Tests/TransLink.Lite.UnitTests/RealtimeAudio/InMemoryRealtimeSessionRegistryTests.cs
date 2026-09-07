using TransLink.Lite.Application.RealtimeAudio;
using TransLink.Lite.Infrastructure.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class InMemoryRealtimeSessionRegistryTests
{
    [Fact]
    public void ActiveSessions_AreOwnerScoped()
    {
        var registry = new InMemoryRealtimeSessionRegistry();
        var owner = Guid.NewGuid();
        registry.TryRegisterProducer(Guid.NewGuid(), owner, "en-US", "es");
        registry.TryRegisterProducer(Guid.NewGuid(), Guid.NewGuid(), "en-US", "es");

        Assert.Single(registry.GetActiveSessions(owner));
    }

    [Fact]
    public async Task Subscribe_RejectsForeignAndUnknownSessions()
    {
        var registry = new InMemoryRealtimeSessionRegistry();
        var sessionId = Guid.NewGuid();
        registry.TryRegisterProducer(sessionId, Guid.NewGuid(), "en-US", "es");

        Assert.Null(registry.Subscribe(sessionId, Guid.NewGuid()));
        Assert.Null(registry.Subscribe(Guid.NewGuid(), Guid.NewGuid()));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_PreservesOrder()
    {
        var registry = new InMemoryRealtimeSessionRegistry();
        var owner = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        registry.TryRegisterProducer(sessionId, owner, "en-US", "es");
        await using var subscription = registry.Subscribe(sessionId, owner)!;

        registry.Publish(sessionId, Event(sessionId, 1));
        registry.Publish(sessionId, Event(sessionId, 2));

        Assert.Equal(1, (await subscription.Events.ReadAsync()).EventSequence);
        Assert.Equal(2, (await subscription.Events.ReadAsync()).EventSequence);
    }

    [Fact]
    public async Task SlowObserver_IsCompletedWithoutAffectingAnotherObserver()
    {
        var registry = new InMemoryRealtimeSessionRegistry();
        var owner = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        registry.TryRegisterProducer(sessionId, owner, "en-US", "es");
        await using var slow = registry.Subscribe(sessionId, owner)!;
        await using var healthy = registry.Subscribe(sessionId, owner)!;

        for (var index = 0; index <= InMemoryRealtimeSessionRegistry.ObserverBufferCapacity; index++)
        {
            registry.Publish(sessionId, Event(sessionId, index));
            if (index < InMemoryRealtimeSessionRegistry.ObserverBufferCapacity)
                await healthy.Events.ReadAsync();
        }

        await Assert.ThrowsAsync<RealtimeObserverOverflowException>(async () =>
        {
            await foreach (var _ in slow.Events.ReadAllAsync()) { }
        });
        Assert.True(healthy.Events.TryRead(out var last));
        Assert.Equal(InMemoryRealtimeSessionRegistry.ObserverBufferCapacity, last.EventSequence);
    }

    [Fact]
    public async Task Complete_EndsObserversAndRemovesSession()
    {
        var registry = new InMemoryRealtimeSessionRegistry();
        var owner = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        registry.TryRegisterProducer(sessionId, owner, "en-US", "es");
        await using var subscription = registry.Subscribe(sessionId, owner)!;

        registry.Complete(sessionId);

        Assert.Equal("session.closed", (await subscription.Events.ReadAsync()).Type);
        Assert.Empty(registry.GetActiveSessions(owner));
        Assert.Null(registry.Subscribe(sessionId, owner));
    }

    private static RealtimeSessionEvent Event(Guid sessionId, long sequence) =>
        new("translation.final", 3, sessionId, sequence, $"result-{sequence}", "text", "en", "es");
}
