using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #154: one owner for "an activity finished, so its path step is done". Before this, only a practice
/// session could close a step (<c>PracticeService</c> did it inline), a finished mock exam touched
/// nothing, and a finished lesson was only *displayed* as done at read time. Three writers now call
/// one place, so the unlock rule and the path-completed rule cannot drift apart again.
/// </summary>
public static class PathStepCompletion
{
    /// <summary>The activity kinds that can close a step, and the item type each one satisfies.</summary>
    public enum Activity
    {
        PracticeSection,
        MockExam,
        Lesson,
    }

    /// <summary>
    /// Closes the learner's open step for this activity, and unlocks whatever comes next. Returns the
    /// step id, or null when the learner has no path or the activity is not part of it.
    /// </summary>
    /// <param name="referenceId">The section, exam or lesson the activity was about. A step is only
    /// closed by an activity over the same reference — sitting Mock Exam 2 does not tick off Mock
    /// Exam 1.</param>
    /// <param name="score">The measured score, or null for a lesson.</param>
    /// <param name="accuracyPct">Measured accuracy, or null when the activity produces no score.</param>
    public static async Task<int?> CloseAsync(
        CompanionDbContext db,
        int userId,
        Activity activity,
        int referenceId,
        decimal? score,
        decimal? accuracyPct,
        CancellationToken cancellationToken = default)
    {
        var itemType = activity switch
        {
            Activity.PracticeSection => PathModuleItemType.PracticeSection.ToCode(),
            Activity.MockExam => PathModuleItemType.MockExam.ToCode(),
            _ => PathModuleItemType.Lesson.ToCode(),
        };

        // The item column that has to match, chosen with the type so the two cannot disagree. A plan
        // may reuse one exam or section in more than one step; one sitting closes the earliest open
        // one, because sitting Mock Exam 1 twice is not two sittings of it. A step that is still
        // `locked` is closeable too: a learner who sits the exam from the catalog before its week
        // arrives has done the work, and refusing to record it would leave a step only a second paid
        // sitting could close.
        var steps = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .Include(s => s.PathModuleItem)
            .Where(s => s.LearnerPath.UserId == userId
                && s.LearnerPath.Status == "active"
                && s.PathModuleItem.ItemType == itemType
                && s.Status != "done")
            .OrderBy(s => s.WeekNumber ?? 0)
            .ThenBy(s => s.StepId)
            .ToListAsync(cancellationToken);

        var step = steps
            .Where(s => activity switch
            {
                Activity.PracticeSection => s.PathModuleItem.RefSectionId == referenceId,
                Activity.MockExam => s.PathModuleItem.RefExamId == referenceId,
                _ => s.PathModuleItem.RefLessonId == referenceId,
            })
            .FirstOrDefault();

        if (step is null)
        {
            return null;
        }

        return await CloseStepAsync(db, step, score, accuracyPct, cancellationToken);
    }

    /// <summary>Closes one step and unlocks the next. Public so a caller that already holds the step
    /// (the practice session path) does not have to look it up again.</summary>
    public static async Task<int> CloseStepAsync(
        CompanionDbContext db,
        LearnerPathStep step,
        decimal? score,
        decimal? accuracyPct,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        step.Status = "done";
        step.CompletedAt ??= now;
        step.Score = score;
        step.ActualAccuracyPct = accuracyPct;

        await db.SaveChangesAsync(cancellationToken);

        await UnlockNextAsync(db, step.LearnerPathId, cancellationToken);

        return step.StepId;
    }

    /// <summary>
    /// Unlocks the first step still locked, in plan order, and marks the path complete when nothing
    /// is left. Re-read from the database rather than trusted from a caller's list, because two
    /// activities can finish close together.
    /// </summary>
    public static async Task UnlockNextAsync(
        CompanionDbContext db, int learnerPathId, CancellationToken cancellationToken = default)
    {
        var next = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == learnerPathId && s.Status == "locked")
            .OrderBy(s => s.WeekNumber ?? 0)
            .ThenBy(s => s.StepId)
            .FirstOrDefaultAsync(cancellationToken);

        if (next is not null)
        {
            next.Status = "available";
        }

        // The path is complete when no step is left undone. Asking this after the unlock, and asking
        // the database rather than the caller's list, is what makes the last step close the path.
        var remaining = await db.LearnerPathSteps
            .CountAsync(s => s.LearnerPathId == learnerPathId && s.Status != "done", cancellationToken);

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
