namespace Contracts;

public sealed record ReviewNavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    ModalityDto Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool? IsCorrect);
