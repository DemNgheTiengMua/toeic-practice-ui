namespace Contracts;

public sealed record MentorWeeklySlotDto(
    int WeeklySlotId,
    int MentorId,
    int DayOfWeek,
    string StartTime,
    string EndTime);

public sealed record MentorWeeklySlotInputDto(
    int DayOfWeek,
    string StartTime,
    string EndTime);

public sealed record ReplaceMentorWeeklySlotsRequest(
    IReadOnlyList<MentorWeeklySlotInputDto> WeeklySlots);
