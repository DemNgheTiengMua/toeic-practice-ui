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
    int? ItemVersionId = null)
{
    /// <summary>#187: the same address <see cref="PracticeQuestionDto"/> uses — the question row for a
    /// section drill, the pinned version for a pool item.</summary>
    public int Address => QuestionId ?? ItemVersionId ?? 0;
}
