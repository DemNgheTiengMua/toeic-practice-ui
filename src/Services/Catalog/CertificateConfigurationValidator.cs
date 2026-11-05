
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

    /// <summary>Bands must tile the certificate's total range exactly: no gap, no overlap, and both
    /// ends covered. The total range depends on the aggregation rule — <c>sum</c> adds the skill
    /// scores, so two 0–50 skills produce a 0–100 total and bands stopping at 50 would never match
    /// the upper half of real attempts.</summary>
    private static void ValidateBands(SaveCertificateWizardRequest request)
    {
        var bands = request.Bands;
        if (bands is null || bands.Count == 0)
        {
            throw new ScoreConversionException(
                ScoreConversionException.InvalidData,
                "The certificate must define at least one score band, or every placement and mock exam submission for it will be rejected.");
        }

        var skillMin = request.MinScaledScore!.Value;
        var skillMax = request.MaxScaledScore!.Value;
        var (min, max) = TotalRange(request.ScoreAggregationRule, skillMin, skillMax, request.Skills.Count);

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

    /// <summary>
    /// The range a total can actually land in, given the aggregation rule. <c>sum</c> adds the skill
    /// scores; the average rules keep the skill range.
    /// </summary>
    private static (int Min, int Max) TotalRange(
        string? aggregationRule, int skillMin, int skillMax, int skillCount)
    {
        var rule = aggregationRule?.Trim().ToLowerInvariant() ?? "sum";
        var skills = Math.Max(skillCount, 1);
        return rule == "sum" ? (skillMin * skills, skillMax * skills) : (skillMin, skillMax);
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

            // #192: a grid is optional here (the prose above is the legacy standard), but a grid that
            // was started must be finished. Six bands is worse than none: the grader would invent the
            // seventh, which is the inference the descriptor table exists to stop.
            if (criterion.Bands is { Count: > 0 } bands)
            {
                var codes = bands.Select(b => b.Band).ToList();
                // Duplicates first: with a repeat present, the "missing" message would name whichever
                // band the repeat displaced, which points at the wrong problem.
                if (codes.Count != codes.Distinct().Count())
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' names the same descriptor band twice. A criterion is seven rows, one per band 0-6.");
                }
                var outOfRange = codes.Where(b => b > CanonicalRubric.HighestBand).ToList();
                if (outOfRange.Count > 0)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' has descriptor band(s) {string.Join(", ", outOfRange)} outside 0-6. The scale is integers 0-6: 0 is Pre-A1, 6 is C2.");
                }
                var missing = CanonicalRubric.Bands.Where(b => !codes.Contains(b)).ToList();
                if (missing.Count > 0)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' on skill '{criterion.SkillCode}' is missing a descriptor for band(s) "
                        + $"{string.Join(", ", missing)}. Every criterion needs one descriptor at every band 0-6, or the bands it omits have to be invented.");
                }
            }

            // #193: anchors are optional here for the same reason the grid is - every rubric authored
            // before the ticket has none - but a set that was started must be the three calibrated bands.
            // Two anchors is worse than none: the rubric is graded against a floor and a ceiling and no
            // middle, which is exactly where a fluent-but-empty answer sits.
            if (criterion.Anchors is { Count: > 0 } anchors)
            {
                var anchorBands = anchors.Select(a => a.Band).ToList();
                if (anchorBands.Count != anchorBands.Distinct().Count())
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' names the same anchor band twice. A criterion carries one anchor at each of bands 2, 3 and 4.");
                }

                var outOfRange = anchorBands.Except(CanonicalRubric.AnchorBands).ToList();
                if (outOfRange.Count > 0)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' has anchor band(s) {string.Join(", ", outOfRange)} outside 2-4. The anchors are the calibrated benchmarks at bands 2, 3 and 4.");
                }

                var missingAnchors = CanonicalRubric.AnchorBands.Where(b => !anchorBands.Contains(b)).ToList();
                if (missingAnchors.Count > 0)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' on skill '{criterion.SkillCode}' is missing an anchor response for band(s) "
                        + $"{string.Join(", ", missingAnchors)}. A criterion is anchored at bands 2, 3 and 4, or it is not anchored at all.");
                }

                if (anchors.Any(a => string.IsNullOrWhiteSpace(a.ResponseText)))
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Criterion '{criterion.Code}' has an anchor response that is empty. An empty anchor is not a benchmark: it anchors nothing and still counts as written.");
                }
            }
        }
    }
}
