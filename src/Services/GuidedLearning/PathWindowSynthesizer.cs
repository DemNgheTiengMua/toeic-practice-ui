using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #233 / D10: the four-week window's one mover.
/// <para>
/// A slide closes the window's <b>first</b> week and synthesises <b>one</b> week at the far end. The
/// three weeks in between are untouched rows, which is what makes "synthesis does not regenerate all
/// four" structural rather than a promise.
/// </para>
/// <para>
/// <b>The close condition is <see cref="PathWindow.Closes"/>, and it is the only copy.</b> This class
/// reads whether the first week is finished and hands both that and today's date to that function; it
/// never decides a boundary itself, so the calendar rule and the work rule cannot disagree.
/// </para>
/// <para>
/// <b>Idempotence.</b> <see cref="LearnerPath.WindowSlides"/> is the token. A slide re-reads the path,
/// archives, advances and increments; a second slide arriving with the same token finds it moved and
/// archives nothing. The unique key on <c>(LearnerPathId, WeekNumber)</c> is the database half of the
/// same guard: two processes that both win the read collide on the archive insert rather than writing
/// two archived weeks for one number.
/// </para>
/// </summary>
public sealed class PathWindowSynthesizer(CompanionDbContext db)
{
    /// <summary>
    /// Advances the window for one path as far as it owes, and returns the number of weeks archived.
    /// Returns 0 when the window is not due for a slide — which is the ordinary case, not an error.
    /// </summary>
    /// <param name="today">The calendar day to judge the boundary against. Passed in so a test can sit
    /// on day 8 without waiting a week, and so there is one clock rather than several.</param>
    /// <param name="pacingOf">The goal's pacing, so the synthesised week is sized by the same arithmetic
    /// #234 landed rather than by the retired count-first rule. Read lazily: a path that owes no slide
    /// pays nothing.</param>
    /// <param name="blocksOf">The skills the far end may plan from, re-read after the closing week so
    /// the new week reflects what the learner can do <i>now</i>.</param>
    public async Task<int> AdvanceAsync(
        int learnerPathId,
        DateOnly today,
        Func<CancellationToken, Task<LearningPathPlanner.WeeklyPacing>> pacingOf,
        Func<CancellationToken, Task<IReadOnlyList<SkillPlanBlock>>> blocksOf,
        CancellationToken cancellationToken = default)
    {
        var path = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.LearnerPathId == learnerPathId, cancellationToken)
            ?? throw new LearningPathException(
                LearningPathException.PathNotFound, $"Path {learnerPathId} not found.");

        if (path.Status != LearnerPathStatus.Active.ToCode())
        {
            return 0;
        }

        var archived = 0;
        foreach (var week in await ClosedWeeksAsync(path, today, cancellationToken))
        {
            if (!await SlideOnceAsync(path, week, pacingOf, blocksOf, cancellationToken))
            {
                // Another slide won the token between the read and the write. Stop rather than loop:
                // the week it closed is already archived and this caller has nothing left to do.
                break;
            }

            archived++;
        }

