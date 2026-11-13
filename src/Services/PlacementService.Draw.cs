using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #276: the two-stage draw, wired into a sitting.
/// <para>
/// <b>Stage one builds the paper.</b> <see cref="StartPlacementInternalAsync"/> asks
/// <see cref="IPlacementStageOneDrawService"/> for the fixed per-band distribution and hands the chosen
/// positions to <c>IAttemptEngine.CreateAttemptAsync(..., AttemptPaperDraw, ...)</c>, which records them on
/// the attempt (#274). The sitting then reads its own paper through the same seam every other read uses.
/// </para>
/// <para>
/// <b>Why a failed draw falls back instead of refusing.</b> Stage one draws the whole pool, so it can only
/// land on a paper whose roster already binds those items. When a band comes up short, or an item the draw
/// chose is not bound to this exam, the honest answer is the exam's own paper - which every learner gets
/// today - not a refusal. That is the same degrade-vs-refuse distinction
/// <see cref="PlacementStageTwoDraw.IsDegraded"/> models for stage two: a shorter paper is a paper, a
/// learner with no paper at all is a regression. The ticket is explicit about this: wiring must not turn
/// "every learner gets a placement" into "most learners get a 500".
/// </para>
/// <para>
/// <b>Stage two is entered where stage one ends - the skill boundary.</b> Routing needs a stage-one score
/// per skill, and that score only exists once the skill's items are all behind the learner. So the pair is
/// computed when the learner leaves a skill (the same boundary that moves the attempt to the next skill)
/// and recorded on the attempt. Stage two's own paper is not appended here: appending positions would move
/// the paper a learner is already sitting on, which is the one thing #274's single-writer rule exists to
/// prevent.
/// </para>
/// </summary>
public sealed partial class PlacementService
{
    /// <summary>
    /// The paper this sitting is served by stage one, or null when the draw cannot land on this exam's
    /// roster and the exam's own paper is the honest answer.
    /// </summary>
    private Task<AttemptPaperDraw?> BuildStageOnePaperAsync(
        int certId,
        Exam exam,
        CancellationToken cancellationToken) =>
        BuildStageOnePaperAsync(certId, exam, PlacementScope.AllSkills, cancellationToken);

    private async Task<AttemptPaperDraw?> BuildStageOnePaperAsync(
        int certId,
        Exam exam,
        PlacementScope scope,
        CancellationToken cancellationToken)
    {
        var draw = await _stageOneDraw.DrawAsync(certId, exam.ContentTier, cancellationToken);

        // A band short of its count is not a stage-one reading: the draw is a routing instrument over
        // every band, so a missing band means the score it produces routes from an incomplete paper.
        // Stage one has no degraded form; the fallback is the exam's own paper.
        if (!draw.IsComplete || draw.ItemCount == 0)
        {
            return null;
        }

        var drawnSkills = scope == PlacementScope.ListeningAndReading
            ? draw.Skills.Where(s =>
                    string.Equals(s.Modality, Modality.Listening.ToCode(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Modality, Modality.Reading.ToCode(), StringComparison.OrdinalIgnoreCase))
                .ToList()
            : draw.Skills;

        var drawn = drawnSkills.SelectMany(skill => skill.Items).ToList();
        if (drawn.Count == 0)
        {
            return null;
        }

        var itemIds = drawn.Select(item => item.ItemId).Distinct().ToList();

        // The served positions must be positions of THIS exam: AttemptItemResponses.ExamItemId is a
        // foreign key into ExamItems(ExamItemId, ExamId), so an item the paper does not bind cannot be
        // served at all - and writing a learner's draw into the shared roster is what #274 refused.
        var positionByItem = await db.ExamItems
            .AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId && itemIds.Contains(i.ItemId))
            .Select(i => new { i.ItemId, i.ExamItemId })
            .ToListAsync(cancellationToken);

        var firstPositionOf = positionByItem
            .GroupBy(row => row.ItemId)
            .ToDictionary(group => group.Key, group => group.Min(row => row.ExamItemId));

        if (drawn.Any(item => !firstPositionOf.ContainsKey(item.ItemId)))
        {
            return null;
        }

        var servedExamItemIds = drawn.Select(item => firstPositionOf[item.ItemId]).ToList();

        // #289: on an AllSkills sitting, keep any authored Speaking/Writing tasks bound on the
        // exam alongside the stage-one objective items; on ListeningAndReading, leave them out.
        if (scope == PlacementScope.AllSkills)
        {
            var speaking = Modality.Speaking.ToCode();
            var writing = Modality.Writing.ToCode();
            var productivePositionIds = await db.ExamItems
                .AsNoTracking()
                .Where(i => i.ExamId == exam.ExamId)
                .Join(db.Sections.AsNoTracking(), i => i.SectionId, s => s.SectionId, (i, s) => new { i, s })
                .Join(db.Skills.AsNoTracking(), x => x.s.SkillId, k => k.SkillId, (x, k) => new { x.i, x.s, k })
                .Where(x => x.s.Modality == speaking || x.s.Modality == writing)
                .OrderBy(x => x.k.DisplayOrder)
                .ThenBy(x => x.s.DisplayOrder)
                .ThenBy(x => x.i.DisplayOrder)
                .Select(x => x.i.ExamItemId)
                .ToListAsync(cancellationToken);

            foreach (var posId in productivePositionIds)
            {
                if (!servedExamItemIds.Contains(posId))
                {
                    servedExamItemIds.Add(posId);
                }
            }
        }

        // The draw's own order: skills by display order, bands ascending inside each skill. Position
        // numbers are 1-based and contiguous, because a gap leaves the navigator a hole to fall into.
        var positions = servedExamItemIds
            .Select((examItemId, index) => new AttemptPaperPosition(index + 1, examItemId))
            .ToList();

        // One bundle per objective skill, stage 1: the draw spans every band, so the pair it covers is
        // the whole authored ladder. The routed pair is stage 2 and is written at the boundary.
        var bundles = drawnSkills
            .Select(skill => new AttemptPaperBundle(
                skill.SkillId, StageNumber: 1, CefrLevel.A1, CefrLevel.C2))
            .ToList();

        return new AttemptPaperDraw(positions, bundles);
    }

