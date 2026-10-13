using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>What the registration form collects. Validation lives here so the API and any
/// future caller share one set of rules.</summary>
public sealed record RegisterInput(
    [property: Required(ErrorMessage = "Please tell us your name.")]
    [property: StringLength(120, ErrorMessage = "Name must be 120 characters or fewer.")]
    string FullName,

    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [property: StringLength(255)]
    string Email,

    [property: StringLength(20, ErrorMessage = "Phone must be 20 characters or fewer.")]
    [property: Phone(ErrorMessage = "That doesn't look like a phone number.")]
    string? Phone,

    [property: Required(ErrorMessage = "Choose a password.")]
    [property: StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string Password,

    [property: Range(typeof(bool), "true", "true", ErrorMessage = "Please agree to the terms of use.")]
    bool TermsAccepted);

public sealed record SignInInput(
    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    string Email,

    [property: Required(ErrorMessage = "Password is required.")]
    string Password);

/// <summary>The first half of a password reset: the address to send the link to.</summary>
public sealed record ForgotPasswordInput(
    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [property: StringLength(255)]
    string Email);

/// <summary>The second half of a password reset: the emailed token and the new password.</summary>
public sealed record ResetPasswordInput(
    [property: Required(ErrorMessage = "The reset link is missing its token.")]
    string Token,

    [property: Required(ErrorMessage = "Choose a new password.")]
    [property: StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string NewPassword);

/// <summary>Who someone is once signed in. <paramref name="RoleCode"/> is 'learner' or 'admin'.
/// <paramref name="EmailVerified"/> gates buying credits, never the free loop.</summary>
public sealed record AuthUser(int UserId, string Email, string? FullName, string RoleCode, bool EmailVerified);
