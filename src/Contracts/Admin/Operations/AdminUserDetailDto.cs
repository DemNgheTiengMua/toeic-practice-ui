namespace Contracts;

public sealed record AdminUserPlacementDto(
    int PlacementId,
    string CertCode,
    string ExamName,
    PlacementStatusDto Status,
    PlacementScopeDto Scope,
    bool Paid,
    int CreditsCharged,
    int? TotalScore,
    string? ResultBandCode,
    DateTimeOffset TakenAt);

public sealed record AdminUserDetailDto(
    AdminUserDto User,
    IReadOnlyList<AdminUserPlacementDto> Placements);
