using System;

namespace BusinessObjects;

public sealed record SaveAttemptDraftResult(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount);
