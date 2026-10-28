using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record SignInInput(
    [property: Required(ErrorMessage = "Email is required.")]
    [property: EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    string Email,

    [property: Required(ErrorMessage = "Password is required.")]
    string Password);
