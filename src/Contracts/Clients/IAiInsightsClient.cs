namespace Contracts;

public interface IAiInsightsClient
{
    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => GetPracticeInsightAsync(sessionId, cancellationToken);
    Task RetryPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default);
    Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) => RetryPracticeInsightAsync(sessionId, cancellationToken);
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
    Task RetryInsightAsync(int insightId, CancellationToken cancellationToken = default);
    Task RetryInsightAsync(int insightId, string bearerToken, CancellationToken cancellationToken = default) => RetryInsightAsync(insightId, cancellationToken);
    Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => GetDashboardInsightAsync(certId, cancellationToken);
    Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => RetryDashboardInsightAsync(certId, cancellationToken);
    Task<AiInsightDto> GetPathInsightAsync(CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPathInsightAsync(cancellationToken);
    Task RetryPathInsightAsync(CancellationToken cancellationToken = default);
    Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => RetryPathInsightAsync(cancellationToken);
}
