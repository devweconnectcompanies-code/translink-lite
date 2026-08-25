using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TransLink.Lite.Application.Auth;
using TransLink.Lite.Application.Auth.Interfaces;
using TransLink.Lite.Infrastructure.Auth;
using TransLink.Lite.Infrastructure.Persistence;

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

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

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
    });

builder.Services.AddAuthorization();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "TransLink Lite API",
    timestamp = DateTime.UtcNow
}));

app.Run();

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
