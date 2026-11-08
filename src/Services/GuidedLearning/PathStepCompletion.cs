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
/// #154 / #232: one owner for "an activity finished, so its path step is done". Three writers call one
/// place, so the unlock rule and the path-completed rule cannot drift apart again.
/// <para>
/// #232 changes what a step is matched by. There is no module item to compare: a lesson step names its
/// lesson and a mock step names its exam, and a drill step names neither because the selector decides
/// its items. So the match is the step's <b>route</b> plus its resource, not an item type.
/// </para>
/// <para>
/// <b>A step closes by being done, not by improving.</b> Nothing here reads the score or the accuracy
/// against a threshold: a learner who sits the activity and finishes it closes the step, and the measured
/// number is recorded beside it. Requiring an accuracy here would make a step silently unfinishable for
/// a learner who is bad at the topic, which is exactly the learner the step exists for.
/// </para>
/// </summary>
public static class PathStepCompletion
{
    /// <summary>The activity kinds that can close a step.</summary>
    public enum Activity
    {
        Drill,
        Lesson,
        MockSection,
    }

    /// <summary>
    /// Closes the learner's open step for this activity, and unlocks whatever comes next. Returns the
    /// step id, or null when the learner has no path or the activity is not part of it.
    /// </summary>
    /// <param name="referenceId">The lesson or exam the activity was about, ignored for a drill (which
    /// has no resource). A step is only closed by an activity over the same reference — sitting Mock
    /// Exam 2 does not tick off Mock Exam 1.</param>
    /// <param name="score">The measured score, or null for a lesson.</param>
    /// <param name="accuracyPct">Measured accuracy, or null when the activity produces no score.</param>
    public static async Task<int?> CloseAsync(
        CompanionDbContext db,
        int userId,
        Activity activity,
        int referenceId,
        decimal? score,
        decimal? accuracyPct,
        CancellationToken cancellationToken = default,
        string? lessonModule = null)
    {
        // The step types a route owns, taken from the planner so the two cannot disagree about which
        // type belongs to which surface. A lesson closes a lesson step; a mock closes a mock step; a
        // drill closes any of the selector-driven types.
        var route = activity switch
        {
            Activity.Drill => StepRoute.ItemSelector,
            Activity.Lesson => StepRoute.LessonReader,
            _ => StepRoute.AttemptEngine,
        };

        var stepTypes = LearningPathPlanner.StepTypesFor(route).Select(t => t.ToCode()).ToList();

        // A plan may reuse one exam or lesson in more than one step; one sitting closes the earliest
        // open one, because sitting Mock Exam 1 twice is not two sittings of it. A step that is still
        // `locked` is closeable too: a learner who sits the exam from the catalog before its week
        // arrives has done the work, and refusing to record it would leave a step only a second paid
        // sitting could close.
        var candidates = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .Where(s => s.LearnerPath.UserId == userId
                && s.LearnerPath.Status == "active"
                && stepTypes.Contains(s.StepType)
                && s.Status != "done")
            .OrderBy(s => s.WeekNumber ?? 0)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToListAsync(cancellationToken);

        var step = activity switch
        {
            // #240: a remedial step is closed by the remedial module, not by finishing the primary one.
            // Without this the primary module's completion would tick off a lesson the learner has not
            // read in its remedial form.
            Activity.Lesson => candidates.FirstOrDefault(s =>
                s.RefLessonId == referenceId
                && (lessonModule is null
                    || s.StepType == StepTypeForLessonModule(lessonModule).ToCode())),
            Activity.MockSection => candidates.FirstOrDefault(s => s.RefExamId == referenceId),
            _ => candidates.FirstOrDefault(),
        };

        if (step is null)
        {
            return null;
        }

        return await CloseStepAsync(db, step, score, accuracyPct, cancellationToken);
    }

    /// <summary>
    /// #240: the step type that reads one module. The primary module belongs to a concept lesson; the
    /// remedial module to an expanded remedial step. This is the mapping D10's table states, and it is
    /// stated once here so the reader and the closure cannot disagree about which module a step means.
    /// </summary>
    public static PathStepType StepTypeForLessonModule(string lessonModule) =>
        string.Equals(lessonModule, LessonModule.Remedial, StringComparison.OrdinalIgnoreCase)
            ? PathStepType.ExpandedRemedial
            : PathStepType.ConceptLesson;

