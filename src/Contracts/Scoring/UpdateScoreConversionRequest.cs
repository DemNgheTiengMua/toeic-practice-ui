using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Contracts;

public sealed record UpdateScoreConversionRequest(
    int? ExamId,
    IReadOnlyList<UpdateScoreScaleEntryDto> Scales,
    IReadOnlyList<UpdateScoreBandEntryDto> Bands) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Scales is not null && Scales.Count > 0)
        {
            if (Scales.Any(s => s.RawScore < 0 || s.ScaledScore < 0))
            {
                yield return new ValidationResult("Raw scores and scaled scores must be 0 or greater.", ["Scales"]);
            }

            if (Scales.GroupBy(s => (s.SkillId, s.RawScore)).Any(g => g.Count() > 1))
            {
                yield return new ValidationResult("Duplicate raw scores are not allowed for the same skill.", ["Scales"]);
            }
        }

        if (Bands is not null && Bands.Count > 0)
        {
            if (Bands.Any(b => b.MinTotal < 0 || b.MaxTotal < 0 || b.MinTotal > b.MaxTotal))
            {
                yield return new ValidationResult("Each score band must have non-negative bounds with MinTotal <= MaxTotal.", ["Bands"]);
            }
        }
    }
}
