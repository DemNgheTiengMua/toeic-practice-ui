using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingQuestionView(
    int QuestionId,
    int DisplayOrder,
    string Stem,
    int? MinWords,
    int? MaxWords,
    int? ResponseSeconds,
    string? Hint,
    string SectionCode,
    string SectionName,
    string? GroupImagePath,
    string? GroupImageAlt,
    string? ResponseText,
    DateTimeOffset? DraftSavedAt,
    FreeResponseStatus Status
);
