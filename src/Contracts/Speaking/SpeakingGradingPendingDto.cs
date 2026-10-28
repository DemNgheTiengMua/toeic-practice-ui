using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingGradingPendingDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatusDto AttemptStatus,
    int TotalResponses,
    int GradedResponses,
    int FailedResponses,
    int PendingResponses,
    bool CanRetry,
    string? FailureReason,
    IReadOnlyList<FreeResponseGradingStatusDto> Items
);

