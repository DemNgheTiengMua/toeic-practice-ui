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
    /// <summary>
    /// #352: delivered sessions excluded from the totals above because their complaint was resolved with
    /// compensation. Paired with <see cref="AdjustedCredits"/> so a smaller total is explained, not
    /// mysterious.
    /// </summary>
    int AdjustedSessionsCount,
    int AdjustedCredits,
    IReadOnlyList<MentorWeeklySlotDto> WeeklySlots,
    bool IsActive);
