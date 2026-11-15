using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record SavePlacementDraftRequestDto(
    int QuestionId,
    string? ResponseText) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        FreeResponseBounds.ValidateDraftRequest(ResponseText);
}
