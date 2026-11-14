namespace Contracts;

public sealed record MentorDetailDto(
    MentorSummaryDto Mentor,
    IReadOnlyList<MentorWeeklySlotDto> WeeklySlots,
    IReadOnlyList<MentorSlotDto> AvailableSlots,
    IReadOnlyList<MentorReviewDto> Reviews);
