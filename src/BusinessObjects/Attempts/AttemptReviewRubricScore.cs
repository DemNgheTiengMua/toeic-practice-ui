namespace BusinessObjects;

/// <summary>
/// #375: one AI rubric mark as the review renders it — the criterion, the mark, and the ceiling that
/// mark was given against (a section override when there is one).
/// </summary>
public sealed record AttemptReviewRubricScore(
    string CriterionCode,
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Feedback);
