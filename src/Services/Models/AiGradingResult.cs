using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// A grading outcome. On success every criterion that was asked for is present in <see cref="Scores"/>;
/// a grader that cannot supply one reports failure instead of a number (E2.1, #151).
/// </summary>
/// <param name="ErrorSpans">E2.1 (#151): located errors, at most five, each quoting the candidate's own text.</param>
/// <param name="PriorityFix">E2.1 (#151): the single change that would improve this text most.</param>
/// <param name="ModelRewrite">E2.1 (#151): the grader's rewrite of the weakest part of the answer.</param>
public record AiGradingResult(
    bool Success,
    string? FailureReason,
    string? Model,
    IReadOnlyList<AiCriterionScoreResult> Scores,
    string? Transcript = null,
    IReadOnlyList<BusinessObjects.AiErrorSpan>? ErrorSpans = null,
    string? PriorityFix = null,
    string? ModelRewrite = null
);
