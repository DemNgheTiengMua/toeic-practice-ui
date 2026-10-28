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
    bool IsPremium,
    int TotalModules,
    int CompletedModules,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfoDto> CefrBands,
    List<LearningPathModuleSummaryDto> Modules,
    int? NextModuleId);
