namespace BusinessObjects;

public sealed record PlacementCatalogItem(
    int ExamId,
    int CertId,
    string CertCode,
    string Name,
    string? Description,
    int DurationMinutes,
    int QuestionCount,
    bool IsRecommended,
    int? ActivePlacementId);
