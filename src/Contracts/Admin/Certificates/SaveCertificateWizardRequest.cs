
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

/// <summary>Request to create or update a certificate via the linear wizard.</summary>
/// <remarks>
/// E1.2 (#147): the wizard owns the whole scoring configuration. A certificate saved here is
/// scoreable immediately — its skills carry the scale, its bands cover the total range, and each
/// graded skill carries its rubric. There is no separate score-conversion screen.
/// </remarks>
public sealed record SaveCertificateWizardRequest(
    int? CertId,
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "Certificate code is required."), StringLength(40, MinimumLength = 2, ErrorMessage = "Certificate code must be between 2 and 40 characters.")] string Code,
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "Certificate name is required."), StringLength(160, MinimumLength = 2, ErrorMessage = "Certificate name must be between 2 and 160 characters.")] string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    IReadOnlyList<SaveSkillWizardDto> Skills,
    int? MinScaledScore = null,
    int? MaxScaledScore = null,
    int? ScoreStep = null,
    IReadOnlyList<SaveScoreBandWizardDto>? Bands = null,
    IReadOnlyList<SaveGradingCriterionWizardDto>? Criteria = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScoreStep is int step && step <= 0)
        {
            yield return new ValidationResult("Score step must be greater than 0.", ["ScoreStep"]);
        }

        if (MinScaledScore is int min && min < 0)
        {
            yield return new ValidationResult("Minimum scaled score must be 0 or greater.", ["MinScaledScore"]);
        }

        if (MinScaledScore is int minScore && MaxScaledScore is int maxScore && minScore >= maxScore)
        {
            yield return new ValidationResult(
                $"Minimum scaled score ({minScore}) must be less than maximum scaled score ({maxScore}).",
                ["MinScaledScore", "MaxScaledScore"]);
        }
    }
}
