namespace Contracts;

public sealed record AttemptReviewRubricScoreDto(
    string CriterionCode,
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Feedback);
