using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record UpdateProfileRequest(string FullName, string? Phone, string TimeZoneId) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ProfileValidation.TryValidateFullName(FullName, out var fullNameError))
        {
            yield return new ValidationResult(fullNameError, [nameof(FullName)]);
        }

        if (!ProfileValidation.TryValidatePhone(Phone, out var phoneError))
        {
            yield return new ValidationResult(phoneError, [nameof(Phone)]);
        }

        if (!ProfileValidation.TryValidateTimeZone(TimeZoneId, out var tzError))
        {
            yield return new ValidationResult(tzError, [nameof(TimeZoneId)]);
        }
    }
}
