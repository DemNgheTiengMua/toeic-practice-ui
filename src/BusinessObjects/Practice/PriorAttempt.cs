using System;

namespace BusinessObjects;

/// <summary>
/// <b>#239:</b> one attempt the learner has already made on this exam, so a prior submission can be
/// re-opened from the instructions page. The route is decided here rather than in the view because the
/// path segment and the attempt's status are one fact: a page that re-derives "/writing/review/{id}"
/// will eventually point a graded attempt at a sitting that no longer exists.
/// </summary>
/// <param name="Status">The stored code (in_progress, submitted, graded, abandoned). The page shows a
/// label from the registry; this code is only ever compared.</param>
/// <param name="CanReview">Graded, so the feedback exists.</param>
/// <param name="SubmittedAt">When the answer was dispatched for grading; null while still open.</param>
public sealed record PriorAttempt(
    int AttemptId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    bool CanReview)
{
    /// <summary>
    /// Where this attempt re-opens under <paramref name="skillPath"/> ("/writing" or "/speaking"), or
    /// null when nothing can be opened. A graded attempt goes to its feedback and a submitted one to
    /// the grading page it is waiting on; an open one goes back into the sitting, the only page that
    /// can finish it. An abandoned attempt has neither, so it is listed as a fact and links nowhere
    /// rather than inventing a route.
    /// </summary>
    public string? ReopenPath(string skillPath) => Status switch
    {
        "graded" => $"{skillPath}/review/{AttemptId}",
        "submitted" => $"{skillPath}/pending/{AttemptId}",
        "in_progress" => $"{skillPath}/take/{AttemptId}",
        _ => null,
    };
}
