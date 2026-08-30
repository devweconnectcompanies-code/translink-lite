using System.Net;
using System.Net.Http.Json;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.RateLimiting;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthenticationRateLimitTests
{
    private readonly PostgreSqlApiFixture _fixture;

    public AuthenticationRateLimitTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AuthenticationRateLimit_WhenPermitLimitIsTwo_RejectsThirdRequest()
    {
        await using var factory = _fixture.CreateFactory(authenticationPermitLimit: 2);
        using var client = factory.CreateClient();
        var invalidRequest = new
        {
            email = "invalid",
            password = "short",
            firstName = "",
            lastName = "",
            preferredLanguage = "unsupported",
        };

        using var first = await client.PostAsJsonAsync("/api/auth/register", invalidRequest);
        using var second = await client.PostAsJsonAsync("/api/auth/register", invalidRequest);
        using var third = await client.PostAsJsonAsync("/api/auth/register", invalidRequest);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
