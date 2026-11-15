using System.Text.RegularExpressions;

namespace Contracts;

/// <summary>
/// Canonical validation and normalization rules for learner profile fields, shared across
/// <see cref="UpdateProfileRequest"/> (Tier 2 API filter) and <c>AuthService</c> (Tier 3 domain invariant).
/// </summary>
public static partial class ProfileValidation
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 70;
    public const string FullNamePattern = @"^[\p{L}\p{M}]+(?:['’\-\. ][\p{L}\p{M}]+)*$";
    public const string FullNameRequiredMessage = "Please tell us your name.";
    public const string FullNameFormatMessage =
        "Name must be 2–70 characters and contain only letters, spaces, hyphens, or apostrophes.";

    public const int PhoneMaxLength = 20;
    public const string PhonePattern = @"^\+?[\d\s\-\(\)\.]{7,20}$";
    public const string PhoneFormatMessage = "That doesn't look like a phone number.";
    public const string PhoneLengthMessage = "Phone must be 20 characters or fewer.";

    public const string TimeZoneRequiredMessage = "Pick a time zone.";
    public const string TimeZoneInvalidMessage = "Pick a time zone from the list.";

    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(FullNamePattern)]
    private static partial Regex FullNameRegex();

    [GeneratedRegex(PhonePattern)]
    private static partial Regex PhoneRegex();

    /// <summary>Trims leading/trailing spaces and collapses redundant internal ASCII spaces.</summary>
    public static string NormalizeFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        return MultiSpaceRegex().Replace(fullName.Trim(), " ");
    }

    public static bool TryValidateFullName(string? fullName, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            errorMessage = FullNameRequiredMessage;
            return false;
        }

        if (fullName.Any(char.IsControl))
        {
            errorMessage = FullNameFormatMessage;
            return false;
        }

        var normalized = NormalizeFullName(fullName);
        if (normalized.Length < FullNameMinLength || normalized.Length > FullNameMaxLength || !FullNameRegex().IsMatch(normalized))
        {
            errorMessage = FullNameFormatMessage;
            return false;
        }

        errorMessage = null;
        return true;
    }

    public static bool TryValidatePhone(string? phone, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            errorMessage = null;
            return true;
        }

        var trimmed = phone.Trim();
        if (trimmed.Length > PhoneMaxLength)
        {
            errorMessage = PhoneLengthMessage;
            return false;
        }

        if (!PhoneRegex().IsMatch(trimmed) || trimmed.Count(char.IsDigit) < 7)
        {
            errorMessage = PhoneFormatMessage;
            return false;
        }

        errorMessage = null;
        return true;
    }

    public static bool TryValidateTimeZone(string? timeZoneId, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            errorMessage = TimeZoneRequiredMessage;
            return false;
        }

        if (!TimeZones.Contains(timeZoneId))
        {
            errorMessage = TimeZoneInvalidMessage;
            return false;
        }

        errorMessage = null;
        return true;
    }
}
