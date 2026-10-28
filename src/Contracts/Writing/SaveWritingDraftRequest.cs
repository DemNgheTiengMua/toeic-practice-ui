using System;
using System.Collections.Generic;

namespace Contracts;

public record SaveWritingDraftRequest(
    int QuestionId,
    string? ResponseText
);

