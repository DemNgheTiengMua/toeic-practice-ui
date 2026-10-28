using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingQuestionReviewItemDto(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? AudioPath,
    int? DurationMs,
    string? Transcript,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<SpeakingCriterionScoreItemDto> Criteria
);

