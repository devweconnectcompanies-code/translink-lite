using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransLink.Lite.Infrastructure.Persistence;

namespace TransLink.Lite.API.HealthChecks;

public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<PostgreSqlHealthCheck> _logger;

    public PostgreSqlHealthCheck(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<PostgreSqlHealthCheck> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "PostgreSQL readiness check failed.");
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }
}
