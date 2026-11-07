using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #210: the one place a CEFR coordinate becomes a number on a certificate's scale.
/// <para>
/// Two decisions in here are load-bearing and both are asserted in
/// <c>CefrProjectionServiceTests</c>:
/// </para>
/// <list type="number">
/// <item><b>Only a <c>cut</c> row yields a level.</b> An <c>uncertified</c> row yields a score and no
/// level; a <c>display_ceiling</c> row yields the top of the scale and no level. Reading "the row for
/// this level" and returning its <c>CefrLevel</c> would report C2 for TOEIC, which LVL-14/15 refuse.</item>
/// <item><b>No nearest-row fallback.</b> A missing row is <see cref="ProjectionOutcome.CannotJudge"/>.
/// A lookup that answers with the row below (or above) invents a number the table never stated -
/// the same class of invention as a prorated study-hour estimate.</item>
/// </list>
/// <para>
/// The aggregation itself is not reimplemented: <see cref="AttemptEngine.AggregateTotalScore"/> already
/// owns the three rules, and a second copy here is how a preview and a real attempt come to disagree
/// about a total.
/// </para>
/// </summary>
public sealed partial class CefrProjectionService(CompanionDbContext db) : ICefrProjectionService
{
    public async Task<CertificateProjection> ProjectAsync(
        int certId,
        IReadOnlyDictionary<int, (CefrLevel Level, int PositionInBand)> coordinates,
        int? tableVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new { c.CertId, c.ScoreAggregationRule })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ScoreConversionException(
                ScoreConversionException.CertificateNotFound,
                $"Certificate {certId} does not exist, so nothing can be projected onto its scale.");

