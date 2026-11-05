namespace Contracts;

/// <summary>#187: <paramref name="QuestionId"/> is NULL for a pool item, addressed by
/// <paramref name="ItemVersionId"/> instead.</summary>
public sealed record PracticeNavigatorItemDto(
    int? QuestionId,
    int DisplayOrder,
    bool IsAnswered,
    bool? IsCorrect,
    bool IsSaved,
    bool IsCurrent,
    int? ItemVersionId = null);
