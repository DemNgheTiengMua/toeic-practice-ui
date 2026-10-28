
namespace Contracts;

/// <summary>Summary of a certificate for admin list view.</summary>
public sealed record AdminCertificateSummaryDto(
    int CertId,
    string Code,
    string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    int SkillsCount,
    int SectionsCount,
    int ExamsCount);
