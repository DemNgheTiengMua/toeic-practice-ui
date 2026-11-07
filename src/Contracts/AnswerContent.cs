namespace Contracts;

/// <summary>
/// #253: the one definition of "this answer carries content", shared by the sitting, the review, the
/// practice drill and the screens that render them.
/// <para>
/// Answered is <b>not</b> "graded". A productive pool answer is stored with <c>IsCorrect</c> null on
/// purpose — it is not judged — and reading that null as "not answered" made one attempt report two
/// different answer counts, left the review screen blank where a grade existed, and meant a pool drill
/// could never finish. A row is answered when it holds an option id, a pool label, a response text or a
/// multi-select id list.
/// </para>
/// </summary>
public static class AnswerContent
{
    public static bool IsAnswered(
        int? selectedOptionId,
        string? selectedLabel,
        string? responseText,
        string? selectedOptionIdsJson) =>
        selectedOptionId.HasValue
        || !string.IsNullOrWhiteSpace(selectedLabel)
        || !string.IsNullOrWhiteSpace(responseText)
        || !string.IsNullOrWhiteSpace(selectedOptionIdsJson);
}
