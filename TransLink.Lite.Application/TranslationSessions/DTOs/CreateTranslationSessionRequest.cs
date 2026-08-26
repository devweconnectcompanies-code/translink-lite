using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Common.Languages;
using TransLink.Lite.Application.Common.Validation;

namespace TransLink.Lite.Application.TranslationSessions.DTOs;

public sealed class CreateTranslationSessionRequest : IValidatableObject
{
    [Required]
    [StringLength(ValidationConstants.TranslationSessionTitleMaxLength, MinimumLength = 1)]
    public required string Title { get; init; }

    [Required]
    [StringLength(ValidationConstants.LanguageCodeMaxLength, MinimumLength = 1)]
    [SupportedLanguage]
    public required string SourceLanguage { get; init; }

    [Required]
    [StringLength(ValidationConstants.LanguageCodeMaxLength, MinimumLength = 1)]
    [SupportedLanguage]
    public required string TargetLanguage { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.Equals(
                SupportedLanguageCatalog.Normalize(SourceLanguage),
                SupportedLanguageCatalog.Normalize(TargetLanguage),
                StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                "SourceLanguage and TargetLanguage must be different.",
                [nameof(SourceLanguage), nameof(TargetLanguage)]);
        }
    }
}