        var versions = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == certId)
            .ToListAsync(cancellationToken);

        var version = tableVersion is int requested
            ? versions.FirstOrDefault(v => v.TableVersion == requested)
            : versions.OrderByDescending(v => v.TableVersion).FirstOrDefault();

        if (version is null)
        {
            // Two different reasons, two different strings: "publish a table" and "that version does
            // not exist" send an admin to different pages. The per-skill rows are still emitted, so a
            // screen can say which skills are unjudged instead of rendering nothing.
            var reason = tableVersion is null ? CefrProjection.NoCutScoreTable : CefrProjection.UnknownCutScoreTableVersion;
            return Unjudgeable(coordinates, cert.ScoreAggregationRule, null, reason);
        }

        // LVL-16 / this ticket's fifth criterion. The check runs once for the certificate, not per
        // skill, because study hours are a property of the certificate's bands, not of a skill.
        var bandsCarryHours = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .AnyAsync(b => b.CumulativeStudyHours != null, cancellationToken);

        if (!bandsCarryHours)
        {
            return Unjudgeable(coordinates, cert.ScoreAggregationRule, version.TableVersion, CefrProjection.BandsCarryNoStudyHours);
        }

        var rows = await db.CefrCutScores
            .AsNoTracking()
            .Where(r => r.CertId == certId && r.TableVersion == version.TableVersion)
            .ToListAsync(cancellationToken);

        var rowsBySkill = rows.ToLookup(r => r.SkillId);
        var skills = new List<SkillProjection>(coordinates.Count);

        foreach (var (skillId, coordinate) in coordinates.OrderBy(c => c.Key))
        {
            skills.Add(ProjectSkill(skillId, coordinate.Level, coordinate.PositionInBand,
                rowsBySkill[skillId].ToList(), version.TableVersion));
        }

        // Any skill the table could not answer makes the certificate's total unanswerable. Summing
        // the rest would be a number computed from a partial vector, which is the prorated estimate
        // this ticket refuses in its fifth criterion, wearing a different name.
        var unjudged = skills.FirstOrDefault(s =>
            s.Outcome is ProjectionOutcome.CannotJudge or ProjectionOutcome.Uncertified);

        if (unjudged is not null)
        {
            return new CertificateProjection(
                skills,
                CombinedTotal: null,
                cert.ScoreAggregationRule,
                version.ReportsSkillsSeparately,
                version.TableVersion,
                ProjectionOutcome.CannotJudge,
                unjudged.CannotJudgeReason ?? ProjectionOutcome.Uncertified.ToString());
        }

        // A certificate that reports its skills separately has no combined total: that is its own
        // rule, not a missing value (LVL-11's third aggregation).
        if (version.ReportsSkillsSeparately || skills.Count == 0)
        {
            return new CertificateProjection(
                skills,
                CombinedTotal: null,
                cert.ScoreAggregationRule,
                version.ReportsSkillsSeparately,
                version.TableVersion,
                skills.Count == 0 ? ProjectionOutcome.CannotJudge : ProjectionOutcome.Projected,
                skills.Count == 0 ? CefrProjection.NoCutScoreRowForLevel : null);
        }

        var total = AttemptEngine.AggregateTotalScore(
            cert.ScoreAggregationRule,
            skills.Select(s => new AttemptSkillScore { SkillId = s.SkillId, ScaledScore = s.ProjectedScore!.Value })
                .ToList());

        // A ceiling anywhere makes the certificate's answer a ceiling: the score is real, but the
        // learner is above what the certificate expresses, and saying "projected" would hide that.
        var outcome = skills.Any(s => s.Outcome == ProjectionOutcome.DisplayCeiling)
            ? ProjectionOutcome.DisplayCeiling
            : ProjectionOutcome.Projected;

        return new CertificateProjection(
            skills, total, cert.ScoreAggregationRule, ReportsSkillsSeparately: false,
            version.TableVersion, outcome);
    }

    /// <summary>
    /// One skill's projection. The row is matched on its level - exactly one row per level per
    /// version, enforced by UQ_CefrCutScores_Version_Skill_Level - and never by proximity.
    /// </summary>
    private static SkillProjection ProjectSkill(
        int skillId, CefrLevel level, int positionInBand, IReadOnlyList<CefrCutScore> rows, int tableVersion)
    {
        var row = rows.FirstOrDefault(r =>
            string.Equals(r.CefrLevel, level.Code, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            return new SkillProjection(
                skillId, level, AwardedLevel: null, ProjectedScore: null,
                ProjectionOutcome.CannotJudge, tableVersion, CannotJudgeReason: CefrProjection.NoCutScoreRowForLevel);
        }

        var kind = EnumCodes.Parse<CutScoreRowKind>(row.RowKind);

        switch (kind)
        {
            case CutScoreRowKind.DisplayCeiling:
                // MaxScaled is the scale's displayable top. There is no level to award: the learner
                // is above the highest one this certificate expresses.
                return new SkillProjection(
                    skillId, level, AwardedLevel: null, ProjectedScore: row.MaxScaled,
                    ProjectionOutcome.DisplayCeiling, tableVersion, DisplayCeiling: row.MaxScaled);

            case CutScoreRowKind.Uncertified:
                return new SkillProjection(
                    skillId, level, AwardedLevel: null,
                    ProjectedScore: Interpolate(row, positionInBand),
                    ProjectionOutcome.Uncertified, tableVersion);

            default:
                return new SkillProjection(
                    skillId, level, level,
                    Interpolate(row, positionInBand),
                    ProjectionOutcome.Projected, tableVersion);
        }
    }

    /// <summary>
    /// The position inside the band (0–100) placed linearly within the row's own range, rounded on the
    /// scale's integer grid. Integer arithmetic throughout: the stored scale is integral, and a
    /// decimal here would round twice.
    /// </summary>
    private static int Interpolate(CefrCutScore row, int positionInBand)
    {
        var min = row.MinScaled!.Value;
        var span = row.MaxScaled - min;
        if (span <= 0) return min;

        var clamped = Math.Clamp(positionInBand, 0, 100);
        return min + (int)Math.Round(span * clamped / 100m, MidpointRounding.AwayFromZero);
    }

    private static CertificateProjection Unjudgeable(
        IReadOnlyDictionary<int, (CefrLevel Level, int PositionInBand)> coordinates,
        string rule, int? version, string reason) =>
        new(coordinates.OrderBy(c => c.Key)
                .Select(c => new SkillProjection(
                    c.Key, c.Value.Level, AwardedLevel: null, ProjectedScore: null,
                    ProjectionOutcome.CannotJudge, version ?? 0, CannotJudgeReason: reason))
                .ToList(),
            CombinedTotal: null, rule, ReportsSkillsSeparately: false, version,
            ProjectionOutcome.CannotJudge, reason);
}
