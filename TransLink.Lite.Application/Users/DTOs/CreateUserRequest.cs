namespace TransLink.Lite.Application.Users.DTOs;

public sealed class CreateUserRequest
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public string PreferredLanguage { get; init; } = "en";
}
