using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TransLink.Lite.API.Configuration;
using TransLink.Lite.API.ErrorHandling;
using TransLink.Lite.API.HealthChecks;
using TransLink.Lite.API.RealtimeAudio;
using TransLink.Lite.Application.Auth;
using TransLink.Lite.Application.Auth.Interfaces;
using TransLink.Lite.Application.Common.Persistence;
using TransLink.Lite.Application.RealtimeAudio;
using TransLink.Lite.Application.TranslationSessions;
using TransLink.Lite.Application.Users;
using TransLink.Lite.Infrastructure.Auth;
using TransLink.Lite.Infrastructure.Persistence;
using TransLink.Lite.Infrastructure.Persistence.Repositories;
using TransLink.Lite.Infrastructure.RealtimeAudio;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Required configuration 'ConnectionStrings:DefaultConnection' is missing.");
}

var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtSettings = jwtSection.Get<JwtSettings>()
    ?? throw new InvalidOperationException("Required JWT configuration is missing.");

ValidateJwtSettings(jwtSettings);

var authenticationRateLimit = builder.Configuration
    .GetSection(AuthenticationRateLimitOptions.SectionName)
    .Get<AuthenticationRateLimitOptions>() ?? new AuthenticationRateLimitOptions();

ValidateAuthenticationRateLimit(authenticationRateLimit);

var realtimeAudioSection = builder.Configuration.GetSection(RealtimeAudioOptions.SectionName);
var realtimeAudioOptions = realtimeAudioSection.Get<RealtimeAudioOptions>()
    ?? new RealtimeAudioOptions();
