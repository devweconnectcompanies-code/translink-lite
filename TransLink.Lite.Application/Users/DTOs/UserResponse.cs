namespace TransLink.Lite.Application.Users.DTOs;

public sealed class UserResponse
{
    public Guid Id { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public string PreferredLanguage { get; init; } = "en";

    public DateTime CreatedAt { get; init; }
}
