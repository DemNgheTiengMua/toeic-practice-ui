using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SavedQuestionItemDto(
    int QuestionId,
    SavedQuestionSourceDto Source,
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
    IReadOnlyList<SavedQuestionOptionDto> Options);
