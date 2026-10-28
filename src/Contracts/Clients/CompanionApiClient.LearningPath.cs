using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<LearningPathSummaryDto> GetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearningPathSummaryDto>("api/learner/learning-path", bearerToken, cancellationToken);

    public Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<GenerateLearningPathRequest, LearningPathSummaryDto>("api/learner/learning-path/generate", request ?? new GenerateLearningPathRequest(), bearerToken, cancellationToken);

    public Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<LearningPathModuleDetailDto>($"api/learner/learning-path/modules/{moduleId}", bearerToken, cancellationToken);

    public Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, StartStepResultDto>($"api/learner/learning-path/steps/{stepId}/start", new { }, bearerToken, cancellationToken);

    public Task<ResetLearningPathResultDto> ResetLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, ResetLearningPathResultDto>("api/learner/learning-path/reset", new { }, bearerToken, cancellationToken);

    public Task<LearningPathSummaryDto> UpgradeLearningPathAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<object, LearningPathSummaryDto>("api/learner/learning-path/upgrade", new { }, bearerToken, cancellationToken);

    public Task<LearningPathSummaryDto> GetLearningPathAsync(CancellationToken cancellationToken = default) =>
        GetLearningPathAsync(string.Empty, cancellationToken);

    public Task<LearningPathSummaryDto> GenerateLearningPathAsync(GenerateLearningPathRequest? request, CancellationToken cancellationToken = default) =>
        GenerateLearningPathAsync(request, string.Empty, cancellationToken);

    public Task<LearningPathModuleDetailDto> GetLearningPathModuleAsync(int moduleId, CancellationToken cancellationToken = default) =>
        GetLearningPathModuleAsync(moduleId, string.Empty, cancellationToken);

    public Task<StartStepResultDto> StartLearningPathStepAsync(int stepId, CancellationToken cancellationToken = default) =>
        StartLearningPathStepAsync(stepId, string.Empty, cancellationToken);

    public Task<ResetLearningPathResultDto> ResetLearningPathAsync(CancellationToken cancellationToken = default) =>
        ResetLearningPathAsync(string.Empty, cancellationToken);

    public Task<LearningPathSummaryDto> UpgradeLearningPathAsync(CancellationToken cancellationToken = default) =>
        UpgradeLearningPathAsync(string.Empty, cancellationToken);
}
