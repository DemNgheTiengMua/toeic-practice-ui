using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingReviewView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingQuestionReviewItem> Questions,
    // E1.3 (#148): the band this writing's rubric lands in, on this certificate's own bands. An
    // estimate from one skill, never the certificate's formal result.
    string? EstimatedBandCode = null,
    string? EstimatedBandName = null
)
{
    /// <summary>
    /// #239: how many tasks actually carry a rubric score. This is the count the "one task cannot set a
    /// level" rule is about, so it is read from the graded rows rather than from
    /// <see cref="AnsweredQuestions"/>: an answer with no marks (a failed grading, a blank) is not
    /// evidence of a level and must not make a one-task result look like a two-task one.
    /// </summary>
    public int GradedQuestions { get; init; }

    /// <summary>
    /// #239: spec §5, "one graded task yields a result marked as estimated from one task". A single
    /// task generalises poorly (<c>r(1) = 0.55</c>), so the page says so in words rather than letting
    /// the band read as a measurement of the whole skill.
    /// </summary>
    public bool IsEstimatedFromOneTask => GradedQuestions == 1;
}

