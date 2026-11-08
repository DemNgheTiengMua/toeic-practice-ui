using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #232 / D10: the readers that turn stored answers into a step coordinate.
/// <para>
/// The one rule this file exists to keep is that a step's level comes from <b>that skill's own</b>
/// measured level. The certificate's target band is a scored interval on a different axis (D1/D6) and
/// is never read into <c>TargetCefr</c>. The per-skill level is read from the certificate's own
/// cut-score table (#210) against the skill's latest graded scaled score — a lookup, never arithmetic
/// on a total.
/// </para>
/// </summary>
public sealed partial class LearningPathService
{
    /// <summary>The lifecycle state selection reads. Named, so a state added to
    /// <c>CK_Items_Lifecycle</c> cannot start being served by "not this".
    /// <para>
    /// #235: this is <b>planning's</b> read of stock ("does this cell hold anything at all"), not a
    /// second selector. What a drill actually serves is <see cref="ItemSelector"/>'s alone.
    /// </para>
    /// </summary>
    private const string ActiveItemLifecycle = "active";

    /// <summary>A drill never draws a placement item: placement stock is an occupancy problem (D8) and
    /// a path drill is not a measurement.</summary>
    private const bool DrillPlacementEligible = false;

    /// <summary>
    /// Each examined skill's own level, keyed by <c>SkillId</c>.
    /// <para>
    /// <b>First read: <c>LearnerSkillStates</c>.</b> D15 makes that table the single answer to "what is
    /// my level" — every screen reads it, so a path that computed its own answer here could disagree
    /// with the dashboard on the same day. A skill with a row uses it verbatim, including its
    /// pre-A1 floor: a learner measured below A1 gets a step at A1 only because no content is authored
    /// at the floor, and that clamp is applied openly rather than by silently replacing the measurement.
    /// </para>
    /// <para>
    /// Fallback, only for a skill #211 has not measured yet: this certificate's per-skill cut-score row
    /// against the latest graded score, then the lowest level the skill has stock at, then the
    /// certificate's own bottom row. Every one of those is still <b>this skill's</b> level from this
    /// skill's own table — never the overall, never the target band.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, CefrLevel>> LoadSkillLevelsAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .Select(s => new { s.SkillId, s.Modality })
            .ToListAsync(cancellationToken);

        if (skills.Count == 0)
        {
            return [];
        }

