namespace Contracts;

public sealed record NavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    ModalityDto Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent,
    bool IsLocked = false);
