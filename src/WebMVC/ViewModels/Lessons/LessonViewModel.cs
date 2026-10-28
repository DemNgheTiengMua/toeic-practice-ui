using Contracts;
namespace WebMVC.Models;

public sealed record LessonViewModel(LessonDetailDto Lesson, LessonProgressDto Progress, int? ModuleId = null)
{
    public IReadOnlyList<LessonCardPresentation> Cards { get; } = Lesson.Cards.OrderBy(c => c.DisplayOrder)
        .Select((c, i) => new LessonCardPresentation(c, i + 1)).ToArray();
    public int InitialIndex => Progress.LastViewedCardId is { } id
        ? Cards.FirstOrDefault(c => c.Card.CardId == id)?.Index ?? 1 : 1;
    public string FinishUrl => ModuleId.HasValue ? $"/learning-path/modules/{ModuleId.Value}" : "/learning-path";
    public string ReturnUrl => $"/admin/lessons/{Lesson.LessonId}/edit";
}
