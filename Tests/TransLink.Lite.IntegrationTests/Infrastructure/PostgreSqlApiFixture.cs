using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TransLink.Lite.Infrastructure.Persistence;

namespace TransLink.Lite.IntegrationTests.Infrastructure;

public sealed class PostgreSqlApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("translink_lite_integration")
        .WithUsername("translink_test")
        .WithPassword("test-only-password")
        .WithCleanUp(true)
        .Build();

    public TransLinkWebApplicationFactory Factory { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Factory = new TransLinkWebApplicationFactory(ConnectionString);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException("The integration database has pending migrations.");
        }
    }

    public async Task ResetDatabaseAsync()
    {
        Factory.RealtimeTranscription.Reset();
        await using var scope = Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"TranslationSessions\", \"Users\";");
    }

    public TransLinkWebApplicationFactory CreateFactory(
        int authenticationPermitLimit = 1_000,
        string? realtimeAllowedOrigin = null) =>
        new(ConnectionString, authenticationPermitLimit, realtimeAllowedOrigin);

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}
