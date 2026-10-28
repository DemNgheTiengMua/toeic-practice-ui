using System;
using System.Collections.Generic;

namespace Contracts;

public record SaveWritingDraftResponse(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount
);

