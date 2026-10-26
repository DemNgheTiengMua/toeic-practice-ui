namespace Contracts;

public interface IMockExamsClient
{
    Task<MockExamListViewDto> GetMockExamsAsync(CancellationToken cancellationToken = default);
    Task<MockExamListViewDto> GetMockExamsAsync(string bearerToken, CancellationToken cancellationToken = default) => GetMockExamsAsync(cancellationToken);
    Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<MockExamInstructionsDto> GetMockExamInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetMockExamInstructionsAsync(examId, cancellationToken);
    Task<StartMockExamResponse> StartMockExamAsync(int examId, CancellationToken cancellationToken = default);
    Task<StartMockExamResponse> StartMockExamAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => StartMockExamAsync(examId, cancellationToken);
    Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, CancellationToken cancellationToken = default);
    Task<AttemptTakeDto> GetAttemptTakeAsync(int attemptId, int? q, string bearerToken, CancellationToken cancellationToken = default) => GetAttemptTakeAsync(attemptId, q, cancellationToken);
    Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, CancellationToken cancellationToken = default);
    Task<SaveAnswerResultDto> SaveAttemptAnswerAsync(int attemptId, int questionId, SaveAnswerRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveAttemptAnswerAsync(attemptId, questionId, request, cancellationToken);
    Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(int attemptId, SaveAttemptDraftRequest request, CancellationToken cancellationToken = default);
    Task<SaveAttemptDraftResponse> SaveAttemptDraftAsync(int attemptId, SaveAttemptDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveAttemptDraftAsync(attemptId, request, cancellationToken);
    Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SubmitAttemptResponse> SubmitAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => SubmitAttemptAsync(attemptId, cancellationToken);
    Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(int attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken = default);
    Task<SubmitAttemptResponse> SubmitAttemptWithDraftsAsync(int attemptId, SubmitAttemptRequest request, string bearerToken, CancellationToken cancellationToken = default) => SubmitAttemptWithDraftsAsync(attemptId, request, cancellationToken);
    Task<AttemptTransitionResponse> TransitionSkillAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AttemptTransitionResponse> TransitionSkillAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => TransitionSkillAsync(attemptId, cancellationToken);
    Task<AttemptTransitionResponse> TransitionUnitAsync(int attemptId, TransitionUnitRequest request, CancellationToken cancellationToken = default);
    Task<AttemptTransitionResponse> TransitionUnitAsync(int attemptId, TransitionUnitRequest request, string bearerToken, CancellationToken cancellationToken = default) => TransitionUnitAsync(attemptId, request, cancellationToken);
    Task<AttemptTransitionResponse> TransitionGroupAsync(int attemptId, TransitionGroupRequest request, CancellationToken cancellationToken = default);
    Task<AttemptTransitionResponse> TransitionGroupAsync(int attemptId, TransitionGroupRequest request, string bearerToken, CancellationToken cancellationToken = default) => TransitionGroupAsync(attemptId, request, cancellationToken);
    Task<AttemptTransitionResponse> StartAudioReviewAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AttemptTransitionResponse> StartAudioReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => StartAudioReviewAsync(attemptId, cancellationToken);
    Task CancelAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task CancelAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => CancelAttemptAsync(attemptId, cancellationToken);
    Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AttemptResultDto> GetAttemptResultAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetAttemptResultAsync(attemptId, cancellationToken);
    Task<ExamHistoryDto> GetExamHistoryAsync(CancellationToken cancellationToken = default);
    Task<ExamHistoryDto> GetExamHistoryAsync(string bearerToken, CancellationToken cancellationToken = default) => GetExamHistoryAsync(cancellationToken);
    Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AttemptReviewDto> GetAttemptReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetAttemptReviewAsync(attemptId, cancellationToken);
    Task<AiInsightDto> GetExamInsightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetExamInsightAsync(attemptId, cancellationToken);
    Task RetryExamInsightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task RetryExamInsightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => RetryExamInsightAsync(attemptId, cancellationToken);
    Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<PreflightStatusDto> GetPreflightStatusAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetPreflightStatusAsync(attemptId, cancellationToken);
    Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<PreflightStatusDto> VerifyPreflightAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => VerifyPreflightAsync(attemptId, cancellationToken);
    Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, CancellationToken cancellationToken = default);
    Task<SittingSyncDto> SaveSittingSyncAsync(int attemptId, SittingSyncRequestDto request, string bearerToken, CancellationToken cancellationToken = default) => SaveSittingSyncAsync(attemptId, request, cancellationToken);
    Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, CancellationToken cancellationToken = default);
    Task<AttemptResumeDto> ResumeAttemptByCodeAsync(ResumeAttemptRequest request, string bearerToken, CancellationToken cancellationToken = default) => ResumeAttemptByCodeAsync(request, cancellationToken);
}
