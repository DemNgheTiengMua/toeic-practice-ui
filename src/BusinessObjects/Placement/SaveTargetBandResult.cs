using System;

namespace BusinessObjects;

public sealed record SaveTargetBandResult(
    int UserId,
    int CertId,
    string TargetBandCode,
    DateTimeOffset SetAt);
