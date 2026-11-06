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
    int TotalSteps,
    int CompletedSteps,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfo> CefrBands,
    List<PathStepInfo> OpenSteps,
    int? NextStepId)
{
    /// <summary>
    /// #154/#232: the same steps grouped into the four planning weeks, with each week's On Track
    /// verdict. Empty for a path whose steps carry no week number.
    /// </summary>
    public List<LearningPathWeekSummary> Weeks { get; init; } = [];

    /// <summary>
    /// #232: the per-skill target levels this path is pitched at, keyed by skill code. Every step at a
    /// skill carries that skill's own level (D10), so this is a read of the steps, not a second fact.
    /// </summary>
    public IReadOnlyDictionary<string, CefrLevel> SkillLevels { get; init; } =
        new Dictionary<string, CefrLevel>();
}
