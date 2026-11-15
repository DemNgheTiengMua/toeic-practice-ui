using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>The editable profile fields. Validation is the API's server-side authority; the MVC
/// view model mirrors it for instant per-field feedback.</summary>
public sealed record UpdateProfileInput(
    [property: Required(ErrorMessage = "Please tell us your name.")]
    [property: StringLength(70, MinimumLength = 2, ErrorMessage = "Name must be 2–70 characters and contain only letters, spaces, hyphens, or apostrophes.")]
    string FullName,

    [property: StringLength(20, ErrorMessage = "Phone must be 20 characters or fewer.")]
    [property: Phone(ErrorMessage = "That doesn't look like a phone number.")]
    string? Phone,

    [property: Required(ErrorMessage = "Pick a time zone.")]
    string TimeZoneId);
