using TransLink.Lite.Application.Common.Exceptions;
using TransLink.Lite.Application.TranslationSessions;
using TransLink.Lite.Application.TranslationSessions.DTOs;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.UnitTests.Fakes;

namespace TransLink.Lite.UnitTests.TranslationSessions;

public sealed class TranslationSessionServiceTests
{
    [Fact]
    public async Task CreateAsync_AssociatesAuthenticatedUserId()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeTranslationSessionRepository();
        var service = new TranslationSessionService(repository);
        var request = new CreateTranslationSessionRequest
        {
            Title = " Session ",
            SourceLanguage = " EN ",
            TargetLanguage = " ES ",
        };

        var response = await service.CreateAsync(
            userId,
            request,
            CancellationToken.None);

        Assert.Equal(userId, repository.AddedSession!.UserId);
        Assert.Equal(userId, response.UserId);
        Assert.Equal("Session", repository.AddedSession.Title);
        Assert.Equal("en", repository.AddedSession.SourceLanguage);
        Assert.Equal("es", repository.AddedSession.TargetLanguage);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task GetByUserIdAsync_ScopesQueryByAuthenticatedUserId()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeTranslationSessionRepository
        {
            SessionsByUser = [CreateSession(Guid.NewGuid(), userId)],
        };
        var service = new TranslationSessionService(repository);

        var responses = await service.GetByUserIdAsync(
            userId,
            CancellationToken.None);

        Assert.Equal(userId, repository.RequestedUserId);
        Assert.Single(responses);
        Assert.Equal(userId, responses[0].UserId);
    }

    [Fact]
    public async Task GetByIdAsync_ScopesQueryBySessionAndAuthenticatedUserIds()
    {
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new FakeTranslationSessionRepository
        {
            SessionByIdAndUser = CreateSession(sessionId, userId),
        };
        var service = new TranslationSessionService(repository);

        var response = await service.GetByIdAsync(
            sessionId,
            userId,
            CancellationToken.None);

        Assert.Equal(sessionId, repository.RequestedSessionId);
        Assert.Equal(userId, repository.RequestedUserId);
        Assert.Equal(sessionId, response.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenSessionIsUnavailableOrNonOwned_ThrowsNotFoundException()
    {
        var service = new TranslationSessionService(new FakeTranslationSessionRepository());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None));
    }

    private static TranslationSession CreateSession(Guid id, Guid userId) => new()
    {
        Id = id,
        UserId = userId,
        Title = "Session",
        SourceLanguage = "en",
        TargetLanguage = "es",
        Status = "Draft",
    };
}
