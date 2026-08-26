using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Common.Languages;

namespace TransLink.Lite.Application.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SupportedLanguageAttribute : ValidationAttribute
{
    public SupportedLanguageAttribute()
        : base("The {0} field contains an unsupported language code.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not string languageCode
        || SupportedLanguageCatalog.IsSupported(languageCode);
}
