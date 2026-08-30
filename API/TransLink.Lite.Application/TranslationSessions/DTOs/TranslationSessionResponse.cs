namespace TransLink.Lite.Application.TranslationSessions.DTOs;

public sealed class TranslationSessionResponse
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public required string Title { get; init; }

    public required string SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }

    public required string Status { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? EndedAt { get; init; }
}
