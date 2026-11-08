using Contracts;

namespace WebMVC.Models;

public sealed record WritingReviewViewModel(
    WritingReviewDto Review,
    string? ErrorMessage = null)
{
    /// <summary>
    /// #220: the category column used to print <c>@span.Category</c> - the grader's own vocabulary
    /// (<c>grammar</c>, <c>cohesion</c>) straight into a table cell. The label is looked up per span
    /// here, keyed by question then span position, because a span carries no id of its own.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> ErrorCategoryLabels { get; } = Review.Questions
        .ToDictionary(
            q => q.QuestionId,
            q => (IReadOnlyList<string>)(q.ErrorSpans ?? [])
                .Select(span => WritingErrorCategoryLabels.LabelForCode(span.Category, NullLog))
                .ToList());

    private static readonly Microsoft.Extensions.Logging.ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
