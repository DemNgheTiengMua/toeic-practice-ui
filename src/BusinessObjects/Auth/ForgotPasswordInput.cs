using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>The first half of a password reset: the address to send the link to.</summary>
public sealed record ForgotPasswordInput(
    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [property: StringLength(255)]
    string Email);