        var measured = await db.LearnerSkillStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CertId == certId)
            .Select(s => new { s.SkillId, s.CefrLevel })
            .ToListAsync(cancellationToken);

        var levels = measured.ToDictionary(s => s.SkillId, s => ClampToAuthored(s.CefrLevel));

        var version = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == certId)
            .OrderByDescending(v => v.TableVersion)
            .Select(v => (int?)v.TableVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return levels;
        }

        var cuts = await db.CefrCutScores
            .AsNoTracking()
            .Where(r => r.CertId == certId && r.TableVersion == version.Value && r.RowKind == "cut")
            .Select(r => new { r.SkillId, r.CefrLevel, r.MinScaled, r.MaxScaled })
            .ToListAsync(cancellationToken);

        var latestScores = (await db.AttemptSkillScores
                .AsNoTracking()
                .Where(s => s.CertId == certId
                    && s.Attempt.UserId == userId
                    && s.Attempt.Status == AttemptStatus.Graded.ToCode())
                .OrderByDescending(s => s.Attempt.SubmittedAt ?? s.Attempt.StartedAt)
                .ThenByDescending(s => s.AttemptId)
                .Select(s => new { s.SkillId, s.ScaledScore })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.SkillId)
            .ToDictionary(g => g.Key, g => g.First().ScaledScore);

        var stockedLevels = (await db.Items
                .AsNoTracking()
                .Where(i => i.LifecycleStatus == ActiveItemLifecycle
                    && i.PlacementEligible == DrillPlacementEligible)
                .Select(i => new { i.Modality, i.CefrLevel })
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToLookup(i => i.Modality, i => i.CefrLevel);

        foreach (var skill in skills)
        {
            if (levels.ContainsKey(skill.SkillId))
            {
                continue;
            }

            var rows = cuts.Where(c => c.SkillId == skill.SkillId).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var row = latestScores.TryGetValue(skill.SkillId, out var score)
                ? rows
                    .Where(r => r.MinScaled is { } min && score >= min && score <= r.MaxScaled)
                    .OrderByDescending(r => r.MinScaled)
                    .FirstOrDefault()
                : null;

            if (row is null)
            {
                // No measurement (or no row covered the score): the lowest level this skill has stock
                // at, so the step has items to serve.
                var stocked = stockedLevels[skill.Modality].ToList();
                row = rows
                    .Select(r => (Row: r, Level: CefrLevel.TryParseAuthored(r.CefrLevel, out var lvl) ? lvl : CefrLevel.PreA1))
                    .Where(x => !x.Level.IsReportableFloor && stocked.Contains(x.Level))
                    .OrderBy(x => x.Level)
                    .Select(x => x.Row)
                    .FirstOrDefault()
                    // Nothing stocked at any level: the certificate's own bottom row still states a level.
                    ?? rows.OrderBy(r => r.MaxScaled).First();
            }

            if (CefrLevel.TryParseAuthored(row.CefrLevel, out var level))
            {
                levels[skill.SkillId] = level;
            }
        }

        return levels;
    }

    /// <summary>
    /// A measured level as an authorable one. The pre-A1 floor is reportable but no content may be
    /// written at it (D1), so a step for a learner measured below A1 is pitched at A1 — the bottom rung
    /// the pool actually holds. That is a clamp on the <b>step</b>, not on the measurement: the stored
    /// state keeps saying "below A1", which is what the dashboard must show.
    /// </summary>
    private static CefrLevel ClampToAuthored(CefrLevel measured) =>
        measured.IsReportableFloor ? CefrLevel.A1 : measured;

    /// <summary>
    /// The topics a skill may drill at one level, weakest first, from this learner's own pool answers.
    /// <para>
    /// Topics are certificate-agnostic (D3), so the candidate set is every topic of the skill's
    /// modality. Weakness is measured accuracy over items the learner actually answered at that exact
    /// level — the same level-conditioning weakness analysis uses, because easy A1 drills would
    /// otherwise mask a B2 weakness.
    /// </para>
    /// <para>
    /// A learner with no history at the level has no weakness to rank. The canonical topic order then
    /// stands in and says so, rather than inventing a priority.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<int>> LoadDrillableTopicsAsync(
        int userId, int certId, string modality, CefrLevel level, CancellationToken cancellationToken)
    {
        var topicIds = await db.Topics
            .AsNoTracking()
            .Where(t => t.Modality == modality)
            .OrderBy(t => t.TopicId)
            .Select(t => t.TopicId)
            .ToListAsync(cancellationToken);

        if (topicIds.Count == 0)
        {
            return [];
        }

        var cellItems = await db.Items
            .AsNoTracking()
            .Where(i => i.Modality == modality
                && i.CefrLevel == level
                && i.LifecycleStatus == ActiveItemLifecycle
                && i.PlacementEligible == DrillPlacementEligible
                && topicIds.Contains(i.TopicId))
            .Select(i => new { i.ItemId, i.TopicId })
            .ToListAsync(cancellationToken);

        if (cellItems.Count == 0)
        {
            // No stock at this level at all: a step would have nothing to serve. Say so by planning no
            // drill rather than creating a step that throws when opened.
            return [];
        }

        // The rows of this learner's drills at the certificate, one query, so the per-item statistics
        // below are computed from one list.
        var stats = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.Session.UserId == userId
                && pa.Session.CertId == certId
                && pa.ItemId != null)
            .ToListAsync(cancellationToken);

        // Accuracy is computed over judged answers only. An unjudged answer (a productive pool drill is
        // ungraded forever) is not a miss: counting it would deflate the cell the planner picks its next
        // step from. #253 changed what "answered" means; it did not make an unjudged answer wrong.
        var graded = stats.Where(pa => pa.IsCorrect.HasValue).ToList();

        var byItem = graded.Count == 0
            ? []
            : graded.GroupBy(pa => pa.ItemId!.Value)
                .ToDictionary(g => g.Key, g => (Answered: g.Count(), Correct: g.Count(a => a.IsCorrect == true)));
        var topics = cellItems
            .GroupBy(i => i.TopicId)
            .Select(g =>
            {
                var answered = g.Sum(i => byItem.TryGetValue(i.ItemId, out var s) ? s.Answered : 0);
                var correct = g.Sum(i => byItem.TryGetValue(i.ItemId, out var s) ? s.Correct : 0);
                return new
                {
                    TopicId = g.Key,
                    Answered = answered,
                    Accuracy = answered == 0 ? 1d : (double)correct / answered,
                };
            })
            .ToList();

        return topics
            .OrderBy(t => t.Accuracy)
            .ThenByDescending(t => t.Answered)
            .ThenBy(t => t.TopicId)
            .Select(t => t.TopicId)
            .ToList();
    }

    /// <summary>
    /// The measured mastery of one (topic, level) cell, from the learner's answers at that cell.
    /// <para>
    /// This is a <b>read</b> of what the learner has done, not a second mastery store: re-keying mastery
    /// to the cell is #237's job and writing a mastery row here would be a second writer of one fact.
    /// The thresholds are <see cref="MasterySchedule"/>'s existing constants, not new ones.
    /// </para>
    /// </summary>
    private async Task<MasteryState> LoadCellMasteryAsync(
        int userId, int certId, int topicId, CefrLevel level, CancellationToken cancellationToken)
    {
        var counts = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.Session.UserId == userId
                && pa.Session.CertId == certId
                && pa.IsCorrect != null
                // #240: a lesson micro-check is evidence of this cell too. Its answer names a card and
                // no item, so it is attributed through the lesson sitting's own (topic, level) - a
                // lesson is pitched at exactly the cell it teaches.
                && ((pa.ItemId != null
                        && db.Items.Any(i => i.ItemId == pa.ItemId
                            && i.TopicId == topicId && i.CefrLevel == level))
                    || (pa.CardId != null
                        && pa.Session.TopicId == topicId
                        && pa.Session.CefrLevel == level)))
            .GroupBy(pa => 1)
            .Select(g => new { Answered = g.Count(), Correct = g.Count(a => a.IsCorrect == true) })
            .FirstOrDefaultAsync(cancellationToken);

        if (counts is null || counts.Answered == 0)
        {
            return MasteryState.Untested;
        }

        var accuracy = (int)Math.Round((double)counts.Correct / counts.Answered * 100);

        return accuracy >= MasterySchedule.MinAccuracyToMastered && counts.Answered >= MasterySchedule.MinAnswersToMastered
            ? MasteryState.Mastered
            : accuracy >= MasterySchedule.MinAccuracyToReviewing && counts.Answered >= MasterySchedule.MinAnswersToReviewing
                ? MasteryState.Reviewing
                : MasteryState.Learning;
    }

    /// <summary>
    /// #219 / D5: the topics the latest placement saw go badly, keyed by (skill, level). <b>A hint,
    /// not mastery.</b>
    /// <para>
    /// Only the latest <c>done</c> placement for this learner and certificate is read, and only its
    /// <see cref="PlacementResultStatus.Measured"/> / <see cref="PlacementResultStatus.EstimatedFromOneTask"/>
    /// rows. A carried row is skipped: its hint belongs to an earlier sitting (the writer clears it on
    /// a carried row for that reason), and a re-used result is not evidence this paper saw anything.
    /// </para>
    /// <para>
    /// The band is compared as the row's own <c>CefrLevel</c>, because a hint about A2 is not a hint
    /// about the B1 cell the block plans at.
    /// </para>
    /// </summary>
    private async Task<Dictionary<(int SkillId, CefrLevel Level), IReadOnlyList<int>>> LoadPlacementTopicHintsAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var latest = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .ThenByDescending(p => p.PlacementId)
            .Select(p => (int?)p.PlacementId)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return [];
        }

        var rows = await db.PlacementSkillResults
            .AsNoTracking()
            .Where(r => r.PlacementId == latest.Value
                && !r.ReusedFromPrior
                && r.CefrLevel != null
                && (r.ResultStatus == PlacementResultStatus.Measured
                    || r.ResultStatus == PlacementResultStatus.EstimatedFromOneTask))
            .Select(r => new { r.SkillId, r.CefrLevel, r.TopicPriorsJson })
            .ToListAsync(cancellationToken);

        var hints = new Dictionary<(int, CefrLevel), IReadOnlyList<int>>();
        foreach (var row in rows)
        {
            var weak = PlacementTopicPrior.Deserialize(row.TopicPriorsJson)
                .Where(p => p.RoutingSignal == PlacementTopicPrior.Weak)
                .Select(p => p.TopicId)
                .Distinct()
                .ToList();

            if (weak.Count > 0 && row.CefrLevel is { } level)
            {
                hints[(row.SkillId, level)] = weak;
            }
        }

        return hints;
    }

    /// <summary>The lesson of this skill at this level, lowest display order first, or null when the
    /// certificate has none.
    /// <para>
    /// #240: a lesson now carries its own level and topic, so the path asks for the level it plans at
    /// and takes the topic FROM the lesson - rather than naming a drill topic and hoping a lesson sits
    /// there. Before this, the path took whichever lesson of the skill sorted first and the step stated
    /// a level the row did not hold.
    /// </para>
    /// </summary>
    private async Task<(int LessonId, int TopicId)?> LoadLessonForLevelAsync(
        int certId, int skillId, CefrLevel level, CancellationToken cancellationToken)
    {
        var lesson = await db.Lessons
            .AsNoTracking()
            .Where(l => l.CertId == certId && l.SkillId == skillId && l.CefrLevel == level)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => new { l.LessonId, l.TopicId })
            .FirstOrDefaultAsync(cancellationToken);

        return lesson is null ? null : (lesson.LessonId, lesson.TopicId);
    }

    /// <summary>
    /// #234 / PLC-53: each skill's `θ_target − θ_current`, which is what the skew weights read and
    /// therefore what decides who gets the largest share of the week.
    /// <para>
    /// <b>Both halves come from stored, certificate-owned data. No crosswalk is introduced.</b> The
    /// current coordinate is <c>LearnerSkillStates.ContinuousCoordinate</c> — D15's single answer to
    /// "where is this skill", read verbatim. The target coordinate is resolved from the certificate's
    /// own cut-score table: the target band's scaled range is intersected with that skill's own cut
    /// rows, and the overlapping row's level becomes the coordinate. That is the same table that
    /// produces the current level, so the two halves cannot sit on different axes (PRN-03).
    /// </para>
    /// <para>
    /// A skill with no stored state, or no cut row overlapping the target band, gets <c>null</c>: "no
    /// evidence" is not "already at target", and a fabricated distance would point the week at a skill
    /// nobody measured. Null takes the floor weight — the guaranteed step, never the largest share —
    /// which is what a week planned on no evidence should do.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, decimal?>> LoadDistancesToTargetAsync(
        int userId, int certId, string targetBandCode, CancellationToken cancellationToken)
    {
        var distances = new Dictionary<int, decimal?>();

        var current = await db.LearnerSkillStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CertId == certId)
            .Select(s => new { s.SkillId, s.ContinuousCoordinate })
            .ToListAsync(cancellationToken);

        if (current.Count == 0)
        {
            return distances;
        }

        var targetBand = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId && b.Code == targetBandCode)
            .Select(b => new { b.MinTotal, b.MaxTotal })
            .FirstOrDefaultAsync(cancellationToken);

        var version = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == certId)
            .OrderByDescending(v => v.TableVersion)
            .Select(v => (int?)v.TableVersion)
            .FirstOrDefaultAsync(cancellationToken);

        var cuts = version is null
            ? new List<(int SkillId, string CefrLevel, int? MinScaled, int MaxScaled)>()
            : (await db.CefrCutScores
                .AsNoTracking()
                .Where(r => r.CertId == certId && r.TableVersion == version.Value && r.RowKind == "cut")
                .Select(r => new { r.SkillId, r.CefrLevel, r.MinScaled, r.MaxScaled })
                .ToListAsync(cancellationToken))
                .Select(r => (r.SkillId, r.CefrLevel, r.MinScaled, r.MaxScaled))
                .ToList();

        foreach (var state in current)
        {
            var target = targetBand is null
                ? null
                : TargetCoordinateFor(
                    cuts.Where(c => c.SkillId == state.SkillId).ToList(), targetBand.MinTotal, targetBand.MaxTotal);

            distances[state.SkillId] = target is null ? null : target.Value - state.ContinuousCoordinate;
        }

        return distances;
    }

    /// <summary>
    /// The coordinate a target band asks one skill for, read from the certificate's own cut rows.
    /// <para>
    /// The band's scaled range is intersected with each cut row; the largest overlap wins, and a tie
    /// goes to the higher row because a band's upper edge is what "reaching" it means. The coordinate
    /// is the middle of that level's span — <c>rank + 0.50</c> — which is the convention the worked
    /// allocation itself states for a B2 target (`θ_target = 4.50`). It is not a name lookup: a
    /// certificate whose bands carry no cut rows resolves nothing and gets no target.
    /// </para>
    /// </summary>
    private static decimal? TargetCoordinateFor(
        IReadOnlyList<(int SkillId, string CefrLevel, int? MinScaled, int MaxScaled)> rows,
        int bandMin, int bandMax)
    {
        CefrLevel? bestLevel = null;
        var bestOverlap = 0;

        foreach (var row in rows)
        {
            if (row.MinScaled is not { } min)
            {
                continue;
            }

            var overlap = Math.Max(0, Math.Min(row.MaxScaled, bandMax) - Math.Max(min, bandMin));
            if (overlap <= 0 || !CefrLevel.TryParseAuthored(row.CefrLevel, out var level))
            {
                continue;
            }

            if (overlap > bestOverlap || (overlap == bestOverlap && bestLevel is { } seen && level > seen))
            {
                bestOverlap = overlap;
                bestLevel = level;
            }
        }

        return bestLevel is { } found ? found.Rank + 0.5m : null;
    }

    /// <summary>The published mock exam that covers this skill, or null. A mock section executes
    /// through <c>IAttemptEngine</c>, which serves a whole paper, so a certificate with no published
    /// mock plans no mock step rather than a step that can never be finished.</summary>
    private async Task<int?> LoadMockExamForSkillAsync(
        int certId, int skillId, CancellationToken cancellationToken) =>
        await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId
                && e.Purpose == ExamPurpose.Mock.ToCode()
                && e.Status == ExamStatus.Published.ToCode()
                && !e.IsDeleted
                && e.QuestionGroups.Any(g => g.Section.SkillId == skillId))
            .OrderBy(e => e.ExamId)
            .Select(e => (int?)e.ExamId)
            .FirstOrDefaultAsync(cancellationToken);
}