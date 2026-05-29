using TransLink.Lite.Application.Auth.Interfaces;

namespace TransLink.Lite.Infrastructure.Auth;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password);

    public bool VerifyPassword(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
