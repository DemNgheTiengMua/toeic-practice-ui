using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingQuestionReviewItem(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? ResponseText,
    int WordCount,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingCriterionScoreItem> Criteria,
    // E2.1 (#151): what the learner can act on. Empty when the grading stored nothing to fix.
    IReadOnlyList<AiErrorSpan>? ErrorSpans = null,
    string? PriorityFix = null,
    string? ModelRewrite = null
);
