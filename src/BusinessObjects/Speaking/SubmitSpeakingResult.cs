using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SubmitSpeakingResult(
    int AttemptId,
    AttemptStatus Status,
    DateTimeOffset SubmittedAt
);
