using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record ChangePasswordInput(
    [property: Required(ErrorMessage = "Enter your current password.")]
    string CurrentPassword,

    [property: Required(ErrorMessage = "Choose a new password.")]
    [property: StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string NewPassword);
