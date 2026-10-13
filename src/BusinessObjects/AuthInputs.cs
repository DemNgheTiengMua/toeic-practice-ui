using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>What the registration form collects. These attributes are the API's server-side
/// authority; the MVC view models mirror them for instant per-field feedback on the form.</summary>
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

/// <summary>Who someone is once signed in. <paramref name="Role"/> is one of Roles (Contracts).</summary>
public sealed record AuthUser(int UserId, string Email, string? FullName, string Role);

/// <summary>The editable profile fields. Validation is the API's server-side authority; the MVC
/// view model mirrors it for instant per-field feedback.</summary>
public sealed record UpdateProfileInput(
    [property: Required(ErrorMessage = "Please tell us your name.")]
    [property: StringLength(120, ErrorMessage = "Name must be 120 characters or fewer.")]
    string FullName,

    [property: StringLength(20, ErrorMessage = "Phone must be 20 characters or fewer.")]
    [property: Phone(ErrorMessage = "That doesn't look like a phone number.")]
    string? Phone,

    [property: Required(ErrorMessage = "Pick a time zone.")]
    string TimeZoneId);

public sealed record ChangePasswordInput(
    [property: Required(ErrorMessage = "Enter your current password.")]
    string CurrentPassword,

    [property: Required(ErrorMessage = "Choose a new password.")]
    [property: StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string NewPassword);
