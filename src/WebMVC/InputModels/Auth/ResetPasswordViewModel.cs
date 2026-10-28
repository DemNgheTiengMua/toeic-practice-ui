using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

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
