using System;
using System.Collections.Generic;

namespace Contracts;

public record StartWritingAttemptResponse(
    int AttemptId,
    int ExamId,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted
);

