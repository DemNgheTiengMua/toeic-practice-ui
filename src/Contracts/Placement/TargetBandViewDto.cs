using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #153: the one-step goal screen. The band list and the assessed band come from the placement flow;
/// <see cref="Goal"/> carries whatever goal is already saved, so the form can be reopened and
/// corrected without losing the deadline and the pace.
/// </summary>
public sealed record TargetBandViewDto(
    CertificateOptionDto ActiveCert,
    string? CurrentBandCode,
    int? CurrentTotalScore,
    string? CurrentBandName,
    string? TargetBandCode,
    List<CefrBandInfoDto> CefrBands,
    List<TargetBandOptionDto> AvailableBands,
    GoalStatusDto? Goal = null);
