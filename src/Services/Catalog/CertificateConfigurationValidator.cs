
using Contracts;

namespace Services;

/// <summary>
/// E1.2 (#147): the five invariants a certificate must satisfy before it is saved. Previously they
/// were enforced nowhere — the certificate wizard wrote only Certificate/Skill/Section, and the
/// score-conversion page validated its own table separately, so a certificate could exist with no
/// bands at all and then fail every placement submission on CK_Placement_DoneHasResult.
/// <para>
/// Each message names the offending skill, band or gap, because the admin has to be able to fix it
/// from the text alone.
/// </para>
/// </summary>
public static class CertificateConfigurationValidator
{
    public static void Validate(SaveCertificateWizardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateScale(request);
        ValidateBands(request);
        ValidateCriteria(request);
    }

    /// <summary>Min &lt; Max &gt; 0, and every skill that will be scored needs a usable step.</summary>
    private static void ValidateScale(SaveCertificateWizardRequest request)
    {
        if (request.MinScaledScore is not int min || request.MaxScaledScore is not int max || request.ScoreStep is not int step)
        {
            throw new ScoreConversionException(
                ScoreConversionException.SkillScaleNotConfigured,
                "The certificate's scoring scale is incomplete: MinScaledScore, MaxScaledScore and ScoreStep must all be set.");
        }

        if (step <= 0)
        {
            throw new ScoreConversionException(
                ScoreConversionException.InvalidData,
                $"ScoreStep must be greater than zero (got {step}).");
        }

        if (min >= max)
        {
            throw new ScoreConversionException(
                ScoreConversionException.InvalidBandRange,
                $"MinScaledScore ({min}) must be less than MaxScaledScore ({max}).");
        }
    }

    /// <summary>Bands must tile [min, max] exactly: no gap, no overlap, and both ends covered.</summary>
    private static void ValidateBands(SaveCertificateWizardRequest request)
    {
        var bands = request.Bands;
        if (bands is null || bands.Count == 0)
        {
            throw new ScoreConversionException(
                ScoreConversionException.InvalidData,
                "The certificate must define at least one score band, or every placement and mock exam submission for it will be rejected.");
        }

        var min = request.MinScaledScore!.Value;
        var max = request.MaxScaledScore!.Value;

        foreach (var band in bands)
        {
            if (string.IsNullOrWhiteSpace(band.Code) || string.IsNullOrWhiteSpace(band.Name))
            {
                throw new ScoreConversionException(
                    ScoreConversionException.InvalidData,
                    "Every band needs a code and a name.");
            }

            if (band.MinTotal > band.MaxTotal)
            {
                throw new ScoreConversionException(
                    ScoreConversionException.InvalidBandRange,
                    $"Band '{band.Name}' has a maximum ({band.MaxTotal}) below its minimum ({band.MinTotal}).");
            }
        }

        var ordered = bands.OrderBy(b => b.MinTotal).ToList();

        if (ordered[0].MinTotal > min)
        {
            throw new ScoreConversionException(
                ScoreConversionException.InvalidBandRange,
                $"The bands leave a gap at the bottom: nothing covers {min}-{ordered[0].MinTotal - 1}.");
        }

        if (ordered[^1].MaxTotal < max)
        {
            throw new ScoreConversionException(
                ScoreConversionException.InvalidBandRange,
                $"The bands leave a gap at the top: nothing covers {ordered[^1].MaxTotal + 1}-{max}.");
        }

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var current = ordered[i];
            var next = ordered[i + 1];

            if (next.MinTotal <= current.MaxTotal + 1 && next.MinTotal <= current.MaxTotal)
            {
                throw new ScoreConversionException(
                    ScoreConversionException.OverlappingBands,
                    $"Bands '{current.Name}' and '{next.Name}' overlap: [{current.MinTotal}-{current.MaxTotal}] and [{next.MinTotal}-{next.MaxTotal}].");
            }

            if (next.MinTotal > current.MaxTotal + 1)
            {
                throw new ScoreConversionException(
                    ScoreConversionException.InvalidBandRange,
                    $"The bands leave a gap between '{current.Name}' and '{next.Name}': nothing covers {current.MaxTotal + 1}-{next.MinTotal - 1}.");
            }
        }
    }

    /// <summary>Every rubric criterion must carry the description the grader is told to follow.</summary>
    private static void ValidateCriteria(SaveCertificateWizardRequest request)
    {
        if (request.Criteria is null)
        {
            return;
        }

        foreach (var criterion in request.Criteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.Code) || string.IsNullOrWhiteSpace(criterion.Name))
            {
                throw new ScoreConversionException(
                    ScoreConversionException.InvalidData,
                    "Every grading criterion needs a code and a name.");
            }

            if (string.IsNullOrWhiteSpace(criterion.RubricDescription))
            {
                throw new ScoreConversionException(
                    ScoreConversionException.InvalidData,
                    $"Criterion '{criterion.Code}' on skill '{criterion.SkillCode}' has no rubric description. The grader cannot score against a rubric nobody wrote down.");
            }
        }
    }
}
