using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record RecommendationItem(
    int RecId,
    string Text,
    RecommendationSeverity Severity,
    string? TargetBandCode,
    string? SkillName,
    DateTimeOffset CreatedAt,
    bool IsResolved);
