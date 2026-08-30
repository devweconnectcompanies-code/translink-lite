using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Common.Normalization;

namespace TransLink.Lite.Application.Common.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NormalizedEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public NormalizedEmailAddressAttribute()
        : base("The {0} field must contain a valid email address.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not string email)
        {
            return true;
        }

        var normalizedEmail = EmailNormalizer.Normalize(email);

        return normalizedEmail.Length <= ValidationConstants.EmailMaxLength
            && !email.Any(char.IsControl)
            && EmailAddressValidator.IsValid(normalizedEmail);
    }
}
