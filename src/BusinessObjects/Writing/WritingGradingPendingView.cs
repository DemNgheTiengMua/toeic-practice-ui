using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingGradingPendingView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatus AttemptStatus,
    int TotalResponses,
    int GradedResponses,
    int FailedResponses,
    int PendingResponses,
    bool CanRetry,
    string? FailureReason,
    IReadOnlyList<FreeResponseGradingStatus> Items
);
