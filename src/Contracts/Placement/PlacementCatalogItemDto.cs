namespace Contracts;

public sealed record PlacementCatalogItemDto(
    int ExamId,
    int CertId,
    string CertCode,
    string Name,
    string? Description,
    int DurationMinutes,
    int QuestionCount,
    bool IsRecommended,
    int? ActivePlacementId);
