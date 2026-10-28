using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record PracticeSessionTake(
    PracticeSessionInfo Session,
    PracticeQuestionView Question,
    IReadOnlyList<PracticeQuestionView> Questions,
    IReadOnlyList<PracticeNavigatorItem> Navigator);
