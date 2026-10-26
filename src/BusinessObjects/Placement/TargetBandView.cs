using System.Collections.Generic;

namespace BusinessObjects;

public sealed record TargetBandView(
    CertificateOption ActiveCert,
    string? CurrentBandCode,
    int? CurrentTotalScore,
    string? CurrentBandName,
    string? TargetBandCode,
    List<CefrBandInfo> CefrBands,
    List<TargetBandOption> AvailableBands);
