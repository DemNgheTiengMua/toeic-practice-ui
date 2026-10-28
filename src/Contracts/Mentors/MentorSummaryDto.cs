using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record MentorSummaryDto(
    int MentorId,
    int? UserId,
    string DisplayName,
    string? Bio,
    int CertId,
    string CertCode,
    string CertName,
    int? SkillId,
    string? SkillCode,
    string? SkillName,
    string? Modality,
    string Type,
    decimal PricePerSlot,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive,
    decimal? AvgRating,
    int ReviewCount);
