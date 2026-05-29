namespace TransLink.Lite.Application.TranslationSessions.DTOs;

public sealed class CreateTranslationSessionRequest
{
    public required string Title { get; init; }

    public required string SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }
}
