using System.ComponentModel.DataAnnotations;
using TransLink.Lite.Application.Common.Validation;

namespace TransLink.Lite.Application.Auth.DTOs;

public sealed class LoginRequest
{
    [Required]
    [StringLength(ValidationConstants.EmailMaxLength)]
    [NormalizedEmailAddress]
    public required string Email { get; init; }

    [Required]
    [StringLength(ValidationConstants.PasswordMaxLength)]
    public required string Password { get; init; }
}
