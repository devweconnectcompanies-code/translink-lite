using TransLink.Lite.Infrastructure.RealtimeAudio;

namespace TransLink.Lite.UnitTests.RealtimeAudio;

public sealed class AwsTranscribeOptionsTests
{
    [Fact]
    public void IsValid_WithBoundedDefaults_ReturnsTrue()
    {
        var options = new AwsTranscribeOptions { Region = "us-east-1" };

        Assert.True(AwsTranscribeOptions.IsValid(options));
    }

    [Theory]
    [InlineData("", "medium", 8)]
    [InlineData("us-east-1", "unknown", 8)]
    [InlineData("us-east-1", "medium", 0)]
    [InlineData("us-east-1", "medium", 65)]
    public void IsValid_WithInvalidConfiguration_ReturnsFalse(
        string region,
        string stability,
        int audioBufferCapacity)
    {
        var options = new AwsTranscribeOptions
        {
            Region = region,
            PartialResultsStability = stability,
            AudioBufferCapacity = audioBufferCapacity,
        };

        Assert.False(AwsTranscribeOptions.IsValid(options));
    }
}
