using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

public record SaveWritingDraftRequest(
    int QuestionId,
    string? ResponseText
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        FreeResponseBounds.ValidateDraftRequest(ResponseText);
}
