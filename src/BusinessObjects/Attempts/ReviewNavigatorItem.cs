namespace BusinessObjects;

public sealed record ReviewNavigatorItem(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    Modality Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool? IsCorrect);
