using BusinessObjects;
using Contracts;

namespace WebAPI.Endpoints;

public static partial class InsightEndpoints
{
    private static AiInsightDto ToDto(AiInsightView view) => new(
        view.InsightId,
        view.UserId,
        view.CertId,
        view.Scope.MapTo<AiInsightScopeDto>(),
        view.PracticeSessionId,
        view.AttemptId,
        view.Narrative,
        view.Trend?.MapTo<AiInsightTrendDto>(),
        view.Status.MapTo<AiInsightStatusDto>(),
        view.FailureReason,
        view.Model,
        view.PromptTokens,
        view.CompletionTokens,
        view.SourceFingerprint,
        view.CreatedAt,
        view.CompletedAt,
        view.Recommendations.Select(r => new RecommendationDto(
            r.RecId,
            view.InsightId,
            view.PracticeSessionId,
            view.AttemptId,
            r.Text,
            r.Severity.MapTo<RecommendationSeverityDto>(),
            r.TargetBandCode,
            r.SkillName,
            r.IsResolved,
            r.CreatedAt)).ToList(),
        view.LearnerPathId
    );
}
