using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

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
