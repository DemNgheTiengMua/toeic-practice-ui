using System;
using System.Collections.Generic;

namespace Contracts;

public record SubmitWritingResponse(
    int AttemptId,
    AttemptStatusDto Status,
    DateTimeOffset SubmittedAt
);

