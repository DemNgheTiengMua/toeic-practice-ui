using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingQuestionReviewItem(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? AudioPath,
    int? DurationMs,
    string? Transcript,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<SpeakingCriterionScoreItem> Criteria
);
