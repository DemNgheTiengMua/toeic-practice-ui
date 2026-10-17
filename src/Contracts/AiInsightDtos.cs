using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record RecommendationDto(
    int RecId,
    int? InsightId,
    int? PracticeSessionId,
    int? AttemptId,
    string Text,
    RecommendationSeverityDto Severity,
    string? TargetBandCode,
    string? SkillName,
    bool IsResolved,
    DateTimeOffset CreatedAt
);

public sealed record AiInsightDto(
    int InsightId,
    int UserId,
    int CertId,
    AiInsightScopeDto Scope,
    int? PracticeSessionId,
    int? AttemptId,
    string? Narrative,
    AiInsightTrendDto? Trend,
    AiInsightStatusDto Status,
    string? FailureReason,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    string SourceFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<RecommendationDto> Recommendations,
    int? LearnerPathId = null,
    string? FormattedNarrative = null
);
