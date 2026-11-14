namespace Contracts;

public sealed record UpdateMentorProfileRequest(
    string DisplayName,
    string? Bio,
    int CertId,
    int? SkillId,
    string MeetLink,
    int PriceCredits,
    string? Headline,
    string? Expertise,
    int SessionMinutes,
    bool IsActive,
    IReadOnlyList<MentorWeeklySlotInputDto>? WeeklySlots = null);