        // The count of weeks THIS caller archived, not the count that looked due. A stale caller owes
        // one and archives none, and must say so rather than claim work another caller did.
        return archived;
    }

    /// <summary>
    /// The weeks this path owes a close for right now. Reads the first live week's steps, because
    /// "all its steps are done" is the one input the pure rule cannot read.
    /// </summary>
    private async Task<IReadOnlyList<PathWindow.ClosedWeek>> ClosedWeeksAsync(
        LearnerPath path, DateOnly today, CancellationToken cancellationToken)
    {
        var firstWeek = PathWindow.FirstLiveWeek(path.ActiveWeek);

        var statuses = await db.LearnerPathSteps
            .AsNoTracking()
            .Where(s => s.LearnerPathId == path.LearnerPathId && s.WeekNumber == firstWeek)
            .Select(s => s.Status)
            .ToListAsync(cancellationToken);

        // A week holding no steps is not a finished week: there was nothing to do, so nothing was done.
        // Closing it would archive an empty week and plan the next one from no evidence.
        var allDone = statuses.Count > 0
            && statuses.All(s => s == LearnerPathStepStatus.Done.ToCode());

        return PathWindow.Closes(path.WindowStartDate, path.ActiveWeek, today, allDone);
    }

    /// <summary>
    /// One slide. Returns false when the window moved underneath this caller, so the slide was not
    /// this caller's to make.
    /// </summary>
    private async Task<bool> SlideOnceAsync(
        LearnerPath path,
        PathWindow.ClosedWeek week,
        Func<CancellationToken, Task<LearningPathPlanner.WeeklyPacing>> pacingOf,
        Func<CancellationToken, Task<IReadOnlyList<SkillPlanBlock>>> blocksOf,
        CancellationToken cancellationToken)
    {
        var expectedSlides = path.WindowSlides;

        // Re-read rather than trusting the copy read before the loop: a concurrent slide may have
        // committed since.
        await db.Entry(path).ReloadAsync(cancellationToken);
        if (path.WindowSlides != expectedSlides)
        {
            return false;
        }

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == path.LearnerPathId && s.WeekNumber == week.WeekNumber)
            .ToListAsync(cancellationToken);

        Archive(path, week, steps);
        ReleaseUnattempted(steps);

        var requeue = PathWindow.TopicsToRequeue(steps.Select(s => (s.TopicId, s.Status)));

        var (windowStart, activeWeek) = PathWindow.AfterClose(path.WindowStartDate, path.ActiveWeek, week);
        path.WindowStartDate = windowStart;
        path.ActiveWeek = activeWeek;
        path.WindowSlides = expectedSlides + 1;

        // The far end, from what the learner can do now. Only this week is new: the three weeks still
        // live keep their rows and their answers, so nothing rewrites history.
        var farEnd = PathWindow.LastLiveWeek(path.ActiveWeek);
        var blocks = (await blocksOf(cancellationToken))
            .Where(b => b.TopicIds.Count > 0)
            .ToList();

        if (blocks.Count > 0)
        {
            var planned = LearningPathPlanner.PlanWeek(
                farEnd, await pacingOf(cancellationToken), blocks, requeue);

            var blocksBySkill = blocks.ToDictionary(b => b.SkillId);
            var sequence = await MaxSequenceAsync(path.LearnerPathId, cancellationToken);

            foreach (var step in planned)
            {
                var block = blocksBySkill[step.SkillId];
                db.LearnerPathSteps.Add(new LearnerPathStep
                {
                    LearnerPathId = path.LearnerPathId,
                    CertId = path.CertId,
                    SkillId = step.SkillId,
                    Modality = block.Modality.ToCode(),
                    TopicId = step.TopicId,
                    TargetCefr = step.TargetCefr.Code,
                    StepType = step.StepType.ToCode(),
                    SizingTier = step.SizingTier.ToCode(),
                    RefLessonId = step.StepType is PathStepType.ConceptLesson or PathStepType.ExpandedRemedial
                        ? block.LessonId
                        : null,
                    RefExamId = step.StepType is PathStepType.MockSection ? block.MockExamId : null,
                    // The far end starts locked; UnlockNextAsync opens whichever step is next. Making
                    // the first one available here would be a second place deciding what is open.
                    Status = LearnerPathStepStatus.Locked.ToCode(),
                    WeekNumber = farEnd,
                    WeekSequence = ++sequence,
                    PlannedItemCount = step.PlannedItemCount,
                    EstimatedMinutes = step.EstimatedMinutes,
                    TargetAccuracyPct = steps.FirstOrDefault()?.TargetAccuracyPct ?? 70,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // A learner who was mid-step when the boundary arrived resumes that step: the window moved, but
        // the step's row and its saved answers were not rewritten. UnlockNextAsync only ever opens a
        // *locked* step, so an in-progress one keeps its status and its session.
        await PathStepCompletion.UnlockNextAsync(db, path.LearnerPathId, cancellationToken);
        return true;
    }

    /// <summary>
    /// The archived week's snapshot, declared by its one writer (ADR-0012 / PRN-01: the writer is a
    /// column on the write).
    /// </summary>
    private void Archive(
        LearnerPath path, PathWindow.ClosedWeek week, IReadOnlyList<LearnerPathStep> steps)
    {
        var scored = steps.Where(s => s.ActualAccuracyPct.HasValue).ToList();

        db.LearnerPathWeeks.Add(new LearnerPathWeek
        {
            LearnerPathId = path.LearnerPathId,
            CertId = path.CertId,
            WeekNumber = week.WeekNumber,
            StartedOn = week.StartedOn,
            ClosedOn = week.ClosedOn,
            CloseReason = week.CloseReason,
            PlannedSteps = steps.Count,
            CompletedSteps = steps.Count(s => s.Status == LearnerPathStepStatus.Done.ToCode()),
            PlannedMinutes = steps.Sum(s => s.EstimatedMinutes ?? 0),
            AverageAccuracyPct = scored.Count > 0
                ? Math.Round(scored.Average(s => s.ActualAccuracyPct!.Value), 2)
                : null,
            RequeuedTopicIdsJson = RequeuedTopics.Serialize(
                PathWindow.TopicsToRequeue(steps.Select(s => (s.TopicId, s.Status)))),
            ArchivedAt = DateTimeOffset.UtcNow,
            WrittenByService = DerivedFactWriters.WindowSynthesis,
            ProjectionVersion = path.WindowSlides + 1,
        });
    }

    /// <summary>
    /// D10: "answers already given still count toward capability and unattempted items return to the
    /// pool."
    /// <para>
    /// An answered item is already a <c>PracticeAnswers</c> row, which is what the capability and
    /// weakness readers count — so it keeps counting, and nothing here touches it. An <b>unattempted</b>
    /// slot is a row with no content; leaving it pins the item to a step whose week has closed, and the
    /// next draw reads it as already seen. Deleting the empty rows returns those items to the pool
    /// without touching one answer the learner gave.
    /// </para>
    /// <para>
    /// <b>The session's status is deliberately not touched.</b> A learner who was mid-step at the
    /// boundary keeps that step's session, which is what lets <c>StartStepAsync</c> resume at the exact
    /// unsubmitted item instead of drawing a fresh set. A session whose every row was unattempted holds
    /// no evidence, so it is removed and the step is free to draw again.
    /// </para>
    /// </summary>
    private void ReleaseUnattempted(IReadOnlyList<LearnerPathStep> steps)
    {
        var stepIds = steps.Select(s => s.StepId).ToHashSet();
        if (stepIds.Count == 0)
        {
            return;
        }

        var sessions = db.PracticeSessions
            .Where(ps => ps.LearnerPathStepId != null
                && stepIds.Contains(ps.LearnerPathStepId.Value)
                && ps.Status == PracticeStatus.InProgress.ToCode())
            .ToList();

        foreach (var session in sessions)
        {
            var answers = db.PracticeAnswers
                .Where(pa => pa.SessionId == session.SessionId)
                .ToList();

            var unattempted = answers.Where(pa => !IsAnswered(pa)).ToList();
            db.PracticeAnswers.RemoveRange(unattempted);

            var attempted = answers.Count - unattempted.Count;
            if (attempted == 0)
            {
                db.PracticeSessions.Remove(session);
                continue;
            }

            // The count follows the remaining rows: a summary claiming eight questions for a session
            // holding three is a lie about what the learner sat, and the step's accuracy reads it.
            session.QuestionCount = attempted;
        }
    }

    /// <summary>The one definition of "this answer carries content": a productive pool answer is
    /// stored ungraded and is still an answer.</summary>
    private static bool IsAnswered(PracticeAnswer answer) =>
        AnswerContent.IsAnswered(
            answer.SelectedOptionId, answer.SelectedLabel, answer.ResponseText, answer.SelectedOptionIdsJson);

    /// <summary>
    /// The highest sequence the path has used. A slide must not reuse a number an earlier week used, or
    /// <c>UnlockNextAsync</c>'s (week, sequence) ordering would put the new week's first step before one
    /// the learner has not reached.
    /// </summary>
    private async Task<int> MaxSequenceAsync(int learnerPathId, CancellationToken cancellationToken)
    {
        var max = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == learnerPathId)
            .Select(s => (int?)s.WeekSequence)
            .MaxAsync(cancellationToken);

        return max ?? 0;
    }
}
