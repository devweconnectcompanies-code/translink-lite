using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Application.Auth.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
}
