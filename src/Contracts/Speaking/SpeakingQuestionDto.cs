using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingQuestionDto(
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
    FreeResponseStatusDto Status,
    // #152: the words a Read Aloud learner is asked to read. NULL on a free-speech question.
    string? ReadAloudText = null
);
