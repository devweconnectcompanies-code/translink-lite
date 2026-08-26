namespace TransLink.Lite.Application.Common.Normalization;

public static class EmailNormalizer
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