    /// <summary>Closes one step and unlocks the next. Public so a caller that already holds the step
    /// (the practice session path) does not have to look it up again.</summary>
    public static async Task<int> CloseStepAsync(
        CompanionDbContext db,
        LearnerPathStep step,
        decimal? score,
        decimal? accuracyPct,
        CancellationToken cancellationToken = default)
    {
        // Idempotent: a step already done is left as it is. Two callers finishing close together
        // (a mock submission and the last practice answer) otherwise overwrite each other's score and
        // run the unlock twice.
        if (step.Status == "done")
        {
            return step.StepId;
        }

        // A step on a path that was reset or already completed must not be reopened by a late
        // activity. PracticeService calls this by bare step id from a session that can still be open
        // when the learner resets the path; without this the reset path's step goes done and
        // UnlockNextAsync can flip the whole path to "completed" - a path the learner never finished.
        var path = await db.LearnerPaths
            .Where(p => p.LearnerPathId == step.LearnerPathId)
            .Select(p => new { p.Status, p.ActiveWeek })
            .FirstOrDefaultAsync(cancellationToken);

        if (path is null || path.Status != LearnerPathStatus.Active.ToCode())
        {
            return step.StepId;
        }

        // #233 / D10: a week that has closed is history, and history is immutable. A session left open
        // when its week's boundary passed finishes late - and without this check it would rewrite the
        // step's status, score and accuracy on a week that is already archived. The answers the learner
        // gave still count toward capability (they are PracticeAnswers rows, untouched), but the step
        // can no longer be closed: the week it belonged to has been recorded as it actually ended.
        //
        // shortcut: the archived week's *snapshot* is frozen by its own trigger; this covers the step
        // rows. Upgrade if a ticket makes the archive re-derivable, at which point the two must agree.
        if (!PathWindow.IsLive(step.WeekNumber, path.ActiveWeek))
        {
            return step.StepId;
        }

        var now = DateTimeOffset.UtcNow;

        step.Status = "done";
        step.CompletedAt ??= now;
        step.Score = score;
        step.ActualAccuracyPct = accuracyPct;
        // #191 / D14: this class is the step closure's one writer, and the trigger on
        // LearnerPathSteps refuses any other declaration. The version moves forward so a late
        // duplicate closure cannot overwrite a result the learner has already seen.
        step.WrittenByService = DerivedFactWriters.StepClosure;
        step.ProjectionVersion++;
        db.Entry(step).Property(s => s.WrittenByService).IsModified = true;

        await db.SaveChangesAsync(cancellationToken);

        await UnlockNextAsync(db, step.LearnerPathId, cancellationToken);

        return step.StepId;
    }

    /// <summary>
    /// Unlocks the first step still locked, in plan order, and marks the path complete when nothing
    /// is left. Re-read from the database rather than trusted from a caller's list, because two
    /// activities can finish close together.
    /// <para>
    /// #233: only a step inside the <b>live window</b> can be opened. A week that has closed keeps its
    /// unfinished steps locked forever — that is what "archived rather than carried forward" means —
    /// so including them here would hand the learner a step on a week that is already history.
    /// </para>
    /// </summary>
    public static async Task UnlockNextAsync(
        CompanionDbContext db, int learnerPathId, CancellationToken cancellationToken = default)
    {
        var activeWeek = await db.LearnerPaths
            .Where(p => p.LearnerPathId == learnerPathId)
            .Select(p => p.ActiveWeek)
            .FirstOrDefaultAsync(cancellationToken);

        var locked = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == learnerPathId && s.Status == "locked")
            .OrderBy(s => s.WeekNumber ?? 0)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToListAsync(cancellationToken);

        var next = locked.FirstOrDefault(s => PathWindow.IsLive(s.WeekNumber, activeWeek));
        if (next is not null)
        {
            next.Status = "available";
        }

        // The path is complete when no <b>live</b> step is left undone. Asking this after the unlock,
        // and asking the database rather than the caller's list, is what makes the last step close the
        // path. #233: an unfinished step on an archived week is excluded — history that can never be
        // finished again, so counting it would make completion unreachable after any partial archive.
        var liveSteps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == learnerPathId)
            .Select(s => new { s.Status, s.WeekNumber })
            .ToListAsync(cancellationToken);

        var remaining = liveSteps
            .Count(s => s.Status != "done" && PathWindow.IsLive(s.WeekNumber, activeWeek));

        if (remaining == 0)
        {
            var path = await db.LearnerPaths
                .FirstOrDefaultAsync(p => p.LearnerPathId == learnerPathId, cancellationToken);

            if (path is not null)
            {
                path.Status = "completed";
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
