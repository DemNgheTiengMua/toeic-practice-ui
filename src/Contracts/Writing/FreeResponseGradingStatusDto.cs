using System;
using System.Collections.Generic;

namespace Contracts;

public record FreeResponseGradingStatusDto(
    int FreeResponseId,
    int QuestionId,
    int DisplayOrder,
    AiGradingStatusDto Status,
    int AttemptCount,
    string? FailureReason
);

