using System;

namespace Contracts;

public sealed record SubmitAttemptResponse(
    int AttemptId,
    AttemptStatusDto Status,
    int TotalScore,
    string? BandCode,
    string? BandName,
    DateTimeOffset SubmittedAt,
    bool IsAutoSubmitted);
