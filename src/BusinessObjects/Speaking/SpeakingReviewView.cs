using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingReviewView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    int? ScaledScore,
    IReadOnlyList<SpeakingQuestionReviewItem> Questions,
    SpeakingQuestionReviewItem? LowestScoredQuestion,
    // E1.3 (#148): the band this speaking rubric lands in, on this certificate's own bands.
    string? EstimatedBandCode = null,
    string? EstimatedBandName = null,
    // #152: the top of the axis ScaledScore is reported on, so the page can say "43 of 50" instead
    // of implying one certificate's range for every certificate.
    int? ScaledScoreMax = null,
    // #152: stored units per reported point. 10 on a certificate whose scale is in tenths, so a
    // stored 75 is shown as 7.5 and never as 75. Every rendered score divides by this.
    int ScoreDisplayFactor = 1
)
{
    /// <summary>
    /// #239: how many tasks actually carry a rubric score — the count the "one task cannot set a level"
    /// rule is about, read from graded rows rather than from <see cref="AnsweredQuestions"/>.
    /// </summary>
    public int GradedQuestions { get; init; }

    /// <summary>
    /// #239: spec §5, "one graded task yields a result marked as estimated from one task". A single
    /// task generalises poorly (<c>r(1) = 0.55</c>), so the page says so in words.
    /// </summary>
    public bool IsEstimatedFromOneTask => GradedQuestions == 1;
}
