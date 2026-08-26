using TransLink.Lite.Application.Common.Languages;
using TransLink.Lite.Application.Common.Persistence;
using TransLink.Lite.Application.Users.DTOs;
using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Application.Users;

public sealed class UserProfileService : IUserProfileService
{
    private readonly IUserRepository _userRepository;

    public UserProfileService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserResponse?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : MapToResponse(user);
    }

    public async Task<UserResponse?> UpdateAsync(
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PreferredLanguage = SupportedLanguageCatalog.Normalize(request.PreferredLanguage);

        await _userRepository.SaveChangesAsync(cancellationToken);
        return MapToResponse(user);
    }

    private static UserResponse MapToResponse(User user) => new()
    {
        Id = user.Id,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        PreferredLanguage = user.PreferredLanguage,
        CreatedAt = user.CreatedAt,
    };
}
