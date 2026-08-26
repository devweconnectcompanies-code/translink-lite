using TransLink.Lite.Application.Users.DTOs;

namespace TransLink.Lite.Application.Users;

public interface IUserProfileService
{
    Task<UserResponse?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserResponse?> UpdateAsync(
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken);
}
