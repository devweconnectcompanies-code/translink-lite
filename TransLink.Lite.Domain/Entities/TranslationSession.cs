namespace TransLink.Lite.Domain.Entities;

public class TranslationSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string SourceLanguage { get; set; } = string.Empty;

    public string TargetLanguage { get; set; } = string.Empty;

    public string Status { get; set; } = "Draft";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }
}
