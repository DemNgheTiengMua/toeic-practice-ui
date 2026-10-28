namespace Contracts;

public sealed record PracticeSessionTakeDto(
    PracticeSessionDto Session,
    PracticeQuestionDto Question,
    IReadOnlyList<PracticeQuestionDto> Questions,
    IReadOnlyList<PracticeNavigatorItemDto> Navigator);
