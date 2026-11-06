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
    /// <c>CK_Items_Lifecycle</c> cannot start being served by "not this".</summary>
    private const string ActiveItemLifecycle = "active";

    /// <summary>A drill never draws a placement item: placement stock is an occupancy problem (D8) and
    /// a path drill is not a measurement.</summary>
    private const bool DrillPlacementEligible = false;

    /// <summary>
    /// Each examined skill's own level, keyed by <c>SkillId</c>, resolved from the certificate's
    /// per-skill cut-score table against that skill's latest graded scaled score.
    /// <para>
    /// A skill with no graded score still needs a level, because every step must name one. The
    /// fallback is the lowest <c>cut</c> level the certificate publishes for that skill <b>at which the
    /// skill actually has drillable stock</b>, and the certificate's own bottom row when the pool holds
    /// nothing at any level. Both are this skill's own level from this skill's own table: never the
    /// overall, never the target band.
    /// </para>
    /// <para>
    /// Skipping straight to the bottom row would pitch a step at a level with no items, and a drill with
    /// nothing to serve throws the moment the learner opens it.
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

        var version = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == certId)
            .OrderByDescending(v => v.TableVersion)
            .Select(v => (int?)v.TableVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return [];
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

        var levels = new Dictionary<int, CefrLevel>();
        foreach (var skill in skills)
        {
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

        var stats = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.Session.UserId == userId
                && pa.Session.CertId == certId
                && pa.ItemId != null
                && (pa.SelectedLabel != null || pa.IsCorrect != null))
            .GroupBy(pa => pa.ItemId!.Value)
            .Select(g => new
            {
                ItemId = g.Key,
                Answered = g.Count(),
                Correct = g.Count(a => a.IsCorrect == true),
            })
            .ToListAsync(cancellationToken);

        var byItem = stats.ToDictionary(s => s.ItemId);
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
                && pa.ItemId != null
                && db.Items.Any(i => i.ItemId == pa.ItemId && i.TopicId == topicId && i.CefrLevel == level))
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

    /// <summary>The one lesson of this skill, lowest display order first, or null when the certificate
    /// has none. A lesson step needs a leveled lesson, so the level is carried on the step and not
    /// converted into a lesson's property: lessons are not leveled in this schema yet.</summary>
    private async Task<int?> LoadLessonForSkillAsync(int certId, int skillId, CancellationToken cancellationToken) =>
        await db.Lessons
            .AsNoTracking()
            .Where(l => l.CertId == certId && l.SkillId == skillId)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => (int?)l.LessonId)
            .FirstOrDefaultAsync(cancellationToken);

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
