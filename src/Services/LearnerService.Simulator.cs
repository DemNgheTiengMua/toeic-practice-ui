using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private async Task<SimulatorConfig?> BuildSimulatorConfigAsync(
        int userId,
        int certId,
        TargetReadiness? targetReadiness,
        CancellationToken cancellationToken)
    {
        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.MinTotal)
            .ThenBy(b => b.DisplayOrder)
            .Select(b => new ScoreBandThreshold(b.Code, b.Name, b.MinTotal, b.MaxTotal))
            .ToListAsync(cancellationToken);

        if (bands.Count == 0)
        {
            return null;
        }

        var allSkills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .ToListAsync(cancellationToken);

        if (allSkills.Count == 0)
        {
            return null;
        }

        var scales = await db.ScoreScales
            .AsNoTracking()
            .Where(s => s.CertId == certId && s.SkillId != null)
            .Select(s => new { SkillId = s.SkillId!.Value, s.ScaledScore })
            .ToListAsync(cancellationToken);

        var latestSkillScores = await db.AttemptSkillScores
            .AsNoTracking()
            .Where(s => s.CertId == certId
                && s.Attempt.UserId == userId
                && s.Attempt.Status == AttemptStatus.Graded.ToCode())
            .OrderByDescending(s => s.Attempt.SubmittedAt ?? s.Attempt.StartedAt)
            .ThenByDescending(s => s.AttemptId)
            .Select(s => new { s.SkillId, s.ScaledScore })
            .ToListAsync(cancellationToken);

        var participatingIds = scales.Select(s => s.SkillId)
            .Concat(latestSkillScores.Select(s => s.SkillId))
            .ToHashSet();

        var participatingSkills = participatingIds.Count > 0
            ? allSkills
                .Where(s => participatingIds.Contains(s.SkillId)
                    || s.Modality == Modality.Listening.ToCode()
                    || s.Modality == Modality.Reading.ToCode())
                .ToList()
            : allSkills;

        if (participatingSkills.Count == 0)
        {
            participatingSkills = allSkills;
        }

        int maxTotalScore = bands[^1].MaxTotal;
        int minTotalScore = bands[0].MinTotal;
        int skillCount = participatingSkills.Count;

        var latestBySkill = new Dictionary<int, int>();
        foreach (var score in latestSkillScores)
        {
            latestBySkill.TryAdd(score.SkillId, score.ScaledScore);
        }

        var scalesBySkill = scales
            .GroupBy(s => s.SkillId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ScaledScore).Distinct().OrderBy(x => x).ToList());

        var simulatorSkills = new List<SimulatorSkill>(skillCount);
        foreach (var skill in participatingSkills)
        {
            int minScaled;
            int maxScaled;
            int step;

            if (scalesBySkill.TryGetValue(skill.SkillId, out var skillScales) && skillScales.Count >= 2)
            {
                minScaled = skillScales[0];
                maxScaled = skillScales[^1];
                step = maxScaled >= 20 && skillScales.All(v => v % 5 == 0) ? 5 : 1;
            }
            else
            {
                maxScaled = Math.Max(1, maxTotalScore / skillCount);
                step = maxScaled >= 20 && maxScaled % 5 == 0 ? 5 : 1;
                int rawMin = minTotalScore > 0 ? minTotalScore / skillCount : 0;
                minScaled = rawMin > 0
                    ? Math.Max(step, (rawMin / step) * step)
                    : 0;
                if (minScaled >= maxScaled)
                {
                    minScaled = 0;
                }
            }

            int midpoint = step > 0
                ? minScaled + (((maxScaled - minScaled) / step / 2) * step)
                : (minScaled + maxScaled) / 2;

            int baseScaled = latestBySkill.TryGetValue(skill.SkillId, out var gradedScore)
                ? gradedScore
                : midpoint;

            simulatorSkills.Add(new SimulatorSkill(
                SkillId: skill.SkillId,
                SkillCode: skill.Code,
                SkillName: skill.Name,
                Modality: skill.Modality,
                BaseScaledScore: baseScaled,
                MinScaledScore: minScaled,
                MaxScaledScore: maxScaled,
                Step: step));
        }

        int? currentTotalScore = targetReadiness?.CurrentTotalScore;
        string? currentBandCode = targetReadiness?.CurrentBandCode;
        if (string.IsNullOrEmpty(currentBandCode) && currentTotalScore.HasValue)
        {
            currentBandCode = MatchBand(bands, currentTotalScore.Value)?.Code;
        }

        string? targetBandCode = targetReadiness?.TargetBandCode;
        int? targetMinScore = targetReadiness?.TargetMinScore;

        if (string.IsNullOrEmpty(targetBandCode))
        {
            ScoreBandThreshold defaultTarget;
            if (!string.IsNullOrEmpty(currentBandCode))
            {
                int currentIdx = bands.FindIndex(b => string.Equals(b.Code, currentBandCode, StringComparison.OrdinalIgnoreCase));
                defaultTarget = currentIdx >= 0 && currentIdx + 1 < bands.Count
                    ? bands[currentIdx + 1]
                    : bands[^1];
            }
            else
            {
                defaultTarget = bands[bands.Count / 2];
            }

            targetBandCode = defaultTarget.Code;
            targetMinScore = defaultTarget.MinTotal;
        }
        else if (!targetMinScore.HasValue)
        {
            targetMinScore = bands
                .FirstOrDefault(b => string.Equals(b.Code, targetBandCode, StringComparison.OrdinalIgnoreCase))
                ?.MinTotal;
        }

        return new SimulatorConfig(
            MaxTotalScore: maxTotalScore,
            CurrentTotalScore: currentTotalScore,
            CurrentBandCode: currentBandCode,
            TargetBandCode: targetBandCode,
            TargetMinScore: targetMinScore,
            Skills: simulatorSkills,
            Bands: bands);
    }

    private static ScoreBandThreshold? MatchBand(IReadOnlyList<ScoreBandThreshold> bands, int score)
    {
        if (bands.Count == 0)
        {
            return null;
        }

        var match = bands.FirstOrDefault(b => score >= b.MinTotal && score <= b.MaxTotal);
        if (match is not null)
        {
            return match;
        }

        return score < bands[0].MinTotal ? bands[0] : bands[^1];
    }
}

