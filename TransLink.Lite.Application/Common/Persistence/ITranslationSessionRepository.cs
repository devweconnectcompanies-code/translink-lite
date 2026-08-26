using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Application.Common.Persistence;

public interface ITranslationSessionRepository
{
    Task AddAsync(TranslationSession session, CancellationToken cancellationToken);

    Task<IReadOnlyList<TranslationSession>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<TranslationSession?> GetByIdAndUserIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
