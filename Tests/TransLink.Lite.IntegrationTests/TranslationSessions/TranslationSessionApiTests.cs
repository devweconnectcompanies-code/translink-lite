using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TransLink.Lite.Application.TranslationSessions.DTOs;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.TranslationSessions;

[Collection(IntegrationTestCollection.Name)]
public sealed class TranslationSessionApiTests
{
    private readonly PostgreSqlApiFixture _fixture;

    public TranslationSessionApiTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Sessions_AreCreatedListedAndRetrievedOnlyByOwner()
    {
        await _fixture.ResetDatabaseAsync();
        using var userAClient = _fixture.Factory.CreateClient();
        using var userBClient = _fixture.Factory.CreateClient();
        var userA = await ApiTestClient.RegisterAsync(userAClient);
        var userB = await ApiTestClient.RegisterAsync(userBClient);
        ApiTestClient.Authenticate(userAClient, userA.AccessToken);
        ApiTestClient.Authenticate(userBClient, userB.AccessToken);

        var sessionA = await CreateSessionAsync(userAClient, "User A session");
        var sessionB = await CreateSessionAsync(userBClient, "User B session");

        Assert.Equal(userA.UserId, sessionA.UserId);
        Assert.Equal(userB.UserId, sessionB.UserId);

        var listA = await userAClient.GetFromJsonAsync<List<TranslationSessionResponse>>(
            "/api/TranslationSessions");
        var listB = await userBClient.GetFromJsonAsync<List<TranslationSessionResponse>>(
            "/api/TranslationSessions");

        Assert.NotNull(listA);
        Assert.Single(listA);
        Assert.Equal(sessionA.Id, listA[0].Id);
        Assert.NotNull(listB);
        Assert.Single(listB);
        Assert.Equal(sessionB.Id, listB[0].Id);

        using var ownResponse = await userAClient.GetAsync($"/api/TranslationSessions/{sessionA.Id}");
        using var nonOwnedResponse = await userAClient.GetAsync($"/api/TranslationSessions/{sessionB.Id}");
        var nonOwnedBody = await nonOwnedResponse.Content.ReadAsStringAsync();
        using var nonOwnedJson = JsonDocument.Parse(nonOwnedBody);

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nonOwnedResponse.StatusCode);
        Assert.Equal("application/problem+json", nonOwnedResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, nonOwnedJson.RootElement.GetProperty("status").GetInt32());
        Assert.True(nonOwnedJson.RootElement.TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData("", "en", "es")]
    [InlineData("Session", "en", "EN")]
    public async Task CreateSession_WithInvalidRequest_ReturnsValidationProblemDetails(
        string title,
        string sourceLanguage,
        string targetLanguage)
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();
        var user = await ApiTestClient.RegisterAsync(client);
        ApiTestClient.Authenticate(client, user.AccessToken);

        using var response = await client.PostAsJsonAsync("/api/TranslationSessions", new
        {
            title,
            sourceLanguage,
            targetLanguage,
            userId = Guid.NewGuid(),
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task RemovedUsersCollectionEndpoint_ReturnsNotFound(string method)
    {
        using var client = _fixture.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/Users");
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<TranslationSessionResponse> CreateSessionAsync(
        HttpClient client,
        string title)
    {
        using var response = await client.PostAsJsonAsync("/api/TranslationSessions", new
        {
            title,
            sourceLanguage = "en",
            targetLanguage = "es",
            userId = Guid.NewGuid(),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TranslationSessionResponse>())!;
    }
}
