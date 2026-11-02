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

    Task<LearningPathModuleDetailView> GetModuleDetailAsync(int userId, int moduleId, CancellationToken cancellationToken = default);

    Task<StartStepResult> StartStepAsync(int userId, int stepId, CancellationToken cancellationToken = default);

    Task<bool> ResetActivePathAsync(int userId, CancellationToken cancellationToken = default);
}
