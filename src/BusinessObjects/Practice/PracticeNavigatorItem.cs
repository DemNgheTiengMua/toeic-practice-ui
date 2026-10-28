using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record PracticeNavigatorItem(
    int QuestionId,
    int DisplayOrder,
    bool IsAnswered,
    bool? IsCorrect,
    bool IsSaved,
    bool IsCurrent);
