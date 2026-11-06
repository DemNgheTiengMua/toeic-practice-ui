using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record LearningPathSummaryDto(
    bool HasActivePath,
    CertificateOptionDto ActiveCert,
    int? LearnerPathId,
    string? FromBandCode,
    string? ToBandCode,
    string? FromBandName,
    string? ToBandName,
    string? CurrentBandCode,
    int? CurrentScore,
    string? TargetBandCode,
    int? TargetScore,
    LearnerPathStatusDto? Status,
    DateTimeOffset? StartedAt,
    int TotalSteps,
    int CompletedSteps,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfoDto> CefrBands,
    List<PathStepDto> OpenSteps,
    int? NextStepId,
    List<LearningPathWeekSummaryDto>? Weeks = null,
    /// <summary>
    /// #232: the level each skill is pitched at. Every step at a skill carries that skill's own level
    /// (D10), so this reads the plan rather than computing a second fact. Unmeasured skills are absent.
    /// </summary>
    Dictionary<string, string>? SkillLevels = null);
