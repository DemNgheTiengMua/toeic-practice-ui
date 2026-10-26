using System.Collections.Generic;

namespace Contracts;

public sealed record TargetBandViewDto(
    CertificateOptionDto ActiveCert,
    string? CurrentBandCode,
    int? CurrentTotalScore,
    string? CurrentBandName,
    string? TargetBandCode,
    List<CefrBandInfoDto> CefrBands,
    List<TargetBandOptionDto> AvailableBands);
