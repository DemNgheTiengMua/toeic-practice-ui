
namespace BusinessObjects;

public sealed record UpcomingBookingCard(
    int BookingId,
    string MentorName,
    string? MentorHeadline,
    string? FocusArea,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? MeetLink);
