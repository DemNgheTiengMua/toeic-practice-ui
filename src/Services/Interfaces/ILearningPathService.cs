using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public interface ILearningPathService
{
    Task<LearningPathSummaryView> GetActivePathAsync(int userId, CancellationToken cancellationToken = default);

    Task<LearningPathSummaryView> GeneratePathAsync(int userId, string? targetBandCode = null, CancellationToken cancellationToken = default, bool archiveExisting = false);

    /// <summary>
    /// #232: opens one step's detail by its own id. There is no module to address: a step is a
    /// coordinate the timeline renders inline, so this reads the step the caller already sees.
    /// </summary>
    Task<PathStepInfo?> GetStepAsync(int userId, int stepId, CancellationToken cancellationToken = default);

    Task<StartStepResult> StartStepAsync(int userId, int stepId, CancellationToken cancellationToken = default);

    Task<bool> ResetActivePathAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// #233 / D10: advances the learner's four-week window as far as the close rule owes, archiving the
    /// weeks that closed and synthesising the same number at the far end. Returns how many weeks were
    /// archived; 0 is the ordinary answer, not a failure.
    /// <para>
    /// <paramref name="today"/> exists so a test can sit on day 8 without waiting a week, and so the
    /// caller owns the clock rather than the service reading it in several places.
    /// </para>
    /// </summary>
    Task<int> AdvanceWindowAsync(int userId, DateOnly today, CancellationToken cancellationToken = default);
}
