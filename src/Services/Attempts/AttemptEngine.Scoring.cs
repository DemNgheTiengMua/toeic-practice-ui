using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken = default) =>
        // Both entry points share one behaviour: interpolate between the surrounding rows, round by the skill's step.
        ComputeScaledScoreAsync(certId, skillId, examId, (decimal)rawScore, totalQuestionsInSkill, cancellationToken);

    public static int RoundHalfStep(double average)
    {
        // IELTS official rounding:
        // if decimal is < 0.25 -> .0; >= 0.25 and < 0.75 -> .5; >= 0.75 -> next whole band
        if (average > 10.0)
        {
            double band = average / 10.0;
            double floor = Math.Floor(band);
            double rem = band - floor;
            double roundedBand = rem < 0.25 ? floor : rem < 0.75 ? floor + 0.5 : floor + 1.0;
            return (int)Math.Round(roundedBand * 10.0);
        }
        else
        {
            double floor = Math.Floor(average);
            double rem = average - floor;
            double roundedBand = rem < 0.25 ? floor : rem < 0.75 ? floor + 0.5 : floor + 1.0;
            return (int)Math.Round(roundedBand * 10.0);
        }
    }

    public static int AggregateTotalScore(
        string? scoreAggregationRule,
        IReadOnlyCollection<AttemptSkillScore> skillScores)
    {
        if (skillScores.Count == 0) return 0;

        var average = skillScores.Average(score => (double)score.ScaledScore);
        return scoreAggregationRule?.ToLowerInvariant() switch
        {
            "avg_half_step" => RoundHalfStep(average),
            "avg_whole" => (int)Math.Round(average, MidpointRounding.AwayFromZero),
            "independent" => 0,
            _ => skillScores.Sum(score => score.ScaledScore)
        };
    }

    private async Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        decimal rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken)
    {
        // The scale belongs to the skill; a skill without one is a configuration error, never a guess.
        var skill = await db.Skills
            .AsNoTracking()
            .Where(s => s.SkillId == skillId && s.CertId == certId)
            .Select(s => new { s.Code, s.MinScaledScore, s.MaxScaledScore, s.ScoreStep })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ScoreConversionException(
                ScoreConversionException.SkillScaleNotConfigured,
                $"Skill {skillId} of certificate {certId} does not exist, so it has no score scale.");

        if (skill.MinScaledScore is not int minimum
            || skill.MaxScaledScore is not int maximum
            || skill.ScoreStep is not int step)
        {
            throw new ScoreConversionException(
                ScoreConversionException.SkillScaleNotConfigured,
                $"Skill '{skill.Code}' has no score scale configured (MinScaledScore, MaxScaledScore and ScoreStep must all be set).");
        }

        var scaleRows = await db.ScoreScales
            .AsNoTracking()
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == examId)
            .OrderBy(s => s.RawScore)
            .ToListAsync(cancellationToken);
        if (scaleRows.Count == 0)
        {
            scaleRows = await db.ScoreScales
                .AsNoTracking()
                .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == null)
                .OrderBy(s => s.RawScore)
                .ToListAsync(cancellationToken);
        }

        var (lower, upper) = (scaleRows.LastOrDefault(row => row.RawScore <= rawScore),
                              scaleRows.FirstOrDefault(row => row.RawScore >= rawScore));
        return ProjectOntoScale(
            rawScore,
            lower?.RawScore,
            lower?.ScaledScore,
            upper?.RawScore,
            upper?.ScaledScore,
            minimum,
            maximum,
            step,
            totalQuestionsInSkill);
    }

    /// <summary>
    /// E1.1 (#146): the one score-conversion behaviour. Interpolate linearly between the two rows that
    /// bracket the raw score, then round onto the skill's own grid. With no rows at all, spread the raw
    /// score proportionally across the skill's range instead. <paramref name="minimum"/> is 0..n and
    /// <paramref name="step"/> &gt; 0, so callers must have already rejected an unconfigured skill.
    /// </summary>
    public static int ProjectOntoScale(
        decimal rawScore,
        int? lowerRaw,
        int? lowerScaled,
        int? upperRaw,
        int? upperScaled,
        int minimum,
        int maximum,
        int step,
        int totalQuestionsInSkill = 0)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), step, "ScoreStep must be positive.");

        decimal scaled;
        if (lowerScaled is not int low)
        {
            // No lower row: below the bottom of the table (or no table at all).
            scaled = upperScaled is int top
                ? top
                : totalQuestionsInSkill > 0
                    ? minimum + Math.Clamp(rawScore / totalQuestionsInSkill, 0m, 1m) * (maximum - minimum)
                    : minimum;
        }
        else if (upperScaled is not int high || upperRaw is not int upperRawScore || upperRawScore == lowerRaw)
        {
            scaled = low;
        }
        else
        {
            scaled = low + (rawScore - lowerRaw!.Value) * (high - low) / (upperRawScore - lowerRaw!.Value);
        }

        return (int)Math.Round(scaled / step, MidpointRounding.AwayFromZero) * step;
    }

    private async Task<string?> FindBandCodeAsync(int certId, int totalScore, CancellationToken cancellationToken)
    {
        var bands = await db.ScoreBands
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var matchingBand = bands.FirstOrDefault(b => totalScore >= b.MinTotal && totalScore <= b.MaxTotal);
        if (matchingBand is null && bands.Count > 0)
        {
            matchingBand = totalScore < bands[0].MinTotal
                ? bands[0]
                : bands.LastOrDefault(b => totalScore >= b.MinTotal) ?? bands[^1];
        }

        return matchingBand?.Code;
    }
}
