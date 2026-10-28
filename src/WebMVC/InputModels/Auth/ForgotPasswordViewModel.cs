using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    public string Email { get; set; } = "";

    /// <summary>True once the request went through: show "link sent" whether or not the account exists.</summary>
    public bool Sent { get; set; }

    public string? FormError { get; set; }
}
