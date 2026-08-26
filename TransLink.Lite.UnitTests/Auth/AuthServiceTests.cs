using TransLink.Lite.Application.Auth;
using TransLink.Lite.Application.Auth.DTOs;
using TransLink.Lite.Application.Common.Exceptions;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.UnitTests.Fakes;

namespace TransLink.Lite.UnitTests.Auth;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task RegisterAsync_WithNewEmail_NormalizesAndChecksCanonicalEmail()
    {
        var repository = new FakeUserRepository();
        var service = CreateService(repository);

        await service.RegisterAsync(CreateRegisterRequest(" User+Tag@Example.com "), CancellationToken.None);

        Assert.Equal("user+tag@example.com", repository.RequestedNormalizedEmail);
        Assert.Equal("user+tag@example.com", repository.AddedUser!.NormalizedEmail);
        Assert.Equal("User+Tag@Example.com", repository.AddedUser.Email);
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_HashesPasswordAndDoesNotExposeHash()
    {
        var repository = new FakeUserRepository();
        var passwordHasher = new FakePasswordHasher { HashResult = "stored-hash" };
        var service = CreateService(repository, passwordHasher);

        var response = await service.RegisterAsync(
            CreateRegisterRequest("user@example.com", "plain-password"),
            CancellationToken.None);

        Assert.Equal("plain-password", passwordHasher.PasswordToHash);
        Assert.Equal("stored-hash", repository.AddedUser!.PasswordHash);
        Assert.Null(response.GetType().GetProperty("PasswordHash"));
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_ThrowsConflictException()
    {
        var repository = new FakeUserRepository { EmailExists = true };
        var passwordHasher = new FakePasswordHasher();
        var service = CreateService(repository, passwordHasher);

        await Assert.ThrowsAsync<ConflictException>(() => service.RegisterAsync(
            CreateRegisterRequest("Existing@Example.com"),
            CancellationToken.None));

        Assert.Equal("existing@example.com", repository.RequestedNormalizedEmail);
        Assert.Null(repository.AddedUser);
        Assert.Null(passwordHasher.PasswordToHash);
    }

    [Fact]
    public async Task LoginAsync_WithDifferentEmailCasing_UsesNormalizedEmail()
    {
        var user = CreateUser();
        var repository = new FakeUserRepository { UserByEmail = user };
        var service = CreateService(repository);

        var response = await service.LoginAsync(
            new LoginRequest { Email = " USER@EXAMPLE.COM ", Password = "valid-password" },
            CancellationToken.None);

        Assert.Equal("user@example.com", repository.RequestedNormalizedEmail);
        Assert.NotNull(response);
        Assert.Equal(user.Id, response.UserId);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ReturnsNull()
    {
        var repository = new FakeUserRepository { UserByEmail = CreateUser() };
        var passwordHasher = new FakePasswordHasher { VerificationResult = false };
        var service = CreateService(repository, passwordHasher);

        var response = await service.LoginAsync(
            new LoginRequest { Email = "user@example.com", Password = "wrong-password" },
            CancellationToken.None);

        Assert.Null(response);
        Assert.Equal("wrong-password", passwordHasher.PasswordToVerify);
    }

    [Fact]
    public async Task LoginAsync_WhenUserDoesNotExist_ReturnsNullWithoutHashVerification()
    {
        var passwordHasher = new FakePasswordHasher();
        var service = CreateService(new FakeUserRepository(), passwordHasher);

        var response = await service.LoginAsync(
            new LoginRequest { Email = "missing@example.com", Password = "valid-password" },
            CancellationToken.None);

        Assert.Null(response);
        Assert.Null(passwordHasher.PasswordToVerify);
    }

    private static AuthService CreateService(
        FakeUserRepository repository,
        FakePasswordHasher? passwordHasher = null) => new(
        repository,
        passwordHasher ?? new FakePasswordHasher(),
        new FakeJwtTokenService());

    private static RegisterRequest CreateRegisterRequest(
        string email,
        string password = "valid-password") => new()
    {
        Email = email,
        Password = password,
        FirstName = " First ",
        LastName = " Last ",
        PreferredLanguage = " EN ",
    };

    private static User CreateUser() => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "First",
        LastName = "Last",
        Email = "user@example.com",
        NormalizedEmail = "user@example.com",
        PasswordHash = "stored-hash",
        PreferredLanguage = "en",
    };
}