    /// <summary>
    /// Record the band pair stage one routed one skill to, at the boundary where that skill ends.
    /// <para>
    /// <b>Only a stage-one paper routes.</b> The score this reads is "how many of the eight stage-one items
    /// this skill served were answered correctly", so a sitting whose paper is the exam's own roster (the
    /// fallback) has no stage-one administration to score and records nothing. That is what keeps a
    /// static paper from acquiring a stage-two row it never earned.
    /// </para>
    /// <para>
    /// Writes no ledger row: placement is free, and this path only reads answers.
    /// </para>
    /// </summary>
    private async Task RecordStageTwoRoutingAsync(
        int attemptId,
        int activeSkillId,
        CancellationToken cancellationToken)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .Select(a => new { a.UserId, a.CertId, a.Exam!.Purpose, a.Exam.ContentTier })
            .SingleOrDefaultAsync(cancellationToken);

        if (attempt is null
            || !string.Equals(attempt.Purpose, ExamPurpose.Placement.ToCode(), StringComparison.Ordinal))
        {
            return;
        }

        // The served positions, and the items they name. The draw asks about ITEMS ("have I already
        // served this one"), while the attempt records POSITIONS - so the ids are translated here rather
        // than passed across as if they were the same thing.
        var servedPositions = await db.AttemptItemResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attemptId)
            .OrderBy(r => r.PositionNumber)
            .Select(r => r.ExamItemId)
            .ToListAsync(cancellationToken);

        if (servedPositions.Count == 0)
        {
            return;
        }

        var servedItems = await db.ExamItems
            .AsNoTracking()
            .Where(i => servedPositions.Contains(i.ExamItemId))
            .Select(i => new { i.ExamItemId, i.ItemId, i.Section.SkillId })
            .ToListAsync(cancellationToken);

        var servedForSkill = servedItems.Count(i => i.SkillId == activeSkillId);

        // Not a stage-one share: nothing to route from.
        if (servedForSkill != PlacementStageOneBlueprint.ItemsPerObjectiveSkill)
        {
            return;
        }

        var skillByAnswer = await RosterSkills.ByAnswerAsync(db, attemptId, cancellationToken);
        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .Select(a => new { a.AnswerId, a.IsCorrect })
            .ToListAsync(cancellationToken);

        var score = answers.Count(a => a.IsCorrect == true
            && skillByAnswer.GetValueOrDefault(a.AnswerId) == activeSkillId);

        // The routed pair comes from the stage-two draw, not from a second copy of the routing table: the
        // draw owns the rule, and it is also the only thing that knows what this pool can supply.
        var draw = await _stageTwoDraw.DrawAsync(
            attempt.UserId,
            attempt.CertId,
            attempt.ContentTier,
            new Dictionary<int, int> { [activeSkillId] = score },
            servedItems.Select(i => i.ItemId).ToList(),
            cancellationToken: cancellationToken);

        var plan = draw.Skills.FirstOrDefault(skill => skill.SkillId == activeSkillId);
        if (plan is null)
        {
            return;
        }

        await _attemptEngine.RecordStageTwoRouteAsync(
            attemptId,
            attempt.UserId,
            activeSkillId,
            plan.Route.Lower,
            plan.Route.Upper,
            cancellationToken);
    }
}
