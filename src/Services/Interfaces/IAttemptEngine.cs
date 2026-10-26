using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;

namespace Services;

public interface IAttemptEngine
{
    Task<ExamAttempt> CreateAttemptAsync(
        int userId,
        int certId,
        int examId,
        int durationMinutes,
        CancellationToken cancellationToken = default);

    Task<AttemptTakeView> GetAttemptTakeAsync(
        int attemptId,
        int userId,
        int? questionDisplayOrder,
        CancellationToken cancellationToken = default);

    Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int attemptId,
        int userId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default,
        string? responseText = null,
        string? selectedOptionIdsJson = null);

    Task<SaveAttemptDraftResult> SaveDraftAsync(
        int attemptId,
        int userId,
        int questionId,
        string? responseText,
        CancellationToken cancellationToken = default);

    Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null);

    Task<AttemptResultView> GetAttemptResultAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<AttemptReviewView> GetAttemptReviewAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<int> AutoSubmitExpiredAttemptsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken = default);

    Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken = default);

    Task FinalizeAiGradedAttemptAsync(int attemptId, CancellationToken cancellationToken = default);

    Task<bool> RequiresPreflightAsync(int examId, CancellationToken cancellationToken = default);

    Task<PreflightStatusView> GetPreflightStatusAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<PreflightStatusView> VerifyPreflightAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task CancelAttemptAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaveAttemptAnswerResult>> SaveBatchAnswersAsync(
        int attemptId,
        int userId,
        IReadOnlyList<BatchAnswerItem> answers,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionToNextSkillAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionUnitAsync(
        int attemptId,
        int targetUnitNumber,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> TransitionGroupAsync(
        int attemptId,
        int targetGroupId,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<AttemptTransitionResult> CompleteAudioAndStartReviewAsync(
        int attemptId,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<SittingSyncResult> SaveSittingSyncAsync(
        int attemptId,
        SittingSyncRequest request,
        int? userId = null,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default);

    Task<AttemptResumeView> ResumeAttemptByCodeAsync(
        string authorizationCode,
        int userId,
        CancellationToken cancellationToken = default);

    WritingEvaluationResult EvaluateWriting(Question question, string? responseText);

    Task<SaveSpeakingAudioResult> SaveSpeakingAudioAsync(
        int attemptId,
        int questionId,
        int userId,
        Stream stream,
        string contentType,
        string fileName,
        int? durationMs,
        CancellationToken cancellationToken = default);

    Task<SaveSpeakingAudioResult> SaveSpeakingAudioAsync(
        int attemptId,
        int questionId,
        int userId,
        string audioPath,
        int? durationMs,
        CancellationToken cancellationToken = default);
}
