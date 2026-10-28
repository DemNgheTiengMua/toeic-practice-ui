using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

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
