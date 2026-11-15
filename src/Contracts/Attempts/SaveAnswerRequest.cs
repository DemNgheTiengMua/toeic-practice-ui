using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record SaveAnswerRequest(
    int? SelectedOptionId,
    bool IsFlagged,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        FreeResponseBounds.ValidateDraftRequest(ResponseText);
}
