using Microsoft.EntityFrameworkCore;
using TransLink.Lite.Application.Common.Persistence;
using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Infrastructure.Persistence.Repositories;

public sealed class TranslationSessionRepository : ITranslationSessionRepository
{
    private readonly AppDbContext _context;

    public TranslationSessionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        TranslationSession session,
        CancellationToken cancellationToken)
    {
        await _context.TranslationSessions.AddAsync(session, cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationSession>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await _context.TranslationSessions
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<TranslationSession?> GetByIdAndUserIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken) =>
        _context.TranslationSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.Id == id && session.UserId == userId,
                cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
