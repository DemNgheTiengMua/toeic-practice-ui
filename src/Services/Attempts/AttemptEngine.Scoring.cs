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
    public async Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken = default)
    {
        // 1. Try exam-specific exact match
        var scale = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == examId && s.RawScore == rawScore)
            .Select(s => (int?)s.ScaledScore)
            .FirstOrDefaultAsync(cancellationToken);

        if (scale.HasValue) return scale.Value;

        // 2. Try default scale (ExamId == null) exact match
        scale = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == null && s.RawScore == rawScore)
            .Select(s => (int?)s.ScaledScore)
            .FirstOrDefaultAsync(cancellationToken);

        if (scale.HasValue) return scale.Value;

        // 3. Fallback to closest available score scale row for this skill
        var allRows = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && (s.ExamId == examId || s.ExamId == null))
            .OrderByDescending(s => s.ExamId)
            .ThenBy(s => s.RawScore)
            .ToListAsync(cancellationToken);

        if (allRows.Count > 0)
        {
            var closest = allRows.OrderBy(r => Math.Abs(r.RawScore - rawScore)).First();
            return closest.ScaledScore;
        }

        // 4. Default proportional fallback if no score scales seeded
        var certCode = await db.Certificates
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);
        var (minimum, maximum, step) = GetFallbackScale(certCode);
        if (totalQuestionsInSkill <= 0) return minimum;
        double fraction = Math.Clamp((double)rawScore / totalQuestionsInSkill, 0.0, 1.0);
        return minimum + (int)Math.Round(
            (fraction * (maximum - minimum)) / step,
            MidpointRounding.AwayFromZero) * step;
    }

    private static (int Minimum, int Maximum, int Step) GetFallbackScale(string? certCode) =>
        certCode?.ToUpperInvariant() switch
        {
            "IELTS" => (10, 90, 5),
            "VSTEP" => (10, 100, 1),
            _ => (5, 495, 5)
        };

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
        if (rawScore == decimal.Truncate(rawScore))
        {
            return await ComputeScaledScoreAsync(
                certId,
                skillId,
                examId,
                (int)rawScore,
                totalQuestionsInSkill,
                cancellationToken);
        }

        var scaleRows = await db.ScoreScales
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == examId)
            .OrderBy(s => s.RawScore)
            .ToListAsync(cancellationToken);
        if (scaleRows.Count == 0)
        {
            scaleRows = await db.ScoreScales
                .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == null)
                .OrderBy(s => s.RawScore)
                .ToListAsync(cancellationToken);
        }

        var certCode = await db.Certificates
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .SingleAsync(cancellationToken);
        var step = GetFallbackScale(certCode).Step;
        if (scaleRows.Count == 0)
        {
            var (minimum, maximum, fallbackStep) = GetFallbackScale(certCode);
            if (totalQuestionsInSkill <= 0) return minimum;
            var fraction = Math.Clamp((double)rawScore / totalQuestionsInSkill, 0.0, 1.0);
            return minimum + (int)Math.Round(
                (fraction * (maximum - minimum)) / fallbackStep,
                MidpointRounding.AwayFromZero) * fallbackStep;
        }

        var lower = scaleRows.LastOrDefault(row => row.RawScore <= rawScore);
        var upper = scaleRows.FirstOrDefault(row => row.RawScore >= rawScore);
        var scaled = lower is null
            ? scaleRows[0].ScaledScore
            : upper is null
                ? scaleRows[^1].ScaledScore
                : lower.RawScore == upper.RawScore
                    ? lower.ScaledScore
                    : lower.ScaledScore
                        + (rawScore - lower.RawScore)
                        * (upper.ScaledScore - lower.ScaledScore)
                        / (upper.RawScore - lower.RawScore);

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
