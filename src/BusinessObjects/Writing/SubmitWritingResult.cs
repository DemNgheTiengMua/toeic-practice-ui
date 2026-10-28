using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SubmitWritingResult(
    int AttemptId,
    AttemptStatus Status,
    DateTimeOffset SubmittedAt
);
