using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SaveDraftResult(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount
);
