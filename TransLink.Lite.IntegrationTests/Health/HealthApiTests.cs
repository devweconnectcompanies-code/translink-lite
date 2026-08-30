using System.Net;
using System.Net.Http.Json;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.Health;

[Collection(IntegrationTestCollection.Name)]
public sealed class HealthApiTests
{
    private readonly PostgreSqlApiFixture _fixture;

    public HealthApiTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HealthEndpoints_WithAvailablePostgreSql_ReturnHealthy()
    {
        using var client = _fixture.Factory.CreateClient();

        foreach (var path in new[] { "/health", "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", body!.Status);
        }
    }

    [Fact]
    public async Task HealthEndpoints_WhenPostgreSqlIsUnavailable_KeepLiveAndFailReady()
    {
        const string unavailableConnection =
            "Host=127.0.0.1;Port=1;Database=translink_lite_unavailable;" +
            "Username=test;Password=test-only;Timeout=1;Command Timeout=1";
        await using var factory = new TransLinkWebApplicationFactory(unavailableConnection);
        using var client = factory.CreateClient();

        using var liveResponse = await client.GetAsync("/health/live");
        using var readyResponse = await client.GetAsync("/health/ready");
        var live = await liveResponse.Content.ReadFromJsonAsync<HealthResponse>();
        var ready = await readyResponse.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal("Healthy", live!.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal("Unhealthy", ready!.Status);
        Assert.DoesNotContain("127.0.0.1", await readyResponse.Content.ReadAsStringAsync());
    }

    private sealed record HealthResponse(string Status);
}
