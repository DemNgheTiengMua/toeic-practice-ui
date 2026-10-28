using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record FreeResponseGradingStatus(
    int FreeResponseId,
    int QuestionId,
    int DisplayOrder,
    AiGradingStatus Status,
    int AttemptCount,
    string? FailureReason
);
