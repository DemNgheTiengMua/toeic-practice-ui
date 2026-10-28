using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record UpdateMentorProfileRequest(
    int? UserId,
    string DisplayName,
    string? Bio,
    int CertId,
    int? SkillId,
    decimal PricePerSlot,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive);
