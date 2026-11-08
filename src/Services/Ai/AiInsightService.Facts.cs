using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AiInsightService
{
    /// <summary>
    /// #244 / D21: the structured payload an insight narrates. Every field is read from its owner -
    /// the per-skill state from #211's read, the verdict from the stored <c>TargetBands</c> row - and
    /// nothing is recomputed here.
    /// <para>
    /// <b>Every skill the certificate examines gets a row.</b> #211 keeps an unmeasured skill out of
    /// <c>LearnerStanding.Skills</c>, which is right for a screen that lists measurements and wrong for
    /// a narrator that must be told the skill exists and has no level. Its row carries a null level, so
    /// the guard can refuse a sentence that gives it one.
    /// </para>
    /// </summary>
    private async Task<InsightFacts?> BuildFactsAsync(
        int userId, int certId, TargetBand? target, CancellationToken cancellationToken)
    {
        var certCode = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (certCode is null)
        {
            return null;
        }

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code, s.Name, s.Modality })
            .ToListAsync(cancellationToken);

        var standing = await _skillStates.ReadAsync(userId, certId, cancellationToken);
        var measured = standing?.Skills.ToDictionary(s => s.SkillId) ?? [];

        var rows = skills
            .Select(skill => measured.TryGetValue(skill.SkillId, out var state)
                ? new InsightSkillFact(
                    skill.SkillId, skill.Code, skill.Name, skill.Modality,
                    state.Level, state.PositionInBand, state.ContinuousCoordinate)
                : new InsightSkillFact(
                    skill.SkillId, skill.Code, skill.Name, skill.Modality, null, null, null))
            .ToList();

        return new InsightFacts(
            certCode,
            rows,
            standing?.Overall,
            standing?.LimitingSkillCode,
            target?.TargetBandCode,
            target?.ScoreBand?.Name,
            target?.FeasibilityStatus);
    }
}
