namespace TransLink.Lite.Application.Auth.DTOs;

public sealed class AuthResponse
{
    public Guid UserId { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string PreferredLanguage { get; init; } = "en";

    public required string AccessToken { get; init; }
}
