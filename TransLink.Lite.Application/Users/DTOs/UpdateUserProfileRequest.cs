namespace TransLink.Lite.Application.Users.DTOs;

public sealed class UpdateUserProfileRequest
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string PreferredLanguage { get; init; } = "en";
}
