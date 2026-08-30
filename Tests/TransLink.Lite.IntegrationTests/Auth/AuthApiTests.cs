using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TransLink.Lite.Application.Auth.DTOs;
using TransLink.Lite.Application.Users.DTOs;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthApiTests
{
    private readonly PostgreSqlApiFixture _fixture;

    public AuthApiTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Register_WithValidRequest_ReturnsSuccessAndJwt()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();

        var auth = await ApiTestClient.RegisterAsync(client);

        Assert.NotEqual(Guid.Empty, auth.UserId);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Null(auth.GetType().GetProperty("PasswordHash"));
    }

    [Theory]
    [InlineData("invalid_email")]
    [InlineData("blank_names")]
    [InlineData("long_name")]
    [InlineData("short_password")]
    [InlineData("long_password")]
    [InlineData("unsupported_language")]
    public async Task Register_WithInvalidRequest_ReturnsValidationProblemDetails(string scenario)
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();
        var request = CreateInvalidRegistration(scenario);

        using var response = await client.PostAsJsonAsync("/api/auth/register", request);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
        Assert.DoesNotContain(GetPassword(request), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_WithDuplicateNormalizedEmail_ReturnsSafeConflictProblemDetails()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();
        const string email = "duplicate@example.invalid";
        await ApiTestClient.RegisterAsync(client, email);
        var duplicate = new RegisterRequest
        {
            Email = " DUPLICATE@EXAMPLE.INVALID ",
            Password = "integration-password",
            FirstName = "Duplicate",
            LastName = "User",
            PreferredLanguage = "en",
        };

        using var response = await client.PostAsJsonAsync("/api/auth/register", duplicate);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal("/api/auth/register", root.GetProperty("instance").GetString());
        Assert.True(root.TryGetProperty("type", out _));
        Assert.True(root.TryGetProperty("title", out _));
        Assert.True(root.TryGetProperty("detail", out _));
        Assert.True(root.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("Postgres", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IX_Users_NormalizedEmail", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithCorrectCredentialsAndDifferentEmailCasing_ReturnsJwt()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();
        const string email = "case-sensitive-display@example.invalid";
        await ApiTestClient.RegisterAsync(client, email);
        var login = new LoginRequest
        {
            Email = email.ToUpperInvariant(),
            Password = "integration-password",
        };

        using var response = await client.PostAsJsonAsync("/api/auth/login", login);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();
        const string email = "wrong-password@example.invalid";
        await ApiTestClient.RegisterAsync(client, email);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = "incorrect-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidRequest_ReturnsBadRequest()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "invalid",
            Password = "integration-password",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidJwt_ReturnsAuthenticatedProfile()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.Factory.CreateClient();
        var auth = await ApiTestClient.RegisterAsync(client);
        ApiTestClient.Authenticate(client, auth.AccessToken);

        using var response = await client.GetAsync("/api/Users/me");
        var profile = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(profile);
        Assert.Equal(auth.UserId, profile.Id);
        Assert.Null(profile.GetType().GetProperty("PasswordHash"));
    }

    [Theory]
    [InlineData("/api/Users/me")]
    [InlineData("/api/TranslationSessions")]
    public async Task ProtectedEndpoint_WithoutJwt_ReturnsUnauthorized(string path)
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static object CreateInvalidRegistration(string scenario) => scenario switch
    {
        "invalid_email" => Registration(email: "invalid", password: "password-must-not-echo"),
        "blank_names" => Registration(firstName: " ", lastName: " "),
        "long_name" => Registration(firstName: new string('a', 101)),
        "short_password" => Registration(password: "short-value"),
        "long_password" => Registration(password: new string('p', 129)),
        "unsupported_language" => Registration(language: "fr"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static object Registration(
        string email = "validation@example.invalid",
        string password = "integration-password",
        string firstName = "First",
        string lastName = "Last",
        string language = "en") => new
    {
        email,
        password,
        firstName,
        lastName,
        preferredLanguage = language,
    };

    private static string GetPassword(object request) =>
        (string)request.GetType().GetProperty("password")!.GetValue(request)!;
}
