using System;

namespace Contracts;

/// <summary>
/// #239: one attempt the learner has already made on this exam, so a prior submission is listed and
/// re-openable. <see cref="ReopenPath"/> is computed by the service, not by the view: the path segment
/// and the attempt's status are one fact.
/// </summary>
public sealed record PriorAttemptDto(
    int AttemptId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    bool CanReview,
    string? ReopenPath);
