using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingQuestionView(
    int QuestionId,
    int DisplayOrder,
    string Stem,
    int PrepSeconds,
    int ResponseSeconds,
    string? Hint,
    string SectionCode,
    string SectionName,
    string? GroupImagePath,
    string? GroupImageAlt,
    string? GroupAudioPath,
    string? AudioPath,
    int? DurationMs,
    string? ResponseText,
    DateTimeOffset? DraftSavedAt,
    FreeResponseStatus Status
);
