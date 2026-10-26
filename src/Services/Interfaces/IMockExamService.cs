using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public interface IMockExamService
{
    Task<MockExamListView> GetMockExamsAsync(int userId, CancellationToken cancellationToken = default);

    Task<MockExamInstructionsView> GetExamInstructionsAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<ExamAttemptInfo> StartAttemptAsync(int userId, int examId, CancellationToken cancellationToken = default);

    Task<AttemptTakeView> GetAttemptTakeAsync(int userId, int attemptId, int? questionDisplayOrder, CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int userId,
        int attemptId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        CancellationToken cancellationToken = default,
        string? responseText = null,
        string? selectedOptionIdsJson = null);

    Task<SaveAttemptDraftResult> SaveDraftAsync(int userId, int attemptId, int questionId, string? responseText, CancellationToken cancellationToken = default);

    Task<AttemptResultView> SubmitAttemptAsync(
        int userId,
        int attemptId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null);

    Task CancelAttemptAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<AttemptResultView> GetAttemptResultAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<ExamHistoryView> GetExamHistoryAsync(int userId, CancellationToken cancellationToken = default);

    Task<AttemptReviewView> GetAttemptReviewAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
    Task<PreflightStatusView> GetPreflightStatusAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
    Task<PreflightStatusView> VerifyPreflightAsync(int userId, int attemptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaveAttemptAnswerResult>> SaveBatchAnswersAsync(
        int userId,
        int attemptId,
        IReadOnlyList<BatchAnswerItem> answers,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionToNextSkillAsync(
        int userId,
        int attemptId,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionUnitAsync(
        int userId,
        int attemptId,
        int targetUnitNumber,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionGroupAsync(
        int userId,
        int attemptId,
        int targetGroupId,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> CompleteAudioAndStartReviewAsync(
        int userId,
        int attemptId,
        CancellationToken cancellationToken = default);

    Task<SittingSyncResult> SaveSittingSyncAsync(
        int userId,
        int attemptId,
        SittingSyncRequest request,
        CancellationToken cancellationToken = default);

    Task<AttemptResumeView> ResumeAttemptByCodeAsync(
        string authorizationCode,
        int userId,
        CancellationToken cancellationToken = default);
}
