using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record AdminMentorListItemDto(
    int MentorId,
    int? UserId,
    string DisplayName,
    string Type,
    int CertId,
    string CertCode,
    int? SkillId,
    string? SkillName,
    decimal PricePerSlot,
    int PriceCredits,
    int SessionMinutes,
    decimal? AvgRating,
    int ReviewCount,
    int TotalSlotsCount,
    int OpenSlotsCount,
    bool IsActive);
