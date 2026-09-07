using TransLink.Lite.Infrastructure.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class AwsPollyOptionsTests
{
    [Fact]
    public void Defaults_AreValidAndBounded() => Assert.True(AwsPollyOptions.IsValid(new()));

    [Theory]
    [InlineData(0, 10, 3000, 1048576)]
    [InlineData(4, 0, 3000, 1048576)]
    [InlineData(4, 10, 3001, 1048576)]
    [InlineData(4, 10, 3000, 4194305)]
    public void UnsafeLimits_AreRejected(int queue, int timeout, int characters, int bytes) =>
        Assert.False(AwsPollyOptions.IsValid(new()
        {
            WorkQueueCapacity = queue,
            ProviderTimeoutSeconds = timeout,
            MaximumTextCharacters = characters,
            MaximumAudioBytes = bytes,
        }));
}
