using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    /// <summary>
    /// #214 / PLC-12: whether this sitting runs a countdown and a per-skill clock. A placement does
    /// not - it measures ability, not speed - so every place the engine would stamp a deadline asks
    /// here first rather than each of them re-reading <c>Exams.Purpose</c> and possibly disagreeing.
    /// </summary>
    private async Task<bool> RunsAClockAsync(ExamAttempt attempt, CancellationToken cancellationToken) =>
        PlacementSitting.HasCountdown(
            await db.Exams.AsNoTracking()
                .Where(exam => exam.ExamId == attempt.ExamId)
                .Select(exam => exam.Purpose)
                .SingleAsync(cancellationToken));
}
