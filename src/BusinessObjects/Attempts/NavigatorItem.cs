namespace BusinessObjects;

public sealed record NavigatorItem(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    Modality Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent,
    bool IsLocked = false);
