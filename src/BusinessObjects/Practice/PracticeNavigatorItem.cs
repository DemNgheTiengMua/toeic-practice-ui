using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #187: one cell of a drill's navigator. <see cref="QuestionId"/> is NULL for a pool item, which is
/// addressed by <see cref="ItemVersionId"/> instead.
/// </summary>
public sealed record PracticeNavigatorItem(
    int? QuestionId,
    int DisplayOrder,
    bool IsAnswered,
    bool? IsCorrect,
    bool IsSaved,
    bool IsCurrent,
    int? ItemVersionId = null,
    int? SavedQuestionId = null);
