using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionItem(
    int QuestionId,
    SavedQuestionSource Source,
    DateTimeOffset SavedAt,
    int SectionId,
    string SectionCode,
    string SectionName,
    int? CategoryId,
    string? CategoryName,
    string Stem,
    string? Passage,
    string? ImagePath,
    string? AudioPath,
    string? Hint,
    string? Explanation,
    string? Transcript,
    IReadOnlyList<SavedQuestionOption> Options);
