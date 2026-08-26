using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Common.Validation;

namespace TransLink.Lite.Application.Auth.DTOs;

public sealed class RegisterRequest
{
    [Required]
    [StringLength(ValidationConstants.NameMaxLength, MinimumLength = 1)]
    public required string FirstName { get; init; }

    [Required]
    [StringLength(ValidationConstants.NameMaxLength, MinimumLength = 1)]
    public required string LastName { get; init; }

    [Required]
    [StringLength(ValidationConstants.EmailMaxLength)]
    [NormalizedEmailAddress]
    public required string Email { get; init; }

    [Required]
    [StringLength(
        ValidationConstants.PasswordMaxLength,
        MinimumLength = ValidationConstants.PasswordMinLength)]
    public required string Password { get; init; }

    [Required]
    [StringLength(ValidationConstants.LanguageCodeMaxLength, MinimumLength = 1)]
    [SupportedLanguage]
    public required string PreferredLanguage { get; init; }
}
