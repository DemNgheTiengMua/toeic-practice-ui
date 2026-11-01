using System;
using System.Collections.Generic;

namespace Contracts;

public record WritingErrorSpanDto(
    string OriginalSnippet,
    string CorrectedSnippet,
    string Category,
    string ExplanationVi
);

public record WritingQuestionReviewItemDto(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? ResponseText,
    int WordCount,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingCriterionScoreItemDto> Criteria,
    // E2.1 (#151): a located error with its correction, the one change worth making first, and a
    // rewrite of the weakest sentence. Empty when the grading stored nothing to fix.
    IReadOnlyList<WritingErrorSpanDto>? ErrorSpans = null,
    string? PriorityFix = null,
    string? ModelRewrite = null
);
