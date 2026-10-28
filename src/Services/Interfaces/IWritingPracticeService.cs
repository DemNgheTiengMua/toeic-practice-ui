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

public interface IWritingPracticeService
{
    Task<WritingPracticeInstructionsView> GetInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<WritingAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<WritingTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionId, CancellationToken cancellationToken = default);

    Task<SaveDraftResult> SaveDraftAsync(int userId, int attemptId, int questionId, string? responseText, CancellationToken cancellationToken = default);

    Task<SubmitWritingResult> SubmitAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<WritingGradingPendingView> GetGradingPendingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task RetryGradingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<WritingReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
}
