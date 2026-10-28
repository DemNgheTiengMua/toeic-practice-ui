using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>The second half of a password reset: the emailed token and the new password.</summary>
public sealed record ResetPasswordInput(
    [property: Required(ErrorMessage = "The reset link is missing its token.")]
    string Token,

    [property: Required(ErrorMessage = "Choose a new password.")]
    [property: StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string NewPassword);
