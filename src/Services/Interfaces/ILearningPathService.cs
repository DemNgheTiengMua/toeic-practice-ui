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
}
