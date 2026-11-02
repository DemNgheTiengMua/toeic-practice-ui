
namespace BusinessObjects;

/// <summary>A sitting the learner has not finished. <paramref name="PlacementId"/> is the id the
/// placement sitting route needs - a placement is reached by <c>/placement/take/{placementId}</c>,
/// and its <see cref="AttemptId"/> is a different key (#159 review).</summary>
public sealed record RecentAttempt(
    int AttemptId,
    string ExamName,
    ExamPurpose Purpose,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode,
    int? PlacementId = null);
