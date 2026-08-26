using TransLink.Lite.Application.Common.Exceptions;
using TransLink.Lite.Application.Users;
using TransLink.Lite.Application.Users.DTOs;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.UnitTests.Fakes;

namespace TransLink.Lite.UnitTests.Users;

public sealed class UserProfileServiceTests
{
    [Fact]
    public async Task GetAsync_WithExistingUser_LoadsOnlyRequestedUser()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeUserRepository { UserById = CreateUser(userId) };
        var service = new UserProfileService(repository);

        var response = await service.GetAsync(userId, CancellationToken.None);

        Assert.Equal(userId, repository.RequestedUserId);
        Assert.Equal(userId, response.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithExistingUser_UpdatesOnlyApprovedProfileFields()
    {
        var user = CreateUser(Guid.NewGuid());
        var originalEmail = user.Email;
        var originalNormalizedEmail = user.NormalizedEmail;
        var originalPasswordHash = user.PasswordHash;
        var repository = new FakeUserRepository { UserById = user };
        var service = new UserProfileService(repository);
        var request = new UpdateUserProfileRequest
        {
            FirstName = " Updated ",
            LastName = " Profile ",
            PreferredLanguage = " ES ",
        };

        var response = await service.UpdateAsync(
            user.Id,
            request,
            CancellationToken.None);

        Assert.Equal("Updated", user.FirstName);
        Assert.Equal("Profile", user.LastName);
        Assert.Equal("es", user.PreferredLanguage);
        Assert.Equal(originalEmail, user.Email);
        Assert.Equal(originalNormalizedEmail, user.NormalizedEmail);
        Assert.Equal(originalPasswordHash, user.PasswordHash);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Equal(originalEmail, response.Email);
    }

    [Fact]
    public async Task GetAsync_WhenUserDoesNotExist_ThrowsNotFoundException()
    {
        var service = new UserProfileService(new FakeUserRepository());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(
            Guid.NewGuid(),
            CancellationToken.None));
    }

    private static User CreateUser(Guid id) => new()
    {
        Id = id,
        FirstName = "First",
        LastName = "Last",
        Email = "user@example.com",
        NormalizedEmail = "user@example.com",
        PasswordHash = "stored-hash",
        PreferredLanguage = "en",
    };
}
