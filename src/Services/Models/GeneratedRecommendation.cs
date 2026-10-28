using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record GeneratedRecommendation(
    string Text,
    RecommendationSeverity Severity,
    int? SkillId = null,
    string? TargetBandCode = null
);
