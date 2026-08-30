namespace TransLink.Lite.API.Configuration;

public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting:Authentication";

    public const string PolicyName = "authentication";

    public int PermitLimit { get; init; } = 10;

    public int WindowSeconds { get; init; } = 60;

    public int QueueLimit { get; init; }
}
