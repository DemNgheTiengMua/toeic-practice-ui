using System.ComponentModel.DataAnnotations;

namespace WebMVC.Models;

/// <summary>The profile form. Mirrors BusinessObjects.UpdateProfileInput so the browser gets
/// instant per-field feedback; the API validates the same rules server-side.</summary>
public sealed class ProfileViewModel
{
    [Required(ErrorMessage = "Please tell us your name.")]
    [StringLength(120, ErrorMessage = "Name must be 120 characters or fewer.")]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [StringLength(20, ErrorMessage = "Phone must be 20 characters or fewer.")]
    [Phone(ErrorMessage = "That doesn't look like a phone number.")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Pick a time zone.")]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = Contracts.TimeZones.DefaultId;

    /// <summary>The shortlist of IANA ids the picker offers (UI-52), plus the current value if it
    /// isn't on the list so the picker never silently discards it.</summary>
    public IReadOnlyList<string> TimeZoneOptions { get; set; } = Contracts.TimeZones.Ids;

    public string? SuccessMessage { get; set; }

    public string? FormError { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Choose a new password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";

    public string? SuccessMessage { get; set; }

    public string? FormError { get; set; }
}
