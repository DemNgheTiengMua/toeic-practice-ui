namespace Contracts;

public interface IWritingClient
{
    Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, CancellationToken cancellationToken = default);
    Task<WritingInstructionsDto> GetWritingInstructionsAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingInstructionsAsync(examId, cancellationToken);
    Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, CancellationToken cancellationToken = default);
    Task<StartWritingAttemptResponse> StartWritingAttemptAsync(int examId, string bearerToken, CancellationToken cancellationToken = default) => StartWritingAttemptAsync(examId, cancellationToken);
    Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, CancellationToken cancellationToken = default);
    Task<WritingTakeDto> GetWritingTakeAsync(int attemptId, int? questionId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingTakeAsync(attemptId, questionId, cancellationToken);
    Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, CancellationToken cancellationToken = default);
    Task<SaveWritingDraftResponse> SaveWritingDraftAsync(int attemptId, SaveWritingDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveWritingDraftAsync(attemptId, request, cancellationToken);
    Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<SubmitWritingResponse> SubmitWritingAttemptAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => SubmitWritingAttemptAsync(attemptId, cancellationToken);
    Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<WritingGradingPendingDto> GetWritingGradingPendingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingGradingPendingAsync(attemptId, cancellationToken);
    Task RetryWritingGradingAsync(int attemptId, CancellationToken cancellationToken = default);
    Task RetryWritingGradingAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => RetryWritingGradingAsync(attemptId, cancellationToken);
    Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, CancellationToken cancellationToken = default);
    Task<WritingReviewDto> GetWritingReviewAsync(int attemptId, string bearerToken, CancellationToken cancellationToken = default) => GetWritingReviewAsync(attemptId, cancellationToken);
}
