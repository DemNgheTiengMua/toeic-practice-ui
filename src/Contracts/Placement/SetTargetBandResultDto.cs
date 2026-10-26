using System;

namespace Contracts;

public sealed record SetTargetBandResultDto(
    int UserId,
    int CertId,
    string TargetBandCode,
    DateTimeOffset SetAt);
