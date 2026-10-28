using System;
using System.Collections.Generic;
using System.IO;
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

public interface ISpeakingPracticeService
{
    Task<SpeakingPracticeInstructionsView> GetInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<SpeakingAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<SpeakingTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionId, CancellationToken cancellationToken = default);

    Task<SaveSpeakingAudioResult> SaveAudioAsync(int userId, int attemptId, int questionId, Stream stream, string contentType, string fileName, int? durationMs, CancellationToken cancellationToken = default);

    Task<SubmitSpeakingResult> SubmitAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<SpeakingGradingPendingView> GetGradingPendingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task RetryGradingAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<SpeakingReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<FileStreamResultInfo?> GetAudioFileAsync(int userId, int attemptId, int questionId, CancellationToken cancellationToken = default);
}
