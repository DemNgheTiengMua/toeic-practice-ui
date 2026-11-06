
namespace Contracts;

public interface ILearningPathClient
{
    Task<LearningPathSummaryDto> GetLearningPathAsync(CancellationToken cancellationToken = default);
    Task<LearningPathSummaryDto> GetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) => GetLearningPathAsync(cancellationToken);
    Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, CancellationToken cancellationToken = default);
    Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, string bearerToken, CancellationToken cancellationToken = default) => GenerateLearningPathAsync(request, cancellationToken);
    /// <summary>#232: one step by its own id. There is no module to address: a step is a coordinate
    /// the timeline renders inline.</summary>
    Task<PathStepDto?> GetLearningPathStepAsync(int stepId, CancellationToken cancellationToken = default);
    Task<PathStepDto?> GetLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) => GetLearningPathStepAsync(stepId, cancellationToken);
    Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, CancellationToken cancellationToken = default);
    Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) => StartLearningPathStepAsync(stepId, cancellationToken);
    Task<ResetLearningPathResultDto> ResetLearningPathAsync(CancellationToken cancellationToken = default);
    Task<ResetLearningPathResultDto> ResetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) => ResetLearningPathAsync(cancellationToken);
    Task<AiInsightDto> GetPathInsightAsync(CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => GetPathInsightAsync(cancellationToken);
    Task RetryPathInsightAsync(CancellationToken cancellationToken = default);
    Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) => RetryPathInsightAsync(cancellationToken);
}
