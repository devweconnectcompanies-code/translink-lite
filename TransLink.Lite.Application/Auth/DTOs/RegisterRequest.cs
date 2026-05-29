namespace TransLink.Lite.Application.Auth.DTOs;

public sealed class RegisterRequest
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public string PreferredLanguage { get; init; } = "en";
}
