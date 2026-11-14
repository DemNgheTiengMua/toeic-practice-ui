namespace Contracts;

public sealed record MentorSummaryDto(
    int MentorId,
    string DisplayName,
    string? Bio,
    int CertId,
    string CertCode,
    string CertName,
    int? SkillId,
    string? SkillCode,
    string? SkillName,
    string? Modality,
    string MeetLink,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive,
    decimal? AvgRating,
    int ReviewCount,
    IReadOnlyList<MentorWeeklySlotDto>? WeeklySlots = null);
