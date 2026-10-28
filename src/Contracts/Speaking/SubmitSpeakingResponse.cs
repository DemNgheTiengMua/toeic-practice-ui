using System;
using System.Collections.Generic;

namespace Contracts;

public record SubmitSpeakingResponse(
    int AttemptId,
    AttemptStatusDto Status,
    DateTimeOffset SubmittedAt
);

