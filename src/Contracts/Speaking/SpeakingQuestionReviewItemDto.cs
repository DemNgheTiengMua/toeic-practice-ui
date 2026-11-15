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
    IReadOnlyList<SpeakingCriterionScoreItemDto> Criteria,
    IReadOnlyList<WritingErrorSpanDto>? ErrorSpans = null,
    string? PriorityFix = null,
    string? ModelRewrite = null,
    // #376: the grader's refusal, when it refused this task.
    string? FailureReason = null
);

