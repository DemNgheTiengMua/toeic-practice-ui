using System;

namespace Contracts;

public sealed record SaveAttemptDraftResponse(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount);
