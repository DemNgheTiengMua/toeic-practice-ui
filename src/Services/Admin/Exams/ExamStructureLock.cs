using System.Threading;
using System.Threading.Tasks;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #157: once an exam has a finished attempt, its structure is what that attempt was scored
/// against, so it may not change. "Structure" means anything that changes which questions exist,
/// which section they belong to, or what the right answer is - adding or deleting a section's
/// questions or groups, moving a question, and editing an option set or an answer key. Cosmetic
/// fields (names, stems, explanations, hints, notes, audio/passage stimuli) stay editable: they
/// cannot change a recorded score.
/// <para>
/// Everyone who writes groups or questions calls this, not just <see cref="AdminExamService"/>:
/// quick ingest and AI generation insert straight into the context from their own services, and
/// they were the doors that went round the lock.
/// </para>
/// </summary>
public static class ExamStructureLock
{
    public static async Task RequireEditableAsync(CompanionDbContext db, int examId, CancellationToken cancellationToken)
    {
        var finishedAttemptCount = await db.ExamAttempts.AsNoTracking()
            .CountAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);

        if (finishedAttemptCount > 0)
        {
            throw new AdminExamException(
                ApiError.Codes.ExamHasFinishedAttempts,
                $"This exam's structure is locked: {finishedAttemptCount} learner {(finishedAttemptCount == 1 ? "attempt has" : "attempts have")} already finished it. " +
                "Adding, removing or moving questions, and changing answer options, would change the score those attempts were given. " +
                "Name, description, internal note, stem, explanation and hint remain editable.",
                400);
        }
    }
}
