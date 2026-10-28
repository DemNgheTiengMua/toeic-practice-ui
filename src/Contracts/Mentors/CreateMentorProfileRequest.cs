using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record CreateMentorProfileRequest(
    int? UserId,
    string DisplayName,
    string? Bio,
    int CertId,
    int? SkillId,
    string Type,
    decimal PricePerSlot,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive);
