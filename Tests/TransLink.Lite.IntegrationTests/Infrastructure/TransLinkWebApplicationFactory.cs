using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransLink.Lite.Infrastructure.Persistence;
using TransLink.Lite.Application.RealtimeAudio;

namespace TransLink.Lite.IntegrationTests.Infrastructure;

public sealed class TransLinkWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestJwtSecret = "integration-test-signing-key-with-at-least-32-bytes";
    private readonly string _connectionString;
    private readonly int _authenticationPermitLimit;
    private readonly string? _realtimeAllowedOrigin;
    public FakeRealtimeSpeechTranscriptionSessionFactory RealtimeTranscription { get; } = new();

    public TransLinkWebApplicationFactory(
        string connectionString,
        int authenticationPermitLimit = 1_000,
        string? realtimeAllowedOrigin = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.Equals(
                connection.Database,
                "translink_lite_dev",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Integration tests must never use the development database.");
        }

        _connectionString = connectionString;
        _authenticationPermitLimit = authenticationPermitLimit;
        _realtimeAllowedOrigin = realtimeAllowedOrigin;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTesting");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Jwt:SecretKey", TestJwtSecret);
        builder.UseSetting(
            "RateLimiting:Authentication:PermitLimit",
            _authenticationPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Authentication:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:Authentication:QueueLimit", "0");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:SecretKey"] = TestJwtSecret,
                ["RateLimiting:Authentication:PermitLimit"] = _authenticationPermitLimit.ToString(),
                ["RateLimiting:Authentication:WindowSeconds"] = "60",
                ["RateLimiting:Authentication:QueueLimit"] = "0",
            };
            if (_realtimeAllowedOrigin is not null)
            {
                settings["RealtimeAudio:AllowedOrigins:0"] = _realtimeAllowedOrigin;
            }

            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_connectionString));
            services.RemoveAll<IRealtimeSpeechTranscriptionSessionFactory>();
            services.AddSingleton<IRealtimeSpeechTranscriptionSessionFactory>(RealtimeTranscription);
        });
    }
}
