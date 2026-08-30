using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLink.Lite.Application.Common.Exceptions;
using TransLink.Lite.Application.Common.Persistence;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.Infrastructure.Persistence;
using TransLink.Lite.IntegrationTests.Infrastructure;

namespace TransLink.Lite.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public sealed class DatabaseConstraintTests
{
    private readonly PostgreSqlApiFixture _fixture;

    public DatabaseConstraintTests(PostgreSqlApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveChangesAsync_WithDuplicateNormalizedEmail_MapsDatabaseConstraintToConflict()
    {
        await _fixture.ResetDatabaseAsync();
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var normalizedEmail = $"constraint-{Guid.NewGuid():N}@example.invalid";
        await repository.AddAsync(CreateUser(normalizedEmail), CancellationToken.None);
        await repository.AddAsync(CreateUser(normalizedEmail), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            repository.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_WithMissingUser_RejectsTranslationSessionForeignKey()
    {
        await _fixture.ResetDatabaseAsync();
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.TranslationSessions.Add(new TranslationSession
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Title = "Foreign key verification",
            SourceLanguage = "en",
            TargetLanguage = "es",
            Status = "Draft",
            CreatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DatabaseInitialization_AppliesAllMigrationsWithNonePending()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        Assert.Contains(
            applied,
            migration => migration.EndsWith("StrengthenUserAndTranslationSessionIntegrity"));
        Assert.Empty(pending);
    }

    private static User CreateUser(string normalizedEmail) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "Constraint",
        LastName = "User",
        Email = normalizedEmail,
        NormalizedEmail = normalizedEmail,
        PasswordHash = "test-only-hash",
        PreferredLanguage = "en",
        CreatedAt = DateTime.UtcNow,
    };
}
