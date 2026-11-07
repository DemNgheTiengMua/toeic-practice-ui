using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #222: the score → level direction of the cut-score lookup, beside the coordinate → score direction
/// <see cref="ProjectAsync"/> already owns.
/// <para>
/// <b>The rule is the same four answers, read the other way.</b> A score inside a <c>cut</c> row yields
/// a level and a position; inside an <c>uncertified</c> row it yields a position and no level; at or
/// above a <c>display_ceiling</c> row it yields the scale's top and no level; in a gap between rows, or
/// where the certificate publishes no table, it yields <c>cannot_judge</c>. In particular there is
/// <b>no nearest-row fallback</b>: the table's ranges have deliberate gaps, and answering with the row
/// below would report a level the certificate never awarded for that score.
/// </para>
/// <para>
/// This is not a second projector. It shares <see cref="CefrProjection"/>'s reason codes and the same
/// row-kind semantics, because the moment the two lookups disagree about the table, a mock result and a
/// placement result quote different levels for the same skill.
/// </para>
/// </summary>
public sealed partial class CefrProjectionService
{
    /// <summary>
    /// Reads each skill's scaled score back against <paramref name="tableVersion"/> (null reads the
    /// newest). A skill absent from <paramref name="scaledScores"/> is not looked up and not reported.
    /// </summary>
    public async Task<ScaledScoreProjection> ProjectScoresAsync(
        int certId,
        IReadOnlyDictionary<int, int> scaledScores,
        int? tableVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scaledScores);

        var versions = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == certId)
            .ToListAsync(cancellationToken);

        var version = tableVersion is int requested
            ? versions.FirstOrDefault(v => v.TableVersion == requested)
            : versions.OrderByDescending(v => v.TableVersion).FirstOrDefault();

        if (version is null)
        {
            var reason = tableVersion is null
                ? CefrProjection.NoCutScoreTable
                : CefrProjection.UnknownCutScoreTableVersion;
            return Unjudgeable(scaledScores, null, reason);
        }

        var rows = await db.CefrCutScores
            .AsNoTracking()
            .Where(r => r.CertId == certId && r.TableVersion == version.TableVersion)
            .ToListAsync(cancellationToken);

        var rowsBySkill = rows.ToLookup(r => r.SkillId);
        var skills = scaledScores
            .OrderBy(s => s.Key)
            .Select(s => ProjectScore(s.Key, s.Value, rowsBySkill[s.Key].ToList(), version.TableVersion))
            .ToList();

        // A paper whose skills the table could not place is cannot_judge as a whole, even if one skill
        // happened to land inside a range: a partial answer would have the screen print a level from a
        // vector the table refused to complete.
        var unjudged = skills.FirstOrDefault(s => s.Outcome == ProjectionOutcome.CannotJudge);
        var outcome = unjudged is not null
            ? ProjectionOutcome.CannotJudge
            : skills.Any(s => s.Outcome == ProjectionOutcome.DisplayCeiling)
                ? ProjectionOutcome.DisplayCeiling
                : skills.Any(s => s.Outcome == ProjectionOutcome.Uncertified)
                    ? ProjectionOutcome.Uncertified
                    : ProjectionOutcome.Projected;

        return new ScaledScoreProjection(
            skills, version.TableVersion, outcome, unjudged?.CannotJudgeReason);
    }

    /// <summary>
    /// One skill's score against the rows of one version. The ceiling is checked first because a
    /// ceiling row has no range and would otherwise be skipped by the range predicate; the cut rows
    /// are then matched inclusively on both ends, so 400 is B2 on Listening rather than the top of B1.
    /// </summary>
    private static ScaledScoreSkillProjection ProjectScore(
        int skillId, int score, IReadOnlyList<CefrCutScore> rows, int tableVersion)
    {
        if (rows.Count == 0)
        {
            return Unjudged(skillId, score, tableVersion, CefrProjection.NoCutScoreRowForLevel);
        }

        // The best range the score sits in. Overlap is refused by trg_CefrCutScores_NoOverlap, so at
        // most one cut/uncertified row can match; OrderByDescending is the deterministic tie-break a
        // hand-edited table still deserves.
        var row = rows
            .Where(r => r.MinScaled is { } min && score >= min && score <= r.MaxScaled)
            .OrderByDescending(r => r.MinScaled)
            .FirstOrDefault();

        if (row is null)
        {
            // The ceiling answers only a score above every range the table states - TOEIC's 495 is
            // inside C1's cut row, so the ceiling is reached by a table with a gap at the top, not by
            // a seeded score. It is a number and never a level.
            var topOfRanges = rows.Where(r => r.MinScaled is not null)
                .Select(r => (int?)r.MaxScaled)
                .DefaultIfEmpty(null)
                .Max();

            var ceiling = rows
                .Where(r => EnumCodes.Parse<CutScoreRowKind>(r.RowKind) == CutScoreRowKind.DisplayCeiling)
                .OrderByDescending(r => r.MaxScaled)
                .FirstOrDefault();

            return ceiling is not null && topOfRanges is int top && score > top
                ? new ScaledScoreSkillProjection(
                    skillId, score, AwardedLevel: null, PositionInBand: null,
                    ProjectionOutcome.DisplayCeiling, tableVersion, DisplayCeiling: ceiling.MaxScaled)
                : Unjudged(skillId, score, tableVersion, CefrProjection.NoCutScoreRowForLevel);
        }

        var kind = EnumCodes.Parse<CutScoreRowKind>(row.RowKind);
        var position = PositionInRange(row, score);

        if (kind == CutScoreRowKind.Uncertified)
        {
            return new ScaledScoreSkillProjection(
                skillId, score, AwardedLevel: null, position,
                ProjectionOutcome.Uncertified, tableVersion);
        }

        // A score inside a range whose level code is not a known level is a table defect, not a result:
        // `cannot_judge`, never `default(CefrLevel)` (which is the pre-A1 floor, a level it did not
        // award).
        return CefrLevel.TryParse(row.CefrLevel, out var awarded)
            ? new ScaledScoreSkillProjection(
                skillId, score, awarded, position, ProjectionOutcome.Projected, tableVersion)
            : Unjudged(skillId, score, tableVersion, CefrProjection.NoCutScoreRowForLevel);
    }

    /// <summary>
    /// Where the score sits inside its own row, 0-100. The inverse of <see cref="Interpolate"/> and
    /// deliberately integer arithmetic for the same reason: the scale is integral, and a decimal here
    /// would round twice.
    /// </summary>
    private static int PositionInRange(CefrCutScore row, int score)
    {
        var min = row.MinScaled!.Value;
        var span = row.MaxScaled - min;
        if (span <= 0) return 0;

        var clamped = Math.Clamp(score, min, row.MaxScaled);
        return (int)Math.Round((clamped - min) * 100m / span, MidpointRounding.AwayFromZero);
    }

    private static ScaledScoreSkillProjection Unjudged(
        int skillId, int score, int? tableVersion, string reason) =>
        new(skillId, score, AwardedLevel: null, PositionInBand: null,
            ProjectionOutcome.CannotJudge, tableVersion, CannotJudgeReason: reason);

    private static ScaledScoreProjection Unjudgeable(
        IReadOnlyDictionary<int, int> scores, int? tableVersion, string reason) =>
        new(scores.OrderBy(s => s.Key)
                .Select(s => Unjudged(s.Key, s.Value, tableVersion, reason))
                .ToList(),
            tableVersion, ProjectionOutcome.CannotJudge, reason);
}
