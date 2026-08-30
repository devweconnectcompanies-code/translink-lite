using System.Net.Http.Headers;
using System.Net.Http.Json;
using TransLink.Lite.Application.Auth.DTOs;

namespace TransLink.Lite.IntegrationTests.Infrastructure;

internal static class ApiTestClient
{
    public static async Task<AuthResponse> RegisterAsync(
        HttpClient client,
        string? email = null,
        string password = "integration-password")
    {
        var request = new RegisterRequest
        {
            Email = email ?? $"user-{Guid.NewGuid():N}@example.invalid",
            Password = password,
            FirstName = "Integration",
            LastName = "User",
            PreferredLanguage = "en",
        };

        using var response = await client.PostAsJsonAsync("/api/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static void Authenticate(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
