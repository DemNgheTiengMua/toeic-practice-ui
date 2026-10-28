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
    bool IsPremium,
    int TotalModules,
    int CompletedModules,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfo> CefrBands,
    List<LearningPathModuleSummary> Modules,
    int? NextModuleId);
