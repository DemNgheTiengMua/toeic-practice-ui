using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record RecommendationItemDto(
    int RecId,
    string Text,
    RecommendationSeverityDto Severity,
    string? TargetBandCode,
    string? SkillName,
    DateTimeOffset CreatedAt,
    bool IsResolved);
