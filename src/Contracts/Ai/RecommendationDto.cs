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
