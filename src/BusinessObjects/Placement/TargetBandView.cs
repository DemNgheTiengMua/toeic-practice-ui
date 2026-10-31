using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>#153: <c>Goal</c> carries the saved deadline, pace and verdict so the one-step screen can
/// be reopened and corrected instead of starting from an empty form.</summary>
public sealed record TargetBandView(
    CertificateOption ActiveCert,
    string? CurrentBandCode,
    int? CurrentTotalScore,
    string? CurrentBandName,
    string? TargetBandCode,
    List<CefrBandInfo> CefrBands,
    List<TargetBandOption> AvailableBands,
    GoalStatus? Goal = null);