ValidateRealtimeAudioOptions(realtimeAudioOptions, builder.Environment);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddOptions<JwtSettings>()
    .Bind(jwtSection)
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer),
        "Required configuration 'Jwt:Issuer' is missing.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience),
        "Required configuration 'Jwt:Audience' is missing.")
    .Validate(settings => Encoding.UTF8.GetByteCount(settings.SecretKey) >= 32,
        "Required configuration 'Jwt:SecretKey' must contain at least 32 bytes.")
    .Validate(settings => settings.ExpirationMinutes > 0,
        "Required configuration 'Jwt:ExpirationMinutes' must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddOptions<RealtimeAudioOptions>()
    .Bind(realtimeAudioSection)
    .Validate(options => RealtimeAudioProtocol.ValidateLimits(ToLimits(options)) is null,
        "RealtimeAudio configuration contains a value outside its supported range.")
    .Validate(options => options.AllowedOrigins.All(IsValidAllowedOrigin),
        "RealtimeAudio:AllowedOrigins must contain exact HTTP(S) or chrome-extension origins.")
    .Validate(options => builder.Environment.IsDevelopment() ||
            builder.Environment.IsEnvironment("IntegrationTesting") ||
            options.AllowedOrigins.Length > 0,
        "RealtimeAudio:AllowedOrigins must contain at least one exact origin outside Development.")
    .ValidateOnStart();

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<ITranslationSessionService, TranslationSessionService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITranslationSessionRepository, TranslationSessionRepository>();
builder.Services.AddSingleton<IRealtimeAudioSessionFactory, ValidationRealtimeAudioSessionFactory>();
builder.Services.AddScoped<RealtimeAudioConnectionHandler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path == RealtimeAudioEndpoint.Path &&
                    context.HttpContext.WebSockets.IsWebSocketRequest)
                {
                    var bearerProtocol = context.HttpContext.WebSockets
                        .WebSocketRequestedProtocols
                        .FirstOrDefault(protocol => protocol.StartsWith(
                            RealtimeAudioProtocol.BearerSubprotocolPrefix,
                            StringComparison.Ordinal));
                    if (bearerProtocol is not null)
                    {
                        context.Token = bearerProtocol[
                            RealtimeAudioProtocol.BearerSubprotocolPrefix.Length..];
                    }
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck<PostgreSqlHealthCheck>(
        "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthenticationRateLimitOptions.PolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = authenticationRateLimit.PermitLimit,
                QueueLimit = authenticationRateLimit.QueueLimit,
                Window = TimeSpan.FromSeconds(authenticationRateLimit.WindowSeconds),
            }));
});

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: \"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\"",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment() &&
    !app.Environment.IsEnvironment("IntegrationTesting"))
{
    app.UseHttpsRedirection();
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        var isRealtimeUpgrade =
            context.Request.Path == RealtimeAudioEndpoint.Path &&
            context.WebSockets.IsWebSocketRequest;
        if (isRealtimeUpgrade)
        {
            app.Logger.LogInformation(
                "Realtime WebSocket upgrade received. Scheme: {Scheme}, Host: {Host}, Path: {Path}, OriginPresent: {OriginPresent}, PublicProtocolRequested: {PublicProtocolRequested}",
                context.Request.Scheme,
                context.Request.Host,
                context.Request.Path,
                context.Request.Headers.Origin.Count > 0,
                context.WebSockets.WebSocketRequestedProtocols.Contains(
                    RealtimeAudioProtocol.WebSocketSubprotocol,
                    StringComparer.Ordinal));
        }

        await next(context);

        if (isRealtimeUpgrade)
        {
            app.Logger.LogInformation(
                "Realtime WebSocket request completed. StatusCode: {StatusCode}, Authenticated: {Authenticated}",
                context.Response.StatusCode,
                context.User.Identity?.IsAuthenticated == true);
        }
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRealtimeAudio();

app.MapHealthChecks("/health", CreateHealthCheckOptions("live"))
    .AllowAnonymous();
app.MapHealthChecks("/health/live", CreateHealthCheckOptions("live"))
    .AllowAnonymous();
app.MapHealthChecks("/health/ready", CreateHealthCheckOptions("ready"))
    .AllowAnonymous();

app.Run();

static HealthCheckOptions CreateHealthCheckOptions(string tag) => new()
{
    Predicate = registration => registration.Tags.Contains(tag),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
};

static void ValidateJwtSettings(JwtSettings settings)
{
    if (string.IsNullOrWhiteSpace(settings.Issuer))
    {
        throw new InvalidOperationException("Required configuration 'Jwt:Issuer' is missing.");
    }

    if (string.IsNullOrWhiteSpace(settings.Audience))
    {
        throw new InvalidOperationException("Required configuration 'Jwt:Audience' is missing.");
    }

    if (Encoding.UTF8.GetByteCount(settings.SecretKey) < 32)
    {
        throw new InvalidOperationException(
            "Required configuration 'Jwt:SecretKey' must contain at least 32 bytes.");
    }

    if (settings.ExpirationMinutes <= 0)
    {
        throw new InvalidOperationException(
            "Required configuration 'Jwt:ExpirationMinutes' must be greater than zero.");
    }
}

static void ValidateAuthenticationRateLimit(AuthenticationRateLimitOptions settings)
{
    if (settings.PermitLimit <= 0)
    {
        throw new InvalidOperationException(
            "Required configuration 'RateLimiting:Authentication:PermitLimit' must be greater than zero.");
    }

    if (settings.WindowSeconds <= 0)
    {
        throw new InvalidOperationException(
            "Required configuration 'RateLimiting:Authentication:WindowSeconds' must be greater than zero.");
    }

    if (settings.QueueLimit < 0)
    {
        throw new InvalidOperationException(
            "Required configuration 'RateLimiting:Authentication:QueueLimit' cannot be negative.");
    }
}

static void ValidateRealtimeAudioOptions(
    RealtimeAudioOptions settings,
    IWebHostEnvironment environment)
{
    var validationError = RealtimeAudioProtocol.ValidateLimits(ToLimits(settings));
    if (validationError is not null)
        throw new InvalidOperationException(
            $"RealtimeAudio configuration is invalid ({validationError}).");
    if (!settings.AllowedOrigins.All(IsValidAllowedOrigin))
        throw new InvalidOperationException(
            "RealtimeAudio:AllowedOrigins must contain exact HTTP(S) or chrome-extension origins.");
    if (!environment.IsDevelopment() &&
        !environment.IsEnvironment("IntegrationTesting") &&
        settings.AllowedOrigins.Length == 0)
        throw new InvalidOperationException(
            "RealtimeAudio:AllowedOrigins must contain at least one exact origin outside Development.");
}

static RealtimeAudioLimits ToLimits(RealtimeAudioOptions settings) => new(
    settings.ProtocolVersion,
    settings.MaxBinaryFrameBytes,
    settings.MaxControlMessageBytes,
    settings.ChunkDurationMs,
    settings.HandshakeTimeoutSeconds,
    settings.IdleTimeoutSeconds,
    settings.MaxProtocolViolations);

static bool IsValidAllowedOrigin(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var origin)) return false;
    if (origin.Scheme is not ("http" or "https" or "chrome-extension")) return false;
    return string.IsNullOrEmpty(origin.Query) &&
        string.IsNullOrEmpty(origin.Fragment) &&
        origin.AbsolutePath == "/";
}

public partial class Program;
