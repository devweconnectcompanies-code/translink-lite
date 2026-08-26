using TransLink.Lite.Application.Auth.Interfaces;
using TransLink.Lite.Application.Common.Persistence;
using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.UnitTests.Fakes;

internal sealed class FakeUserRepository : IUserRepository
{
    public bool EmailExists { get; set; }

    public User? UserByEmail { get; set; }

    public User? UserById { get; set; }

    public string? RequestedNormalizedEmail { get; private set; }

    public Guid? RequestedUserId { get; private set; }

    public User? AddedUser { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public Task<bool> ExistsByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        RequestedNormalizedEmail = normalizedEmail;
        return Task.FromResult(EmailExists);
    }

    public Task<User?> GetByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        RequestedNormalizedEmail = normalizedEmail;
        return Task.FromResult(UserByEmail);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        RequestedUserId = id;
        return Task.FromResult(UserById);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        AddedUser = user;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTranslationSessionRepository : ITranslationSessionRepository
{
    public IReadOnlyList<TranslationSession> SessionsByUser { get; set; } = [];

    public TranslationSession? SessionByIdAndUser { get; set; }

    public TranslationSession? AddedSession { get; private set; }

    public Guid? RequestedUserId { get; private set; }

    public Guid? RequestedSessionId { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public Task AddAsync(TranslationSession session, CancellationToken cancellationToken)
    {
        AddedSession = session;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TranslationSession>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        RequestedUserId = userId;
        return Task.FromResult(SessionsByUser);
    }

    public Task<TranslationSession?> GetByIdAndUserIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        RequestedSessionId = id;
        RequestedUserId = userId;
        return Task.FromResult(SessionByIdAndUser);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string HashResult { get; set; } = "hashed-password";

    public bool VerificationResult { get; set; } = true;

    public string? PasswordToHash { get; private set; }

    public string? PasswordToVerify { get; private set; }

    public string? HashToVerify { get; private set; }

    public string HashPassword(string password)
    {
        PasswordToHash = password;
        return HashResult;
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        PasswordToVerify = password;
        HashToVerify = passwordHash;
        return VerificationResult;
    }
}

internal sealed class FakeJwtTokenService : IJwtTokenService
{
    public string Token { get; set; } = "test-access-token";

    public User? TokenUser { get; private set; }

    public string GenerateAccessToken(User user)
    {
        TokenUser = user;
        return Token;
    }
}
