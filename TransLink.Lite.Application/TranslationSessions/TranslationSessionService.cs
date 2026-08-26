using TransLink.Lite.Application.Common.Languages;
using TransLink.Lite.Application.Common.Persistence;
using TransLink.Lite.Application.TranslationSessions.DTOs;
using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Application.TranslationSessions;

public sealed class TranslationSessionService : ITranslationSessionService
{
    private readonly ITranslationSessionRepository _translationSessionRepository;

    public TranslationSessionService(ITranslationSessionRepository translationSessionRepository)
    {
        _translationSessionRepository = translationSessionRepository;
    }

    public async Task<TranslationSessionResponse> CreateAsync(
        Guid userId,
        CreateTranslationSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = new TranslationSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = request.Title.Trim(),
            SourceLanguage = SupportedLanguageCatalog.Normalize(request.SourceLanguage),
            TargetLanguage = SupportedLanguageCatalog.Normalize(request.TargetLanguage),
            Status = "Draft",
            CreatedAt = DateTime.UtcNow,
        };

        await _translationSessionRepository.AddAsync(session, cancellationToken);
        await _translationSessionRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(session);
    }

    public async Task<IReadOnlyList<TranslationSessionResponse>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var sessions = await _translationSessionRepository.GetByUserIdAsync(userId, cancellationToken);
        return sessions.Select(MapToResponse).ToList();
    }

    public async Task<TranslationSessionResponse?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var session = await _translationSessionRepository.GetByIdAndUserIdAsync(
            id,
            userId,
            cancellationToken);

        return session is null ? null : MapToResponse(session);
    }

    private static TranslationSessionResponse MapToResponse(TranslationSession session) => new()
    {
        Id = session.Id,
        UserId = session.UserId,
        Title = session.Title,
        SourceLanguage = session.SourceLanguage,
        TargetLanguage = session.TargetLanguage,
        Status = session.Status,
        CreatedAt = session.CreatedAt,
        StartedAt = session.StartedAt,
        EndedAt = session.EndedAt,
    };
}
