using Contracts;
namespace WebMVC.Models;

/// <summary>
/// #232: the lesson reader. A lesson step opens <c>/lessons/{id}</c> directly — there is no module to
/// carry in the query string, because a path step is a coordinate rather than a member of one.
/// </summary>
public sealed record LessonViewModel(LessonDetailDto Lesson, LessonProgressDto Progress)
{
    public IReadOnlyList<LessonCardPresentation> Cards { get; } = Lesson.Cards.OrderBy(c => c.DisplayOrder)
        .Select((c, i) => new LessonCardPresentation(c, i + 1)).ToArray();
    public int InitialIndex => Progress.LastViewedCardId is { } id
        ? Cards.FirstOrDefault(c => c.Card.CardId == id)?.Index ?? 1 : 1;
    public string FinishUrl => "/learning-path";
    public string ReturnUrl => $"/admin/lessons/{Lesson.LessonId}/edit";
}
