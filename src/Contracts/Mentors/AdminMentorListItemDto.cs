namespace Contracts;

public sealed record AdminMentorListItemDto(
    int MentorId,
    string DisplayName,
    int CertId,
    string CertCode,
    int? SkillId,
    string? SkillName,
    string MeetLink,
    int PriceCredits,
    decimal PriceVnd,
    int SessionMinutes,
    decimal? AvgRating,
    int ReviewCount,
    int TotalSlotsCount,
    int OpenSlotsCount,
    int CompletedSessionsCount,
    int TotalEarnedCredits,
    decimal TotalEarnedVnd,
    IReadOnlyList<MentorWeeklySlotDto> WeeklySlots,
    bool IsActive);
