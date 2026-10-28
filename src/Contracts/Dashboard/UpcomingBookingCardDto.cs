
namespace Contracts;

public sealed record UpcomingBookingCardDto(
    int BookingId,
    string MentorName,
    string? MentorHeadline,
    string? FocusArea,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? MeetLink);
