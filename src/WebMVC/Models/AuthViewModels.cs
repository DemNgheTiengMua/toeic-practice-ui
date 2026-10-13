using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Please tell us your name.")]
    [StringLength(120, ErrorMessage = "Name must be 120 characters or fewer.")]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    public string Email { get; set; } = "";

    [StringLength(20, ErrorMessage = "Phone must be 20 characters or fewer.")]
    [Phone(ErrorMessage = "That doesn't look like a phone number.")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Please agree to the terms of use.")]
    [Display(Name = "I agree to the terms of use")]
    public bool TermsAccepted { get; set; }

    public string? ReturnUrl { get; set; }

    /// <summary>A form-level error coming back from the API (duplicate email, locked, ...).</summary>
    public string? FormError { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    public string Email { get; set; } = "";

    /// <summary>True once the request went through: show "link sent" whether or not the account exists.</summary>
    public bool Sent { get; set; }

    public string? FormError { get; set; }
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Choose a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Type the new password again.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The two passwords don't match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>The link was used, expired or never existed; offer a new one instead of the form.</summary>
    public bool LinkInvalid { get; set; }

    public string? FormError { get; set; }
}

public sealed class VerifyEmailViewModel
{
    public string Token { get; set; } = "";

    /// <summary>null before the learner confirms; true or false after the API answers.</summary>
    public bool? Verified { get; set; }
}

public sealed class SignInViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }

    /// <summary>A form-level error coming back from the API (bad credentials, locked).</summary>
    public string? FormError { get; set; }

    /// <summary>A success note to show above the form (e.g. "password changed").</summary>
    public string? Notice { get; set; }
}
