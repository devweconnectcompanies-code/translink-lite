using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Common.Validation;

namespace TransLink.Lite.Application.Users.DTOs;

public sealed class UpdateUserProfileRequest
{
    [Required]
    [StringLength(ValidationConstants.NameMaxLength, MinimumLength = 1)]
    public required string FirstName { get; init; }

    [Required]
    [StringLength(ValidationConstants.NameMaxLength, MinimumLength = 1)]
    public required string LastName { get; init; }

    [Required]
    [StringLength(ValidationConstants.LanguageCodeMaxLength, MinimumLength = 1)]
    [SupportedLanguage]
    public required string PreferredLanguage { get; init; }
}
