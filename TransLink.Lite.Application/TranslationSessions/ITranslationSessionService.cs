using TransLink.Lite.Application.TranslationSessions.DTOs;

namespace TransLink.Lite.Application.TranslationSessions;

public interface ITranslationSessionService
{
    Task<TranslationSessionResponse> CreateAsync(
        Guid userId,
        CreateTranslationSessionRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranslationSessionResponse>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<TranslationSessionResponse?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);
}
