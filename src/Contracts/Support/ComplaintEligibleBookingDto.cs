
namespace Contracts;

public sealed record ComplaintEligibleBookingDto(
    int BookingId,
    string MentorName,
    DateTimeOffset? ScheduledAt,
    string Status);
