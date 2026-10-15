using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record AiInsightView(
    int InsightId,
    int UserId,
    int CertId,
    AiInsightScope Scope,
    int? PracticeSessionId,
    int? AttemptId,
    string? Narrative,
    AiInsightTrend? Trend,
    AiInsightStatus Status,
    string? FailureReason,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    string SourceFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<RecommendationItem> Recommendations,
    int? LearnerPathId = null
);
