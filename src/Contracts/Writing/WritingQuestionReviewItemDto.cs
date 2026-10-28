using System;
using System.Collections.Generic;

namespace Contracts;

public record WritingQuestionReviewItemDto(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? ResponseText,
    int WordCount,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingCriterionScoreItemDto> Criteria
);

