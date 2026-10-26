using System.Collections.Generic;

namespace Services;

public sealed record WritingEvaluationResult(
    int WordCount,
    bool IsUnderLength,
    int? MinWords,
    bool HasRequiredKeywords,
    IReadOnlyList<string> MissingKeywords,
    string? Feedback);
