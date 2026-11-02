using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record LearningPathSummaryView(
    bool HasActivePath,
    CertificateOption ActiveCert,
    int? LearnerPathId,
    string? FromBandCode,
    string? ToBandCode,
    string? FromBandName,
    string? ToBandName,
    string? CurrentBandCode,
    int? CurrentScore,
    string? TargetBandCode,
    int? TargetScore,
    LearnerPathStatus? Status,
    DateTimeOffset? StartedAt,
    int TotalModules,
    int CompletedModules,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfo> CefrBands,
    List<LearningPathModuleSummary> Modules,
    int? NextModuleId)
{
    /// <summary>#154: the same steps grouped into the four planning weeks, with each week's On Track
    /// verdict. Empty for a path generated before #154 (its steps carry no week number).</summary>
    public List<LearningPathWeekSummary> Weeks { get; init; } = [];
}
