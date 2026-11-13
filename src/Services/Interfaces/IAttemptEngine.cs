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

    /// <summary>
    /// #274: the same creation, with the paper this sitting is served supplied by the caller. The
    /// attempt-scoped paper is recorded in the same transaction as the attempt row.
    /// </summary>
    Task<ExamAttempt> CreateAttemptAsync(
        int userId,
        int certId,
        int examId,
        int durationMinutes,
        AttemptPaperDraw? drawnPaper,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// #289: the same creation, additionally pinning a <see cref="PlacementScope"/> so a
    /// Listening &amp; Reading placement sitting omits Speaking and Writing from both drawn and
    /// static-fallback papers and skips microphone preflight checks.
    /// </summary>
    Task<ExamAttempt> CreateAttemptAsync(
        int userId,
        int certId,
        int examId,
        int durationMinutes,
        AttemptPaperDraw? drawnPaper,
        PlacementScope? placementScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// #276: record the band pair stage one routed one skill to. The sole writer of
    /// <c>AttemptStageBundles</c> (ADR-0012); returns false when there is nothing to write.
    /// </summary>
    Task<bool> RecordStageTwoRouteAsync(
        int attemptId,
        int userId,
        int skillId,
        CefrLevel bandLower,
        CefrLevel bandUpper,
        CancellationToken cancellationToken = default);

    Task<AttemptTakeView> GetAttemptTakeAsync(
        int attemptId,
        int userId,
        int? questionDisplayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>#158: the sitting projection for an exam with no attempt behind it. Writes nothing.</summary>
    Task<AttemptTakeView> GetExamPreviewTakeAsync(
        int examId,
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

    /// <summary>#216 / PLC-14: a placement's resume summary. A read - it stamps no clock.</summary>
    Task<PlacementResumeView> GetPlacementResumeAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>#216 / PLC-15: one interrupted recording, idempotent per session token so a reload
    /// cannot grant a second retry.</summary>
    Task<PlacementRecordingDecision> RecordPlacementRecordingInterruptionAsync(
        int attemptId,
        int questionId,
        int userId,
        string sessionToken,
        int capturedMs,
        CancellationToken cancellationToken = default);

    // #269: EvaluateWriting(Question, ...) is gone with the Questions table. Its only implementation
    // judged a legacy question row; a pool position is judged by
    // AttemptEngine.EvaluateWritingResponse(minWords, requiredKeywords, text), which reads exactly the
    // two facts the rule needs.

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
