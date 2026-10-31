using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class ScoreConversionService
{

    public async Task<ScorePreviewResult> CalculateScorePreviewAsync(ScorePreviewRequest request, CancellationToken cancellationToken = default)
    {
        List<ScoreScaleRowItem>? dbScales = null;
        List<ScoreBandConfigItem>? dbBands = null;
        Dictionary<int, (int Min, int Max, int Step)>? skillRanges = null;

        if (request.CustomScales == null || request.CustomScales.Count == 0)
        {
            var q = db.ScoreScales.AsNoTracking().Where(s => s.ExamId == null);
            if (request.CertId.HasValue)
            {
                q = q.Where(s => s.CertId == request.CertId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(request.CertCode))
            {
                q = q.Where(s => s.Cert.Code == request.CertCode);
            }

            dbScales = await q
                .Include(s => s.Skill)
                .Select(s => new ScoreScaleRowItem(
                    s.ScaleId,
                    s.SkillId ?? 0,
                    s.Skill != null ? s.Skill.Code : "",
                    s.Skill != null ? s.Skill.Name : "",
                    s.RawScore,
                    s.ScaledScore))
                .ToListAsync(cancellationToken);
        }

        // E1.1 (#146): the scale is the skill's own data, never a guess from the certificate code.
        skillRanges = await db.Skills
            .AsNoTracking()
            .Where(s => s.MinScaledScore != null && s.MaxScaledScore != null && s.ScoreStep != null)
            .Select(s => new { s.SkillId, s.MinScaledScore, s.MaxScaledScore, s.ScoreStep })
            .ToDictionaryAsync(s => s.SkillId, s => (s.MinScaledScore!.Value, s.MaxScaledScore!.Value, s.ScoreStep!.Value), cancellationToken);

        if (request.CustomBands == null || request.CustomBands.Count == 0)
        {
            var q = db.ScoreBands.AsNoTracking();
            if (request.CertId.HasValue)
            {
                q = q.Where(b => b.CertId == request.CertId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(request.CertCode))
            {
                q = q.Where(b => b.Cert.Code == request.CertCode);
            }

            dbBands = await q
                .OrderBy(b => b.MinTotal)
                .Select(b => new ScoreBandConfigItem(
                    b.BandId,
                    b.Code,
                    b.Name,
                    b.MinTotal,
                    b.MaxTotal,
                    b.DisplayOrder))
                .ToListAsync(cancellationToken);
        }

        return CalculateScorePreview(request, dbScales, dbBands, skillRanges);
    }

    public ScorePreviewResult CalculateScorePreview(
        ScorePreviewRequest request,
        IReadOnlyList<ScoreScaleRowItem>? scales = null,
        IReadOnlyList<ScoreBandConfigItem>? bands = null,
        IReadOnlyDictionary<int, (int Min, int Max, int Step)>? skillRanges = null)
    {
        var scaleEntries = new List<ScoreScaleRowItem>();
        if (request.CustomScales != null && request.CustomScales.Count > 0)
        {
            scaleEntries.AddRange(request.CustomScales.Select(cs => new ScoreScaleRowItem(null, cs.SkillId, "", "", cs.RawScore, cs.ScaledScore)));
        }
        else if (scales != null)
        {
            scaleEntries.AddRange(scales);
        }

        var bandEntries = new List<ScoreBandConfigItem>();
        if (request.CustomBands != null && request.CustomBands.Count > 0)
        {
            bandEntries.AddRange(request.CustomBands.Select(cb => new ScoreBandConfigItem(cb.BandId ?? 0, cb.Code, cb.Name, cb.MinTotal, cb.MaxTotal, cb.DisplayOrder)));
        }
        else if (bands != null)
        {
            bandEntries.AddRange(bands);
        }

        var skillResults = new List<SkillScorePreviewResult>();
        var simulatedSkillScores = new List<AttemptSkillScore>();

        foreach (var skillInput in request.Skills)
        {
            var sectionResults = new List<SectionScorePreviewResult>();
            decimal computedSkillRawScore = 0m;

            if (skillInput.Sections != null && skillInput.Sections.Count > 0)
            {
                decimal totalWeight = skillInput.Sections.Sum(s => s.ScoreWeight > 0 ? s.ScoreWeight : 1.0m);
                decimal weightedSectionSum = skillInput.Sections.Sum(s => s.RawScore * (s.ScoreWeight > 0 ? s.ScoreWeight : 1.0m));
                computedSkillRawScore = totalWeight > 0 ? weightedSectionSum / totalWeight : 0m;

                foreach (var sec in skillInput.Sections)
                {
                    sectionResults.Add(new SectionScorePreviewResult(
                        sec.SectionId,
                        sec.SectionCode,
                        sec.RawScore,
                        sec.ScoreWeight > 0 ? sec.ScoreWeight : 1.0m,
                        sec.RawScore));
                }
            }
            else
            {
                computedSkillRawScore = skillInput.RawScore ?? 0m;
            }

            int scaledScore;
            var skillScales = scaleEntries.Where(s => skillInput.SkillId.HasValue && s.SkillId == skillInput.SkillId.Value)
                .OrderBy(s => s.RawScore)
                .ToList();

            // E1.1 (#146): one behaviour for every caller - interpolate between the rows and round on the
            // skill's own grid. A skill without a configured scale is a configuration error, not a guess.
            if (skillInput.SkillId is not int previewSkillId
                || skillRanges is null
                || !skillRanges.TryGetValue(previewSkillId, out var range))
            {
                throw new ScoreConversionException(
                    ScoreConversionException.SkillScaleNotConfigured,
                    $"Skill '{skillInput.SkillCode ?? skillInput.SkillId?.ToString() ?? "?"}' has no score scale configured (MinScaledScore, MaxScaledScore and ScoreStep must all be set).");
            }

            if (skillScales.Count > 0)
            {
                var lower = skillScales.LastOrDefault(s => s.RawScore <= computedSkillRawScore);
                var upper = skillScales.FirstOrDefault(s => s.RawScore >= computedSkillRawScore);
                scaledScore = AttemptEngine.ProjectOntoScale(
                    computedSkillRawScore,
                    lower?.RawScore,
                    lower?.ScaledScore,
                    upper?.RawScore,
                    upper?.ScaledScore,
                    range.Min,
                    range.Max,
                    range.Step);
            }
            else
            {
                // No rows at all: spread the raw score across the skill's own range.
                var rule = request.ScoreAggregationRule?.ToLowerInvariant() ?? "sum";
                if (rule == "avg_half_step")
                {
                    scaledScore = AttemptEngine.RoundHalfStep((double)computedSkillRawScore);
                }
                else if (rule == "avg_whole")
                {
                    scaledScore = (int)Math.Round(computedSkillRawScore, MidpointRounding.AwayFromZero);
                }
                else
                {
                    int maxQ = skillInput.Sections?.Sum(s => s.MaxQuestions ?? 0) ?? 0;
                    scaledScore = AttemptEngine.ProjectOntoScale(
                        computedSkillRawScore,
                        null, null, null, null,
                        range.Min,
                        range.Max,
                        range.Step,
                        maxQ > 0 ? maxQ : 100);
                }
            }

            skillResults.Add(new SkillScorePreviewResult(
                skillInput.SkillId,
                skillInput.SkillCode,
                computedSkillRawScore,
                scaledScore,
                sectionResults));

            simulatedSkillScores.Add(new AttemptSkillScore
            {
                SkillId = skillInput.SkillId ?? 0,
                ScaledScore = scaledScore
            });
        }

        int totalScore = AttemptEngine.AggregateTotalScore(request.ScoreAggregationRule, simulatedSkillScores);

        // Match band
        string? matchedBandCode = null;
        string? matchedBandName = null;
        if (bandEntries.Count > 0)
        {
            var orderedBands = bandEntries.OrderBy(b => b.MinTotal).ToList();
            var matchingBand = orderedBands.FirstOrDefault(b => totalScore >= b.MinTotal && totalScore <= b.MaxTotal);
            if (matchingBand == null)
            {
                matchingBand = totalScore < orderedBands[0].MinTotal
                    ? orderedBands[0]
                    : orderedBands.LastOrDefault(b => totalScore >= b.MinTotal) ?? orderedBands[^1];
            }
            matchedBandCode = matchingBand.Code;
            matchedBandName = matchingBand.Name;
        }

        return new ScorePreviewResult(
            request.ScoreAggregationRule ?? "sum",
            skillResults,
            totalScore,
            matchedBandCode,
            matchedBandName);
    }
}
