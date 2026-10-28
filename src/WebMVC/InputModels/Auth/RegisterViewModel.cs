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
