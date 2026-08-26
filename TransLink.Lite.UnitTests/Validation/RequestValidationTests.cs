using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Auth.DTOs;
using TransLink.Lite.Application.TranslationSessions.DTOs;

namespace TransLink.Lite.UnitTests.Validation;

public sealed class RequestValidationTests
{
    [Fact]
    public void RegisterRequest_WithValidValues_IsValid()
    {
        var request = CreateRegisterRequest();

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void RegisterRequest_WithInvalidEmail_IsRejected()
    {
        var request = CreateRegisterRequest(email: "invalid-email");

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("Email"));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void RegisterRequest_WithPasswordOutsideLimits_IsRejected(int length)
    {
        var request = CreateRegisterRequest(password: new string('a', length));

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("Password"));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public void RegisterRequest_WithPasswordAtApprovedLimits_IsValid(int length)
    {
        var request = CreateRegisterRequest(password: new string('a', length));

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterRequest_WithEmptyName_IsRejected(string firstName)
    {
        var request = CreateRegisterRequest(firstName: firstName);

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("FirstName"));
    }

    [Fact]
    public void RegisterRequest_WithNameOverLimit_IsRejected()
    {
        var request = CreateRegisterRequest(lastName: new string('a', 101));

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("LastName"));
    }

    [Fact]
    public void RegisterRequest_WithUnsupportedLanguage_IsRejected()
    {
        var request = CreateRegisterRequest(preferredLanguage: "fr");

        Assert.Contains(Validate(request), result => result.MemberNames.Contains("PreferredLanguage"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData(" ES ")]
    public void RegisterRequest_WithSupportedLanguage_IsValid(string language)
    {
        var request = CreateRegisterRequest(preferredLanguage: language);

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CreateTranslationSessionRequest_WithSameLanguages_IsRejected()
    {
        var request = new CreateTranslationSessionRequest
        {
            Title = "Session",
            SourceLanguage = " EN ",
            TargetLanguage = "en",
        };

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains("SourceLanguage")
            && result.MemberNames.Contains("TargetLanguage"));
    }

    private static RegisterRequest CreateRegisterRequest(
        string email = "user@example.com",
        string password = "valid-password",
        string firstName = "First",
        string lastName = "Last",
        string preferredLanguage = "en") => new()
    {
        Email = email,
        Password = password,
        FirstName = firstName,
        LastName = lastName,
        PreferredLanguage = preferredLanguage,
    };

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }
}
